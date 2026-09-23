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
| 4 | Props & scenarios | ⬜ Not started |
| 5 | Traffic lights | ⬜ Not started |
| 6 | Replanning | ⬜ Not started |
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

### M4 — Props & scenarios
- [ ] Traffic cones, barriers, "Road Closed" signs, parked cars, street lamps
- [ ] Scenario presets (normal, cones in lane, road closure, …)
- [ ] "Drop blocker ahead" runtime action

**Acceptance:** several reproducible test scenarios.

### M5 — Traffic lights
- [ ] Signal controllers at the 25 inner intersections (NS/EW phases)
- [ ] Autopilot stops at stop line on red / yellow

**Acceptance:** car waits at red, proceeds on green.

### M6 — Replanning
- [ ] Mark blocked road edge in the graph
- [ ] 3-point turn maneuver
- [ ] Deterministic fallback replan (works without AI)

**Acceptance:** car reaches destination despite a road closure.

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

### M9 — LangGraph agent
- [ ] Python project with LangGraph + LLM
- [ ] Tools wrapping the HTTP API
- [ ] Agent loop: parse goal → plan → drive → observe → replan → arrive
- [ ] Handles constraints ("avoid Main St"), blocked roads, stuck vehicle

**Acceptance:** natural-language commands drive the car end-to-end.

### M10 — Evaluation framework
- [ ] Seeded scenario generator (20–50 scenarios)
- [ ] Metrics: goal success, collisions, route efficiency, replan success, tool calls, decision latency, completion time, safety violations
- [ ] Batch runner + report

**Acceptance:** reproducible eval report comparing runs.

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
