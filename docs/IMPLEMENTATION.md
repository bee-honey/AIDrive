# AIDrive — Implementation Plan

**Autonomous Driving Agent in Unity** — an agentic AI system that accepts natural-language navigation goals ("Take me to Park B") and autonomously drives a simulated vehicle through a Unity city, obeying signals, avoiding obstacles, and replanning when roads are blocked.

> Long-term goal: run the same agent against a real 3D-printed Raspberry Pi + camera car by swapping the backend behind the same tool API.

---

## Progress

| # | Milestone | Status |
|---|-----------|--------|
| 0 | City + roads | ✅ Done |
| 1 | Map semantics (street names, landmarks, road graph, A*) | ✅ Done |
| 2 | Car + autopilot | ✅ Done |
| 3 | Raycast sensors | ✅ Done |
| 4 | Props & scenarios | ✅ Done |
| 5 | Traffic lights | ✅ Done |
| 6 | Replanning primitives + baseline policy | ⬜ Not started |
| 7 | In-game UI & minimap | ⬜ Not started |
| 8 | Unity ↔ Python bridge | ⬜ Not started |
| 9 | LangGraph agent | ⬜ Not started |
| 10 | Evaluation framework | ⬜ Not started |
| 11 | Stretch: RAG / Raspberry Pi car | ⬜ Not started |

Status legend: ⬜ Not started · 🟨 In progress · ✅ Done

---

## Architecture

```
 User ─ "Take me to Park B"
          │
 ┌────────▼─────────┐   Python (LangGraph + LLM)
 │  Driving Agent   │   plan · call tools · observe events · replan
 └────────┬─────────┘   (decides per event, never per frame)
          │ HTTP/JSON tools
 ┌────────▼─────────┐   Unity (C#) — deterministic
 │  Vehicle API     │   get_state, list_landmarks, plan_route,
 │                  │   drive_route, stop, replan
 ├──────────────────┤
 │ Autopilot        │   steering, speed, lane keeping, braking
 │ Sensors          │   raycast fan, obstacle detection
 │ Road graph + A*  │   streets, intersections, landmarks
 │ City + props     │   cones, barriers, traffic lights
 └──────────────────┘
```

**Key principle:** the LLM is the high-level brain (goals, routing decisions, recovery). C# is the low-level driver (steering, throttle, braking). The LLM never steers.

**Sim-to-real:** the tool API is the contract. A Raspberry Pi car can later expose the same endpoints, so the agent code doesn't change.

---

## Milestones

### ✅ M0 — City + roads
- [x] 6×6 block grid (40 m blocks, 12 m roads, 324 m × 324 m)
- [x] Two-lane-each-way roads, double-yellow centre, dashed dividers, crosswalks
- [x] Procedural buildings (taller downtown), 2 parks with trees
- [x] URP materials in `Assets/City/Materials/`

### ✅ M1 — Map semantics
- [x] City generator saved in repo (`AIDrive → Generate City`), shared `CityLayout` config
- [x] Grid street names: **S1–S7** run north–south (west → east), **E1–E7** run east–west (south → north); intersections are `S3 & E4`
- [x] Street-name signs at all 49 intersections + road names painted at the city edges
- [x] Named landmarks: Home, Office, Hospital, School, Gas Station, Mall, Park A, Park B (curb sign, drop-off zone, floating label)
- [x] Road graph: 49 intersection + 8 landmark nodes, 92 edges (street, length, blocked flag)
- [x] A* with no-U-turn constraint, turn penalty, avoid-streets, blocked edges
- [x] Turn-by-turn directions ("At S6 & E2, turn left onto S6 heading north for 208 m")
- [x] Scene-view gizmos for the graph + `RouteDebugger` inspector (From/To/Avoid → Plan Route)
- [x] 13 EditMode tests (`Assets/Tests/EditMode/RoutePlannerTests.cs`)

**Acceptance:** a route between any two landmarks can be computed and drawn in the Scene view. ✅

**Resolved in M2:** the planner now adds a 400 m penalty for arriving with the destination on the left, so every landmark-to-landmark route arrives on the right.

### ✅ M2 — Car + autopilot
- [x] Procedural car prefab (`AIDrive → Create Car`): body, cabin, lights, 4 wheels that steer/spin
- [x] `VehicleController`: Rigidbody + kinematic bicycle model; steer/throttle/brake inputs; collision + distance stats
- [x] `LanePath`: route → right-lane polyline, Bézier turns, merge-out from the curb, S-curve pull-over into the drop-off zone
- [x] `Autopilot`: Stanley steering + curvature feed-forward, curvature-aware speed profile, `Idle → Driving → Arrived`
- [x] Plans from the car's current pose (no U-turn); planner prefers arriving with the destination on the right
- [x] `Localizer`: "on E2 between S3 & S4, heading east (at Home)"
- [x] `CameraRig` (chase / overview) + `DriveHud` test panel (state, speed, location, next turn, landmark buttons)
- [x] 8 new EditMode tests + 3 PlayMode drive tests

