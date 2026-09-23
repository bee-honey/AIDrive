# AIDrive — Implementation Plan

**Autonomous Driving Agent in Unity** — an agentic AI system that accepts natural-language navigation goals ("Take me to Park B") and autonomously drives a simulated vehicle through a Unity city, obeying signals, avoiding obstacles, and replanning when roads are blocked.

> Long-term goal: run the same agent against a real 3D-printed Raspberry Pi + camera car by swapping the backend behind the same tool API.

---

## Progress

| # | Milestone | Status |
|---|-----------|--------|
| 0 | City + roads | ✅ Done |
| 1 | Map semantics (street names, landmarks, road graph, A*) | ⬜ Not started |
| 2 | Car + autopilot | ⬜ Not started |
| 3 | Raycast sensors | ⬜ Not started |
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

### M1 — Map semantics
- [ ] Street names (e.g. avenues N–S, streets E–W)
- [ ] Named landmarks: Home, Office, Hospital, School, Gas Station, Mall, Park A, Park B
- [ ] Landmark signs / labels visible in the scene
- [ ] Road graph: intersection nodes + landmark nodes, edges with lengths
- [ ] A* pathfinding with no-U-turn constraint
- [ ] Editor gizmo visualization of graph and a debug route

**Acceptance:** a route between any two landmarks can be computed and drawn in the Scene view.

### M2 — Car + autopilot
- [ ] Procedural car (body, cabin, 4 wheels that steer/spin)
- [ ] Rigidbody + kinematic bicycle model (no WheelColliders)
- [ ] Route → lane-offset waypoints (right-hand traffic), smooth Bézier turns
- [ ] Pure-pursuit steering, speed profile (slow for turns, stop at destination)
- [ ] Pull over at destination curb
- [ ] Chase camera + overview camera

**Acceptance:** car drives Home → Park B without collisions.

### M3 — Raycast sensors
- [ ] Front fan (7 rays), side (2), rear (1); debug-drawn
- [ ] Emergency braking based on front clearance
- [ ] Lane-change avoidance for partial obstructions (same-direction lane)
- [ ] "Blocked" detection when both lanes obstructed
- [ ] Sensor + vehicle state exposed as JSON

**Acceptance:** car brakes for any object placed ahead; state JSON is readable.

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

| Decision | Choice | Why |
|----------|--------|-----|
| Vehicle physics | Rigidbody + bicycle model | Predictable, easy to tune; flat city doesn't need WheelColliders |
| Pathfinding | Road graph + A* | NavMesh is designed for walking agents, not lane-following cars |
| Input | IMGUI buttons/text box | Project uses the new Input System only; avoids legacy `Input` |
| Python ↔ Unity | Local HTTP/JSON server in Unity | Debuggable with `curl`, maps 1:1 to agent tools, same API on a Pi |
| LLM role | High-level decisions only | Per-frame LLM control is slow, costly and unreliable |

## Open questions

- [ ] Final landmark names
- [ ] Primitive car vs imported car model
- [ ] Traffic lights in MVP or stretch
- [ ] Team split (suggested: Unity M1–M7 / Python M9–M10, M8 API as the contract)

## Environment

- Unity 6000.6.2f1, URP 17.6, Input System 1.20
- Unity MCP (AI Game Developer) for editor automation