**Acceptance:** car drives Home → Park B without collisions. ✅

| Drive | Route | Time | Max lane error | Collisions |
|---|---|---|---|---|
| Home → Hospital | E2 → S7 → E6 (494 m) | 54.6 s | 0.63 m | 0 |
| Home → Park B | E2 → S5 → E3 → S6 (286 m) | 36.7 s | 0.80 m | 0 |
| School → Gas Station | S2 → E1 → S6 (494 m) | 54.6 s | 0.63 m | 0 |

### ✅ M3 — Raycast sensors
- [x] `RaycastSensors`: 7 front rays (0°, ±15°, ±35°, ±60°), 2 side, 1 rear, plus 2 parallel lane probes (45 m); drawn in the Game view
- [x] Path filter: hits are projected onto the route and only count inside the 1.45 m-wide corridor, so buildings are ignored
- [x] Stops 3 m short of obstacles, braking by the deceleration actually needed
- [x] Lane-change avoidance into the other same-direction lane (straight stretches only; never crosses the yellow line)
- [x] `Waiting` → `Blocked` after 2 s; `blocked` event with street, cross streets, distance and obstacle; resumes when cleared
- [x] `VehicleState` JSON (the future `get_vehicle_state()` contract) + HUD toggle
- [x] Test obstacles: "Drop cone ahead" / "Drop blocker ahead" / Clear
- [x] 1 new EditMode test + 3 PlayMode obstacle tests (6 PlayMode total)

**Acceptance:** car brakes for any object placed ahead; state JSON is readable. ✅

| Scenario | Result |
|---|---|
| Cone in lane on E2 | 1 lane change, passes, arrives, 0 collisions |
| Blocker across both lanes | No lane change attempt, stops 2.6 m short, `Blocked` on E2 between S3 & S4 |
| Blocker removed while blocked | Resumes and arrives, 0 collisions |
| Plain drives (×3) | 0 lane changes (no false obstacles), 0 collisions |

### ✅ M4 — Props & scenarios
- [x] Procedural prop prefabs (`AIDrive → Build Props`): cone (custom mesh), A-frame barrier, ROAD CLOSED barricade, jersey barrier, parked cars (4 colours), street lamp
- [x] `Obstacle` component: sensors and state JSON report the prop type (`"by": "RoadClosed"`) instead of raw object names
- [x] 144 street lamps as permanent city decoration (emissive heads, no real lights)
- [x] `RoadAddress`: place anything by street address (`E2 between S3 & S4, eastbound, inner lane, at 0.5`)
- [x] Scenario JSON files in `Assets/Resources/Scenarios/` + `ScenarioLibrary` (load/validate) + `ScenarioRunner` (run → `ScenarioResult`)
- [x] HUD: scenario picker + Run + pass/fail; "Drop cone" / "Drop road-closed" now use real props
- [x] Parked cars are scenario-only; the default city stays clear
- [x] 11 new EditMode tests (addresses, scenario files, prop library) + 6 PlayMode scenario runs

**Acceptance:** several reproducible test scenarios. ✅

| Scenario | Expected | Result |
|---|---|---|
| `clear_roads` | Arrived | ✅ 54.6 s, 0 lane changes |
| `cones_in_lane` | Arrived, ≥ 2 lane changes | ✅ 57.7 s, 2 lane changes |
| `construction_zone` | Arrived, ≥ 1 lane change | ✅ 56.2 s, stayed in curb lane past barrier + 6 cones |
| `jersey_barrier_on_s7` | Arrived, ≥ 1 lane change | ✅ 56.8 s (avoidance on a northbound street after a turn) |
| `parked_cars_and_cone` | Blocked | ✅ Blocked by Cone on E2 between S3 & S4, no lane change into parked cars |
| `road_closed` | Blocked | ✅ Blocked by RoadClosed, 2.9 m gap (M6 will reroute) |

All with 0 collisions.

### ✅ M5 — Traffic lights
- [x] `TrafficSignal` at the **9 main-road intersections** (S2/S4/S6 × E2/E4/E6; reduced from all 25 inner ones so not every block has a light): two-phase plan (green 10 s → yellow 3 s → all-red 1.5 s, 29 s cycle), fixed offset per intersection, scene-time based (deterministic)
- [x] Far-side signal heads on corner poles + white stop lines before the crosswalks (generated with the city)
- [x] `LanePath.StopPoints`: stop line 8 m before every intersection the route enters
- [x] Autopilot: stops on red; on yellow stops only if ≤ 4 m/s² braking is enough, otherwise commits to going through; `StoppedAtLight` state
- [x] Counts red-light stops, time waiting, and **violations** (crossing a stop line on red); any violation fails a scenario
- [x] State JSON: `next_signal`, `red_light_stops`, `red_light_violations`, `time_at_lights_s`; HUD shows the next signal in colour
- [x] `Force(axis, state)` API for tests and future scenarios
- [x] 4 new EditMode tests + 2 new PlayMode tests; all 14 PlayMode drives pass with live signals

**Acceptance:** car waits at red, proceeds on green. ✅ Stopped 0.55 m before the stop line on a forced red; 0 violations across all 14 drives.

**Observations for later:**
- With all 25 signals, trips took about twice as long (Home → Hospital: 55 s → 128 s, 4 red-light stops, 57 s waiting); that's part of why we reduced to 9. That's realistic, since E2 isn't a coordinated green wave. It's also a useful signal for the agent ("the fastest route" is no longer just "the shortest route").
- The worst lane error in the lane-change scenarios rose from about 1.2 m to 1.5–1.8 m. That's likely a lane change overlapping with braking for a light. It isn't asserted anywhere yet; I'll look at it when the eval metrics are formalised (M10).
- The full PlayMode suite now runs longer than the MCP tool's time limit. Results are read from `TestResults.xml` instead.

### M6 — Replanning primitives (Unity reports, the agent decides)
- [ ] On `Blocked`: mark the blocked road segment closed in the graph and raise a `blocked` event. Unity does **not** reroute by itself
- [ ] Primitives the decision-maker can call: `replan(avoid?)`, `turn_around()` (3-point turn), `wait()`, `drive_to(other destination)`
- [ ] **Baseline policy** (used only when no agent is connected): "when blocked → turn around → A* reroute"
- [ ] Scenario runner can run with the baseline policy, so every scenario has a no-agent result to compare against

**Acceptance:** with the baseline policy, the car reaches the destination despite a road closure; with the policy off, it stops and waits for a decision.

### M7 — In-game UI & minimap
- [ ] Command box ("take me to park b") + landmark buttons
- [ ] Status panel (state, speed, location, sensors, collisions)
- [ ] Minimap: roads, landmarks, car, route, blocked roads

**Acceptance:** full demo runs with no Python.

### M8 — Unity ↔ Python bridge
- [ ] Local HTTP/JSON server in Unity
- [ ] Endpoints: `get_vehicle_state`, `list_landmarks`, `plan_route`, `drive_route`, `stop_vehicle`, `replan_route`, events
- [ ] API documented in `docs/API.md`

**Acceptance:** all tools callable from `curl` / Python.

### M9 — LangGraph agent (the navigator, not the driver — see D18)
- [ ] Python project with LangGraph + LLM
- [ ] Tools wrapping the HTTP API
- [ ] Agent loop: understand intent → plan → drive → observe events → decide → arrive → explain
- [ ] Judgment tasks: vague / multi-stop goals, passenger preferences ("avoid downtown"), mid-trip goal changes
- [ ] Decides on `blocked`: wait, detour, or change destination; asks the passenger when unsure
- [ ] Reads text traffic reports ("E2 closed for a parade") and plans around them before reaching the closure
- [ ] Explains decisions ("E2 was closed at S3, so I took E3")

**Acceptance:** natural-language requests, including ones needing judgment, drive the car end-to-end.

### M10 — Evaluation framework: agent vs. baseline
**Research question:** *Does an LLM agent reach better outcomes than a rule-based baseline in situations that need judgment?*
- [ ] Seeded scenario generator (20–50 scenarios), including **judgment scenarios**: vague or multi-stop requests, passenger preferences, unreachable destinations, mid-trip changes, text traffic reports that only the agent reads
- [ ] Every scenario run twice: baseline policy vs. agent
- [ ] Metrics: goal success, intent respected, collisions, red-light violations, route efficiency, replan success, tool calls, decision latency, completion time
- [ ] Batch runner + comparison report (where each approach wins, and why)

**Acceptance:** reproducible eval report comparing agent and baseline.

### M11 — Stretch
- [ ] RAG over traffic rules / road signs for unusual situations
- [ ] Raspberry Pi car implementing the same API

---

## Technical decisions

See [DESIGN.md](DESIGN.md) for the full decision records (D1–D12) with context, alternatives and consequences.

## Open questions

- [ ] Final landmark names
- [ ] Primitive car vs imported car model
- [ ] Traffic lights in MVP or stretch
- [ ] Team split (suggested: Unity M1–M7 / Python M9–M10, M8 API as the contract)

## Environment

- Unity 6000.6.2f1, URP 17.6, Input System 1.20
- Unity MCP (AI Game Developer) for editor automation
