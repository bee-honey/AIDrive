# AIDrive — Design Decisions

This document records the main design decisions, what we considered instead, and what each choice costs us. Add a new entry whenever a decision changes. Don't edit an old entry to say something different; mark it **Superseded** instead.

Status: **Accepted** · **Proposed** · **Superseded**

---

## D1. Split the agent from the driver

**Status:** Accepted

**Context:** The agent needs to make driving decisions, like where to go and what to do when a road is blocked. A car also needs control inputs 30–60 times per second.

**Decision:** Use two layers. The LLM agent makes high-level decisions: goals, routes, recovery, and when the trip is finished. Deterministic C# code does the low-level control: steering, throttle, braking, and lane following.

**Alternatives considered:**
- *The LLM steers every frame.* This is too slow (hundreds of milliseconds per decision), too expensive, not repeatable from run to run, and unsafe.
- *No LLM, just a scripted state machine.* This works as a driver but isn't an agent: it can't take natural-language goals or handle new situations.

**Consequences:** The agent only runs when something happens, such as a new goal, a blocked road, arriving, or getting stuck. The driver works on its own, so the simulator can be demoed and tested without the LLM.

---

## D2. The tool API is the contract

**Status:** Accepted

**Decision:** The agent talks to the vehicle only through a small set of tools:

| Tool | Purpose |
|------|---------|
| `list_landmarks()` | Named destinations and where they are |
| `get_vehicle_state()` | Position, speed, current street, sensor readings, driving state |
| `plan_route(destination, avoid?)` | Computes a route; can avoid certain streets |
| `drive_route(route)` | Starts the car on the route |
| `stop_vehicle()` | Brakes to a stop |
| `replan_route()` | Computes a new route from where the car is now |
| events | `blocked`, `arrived`, `stuck`, `collision` |

**Consequences:**
- The Unity side and the Python side can be built at the same time against this agreed API.
- A Raspberry Pi car can serve the same API later, so the agent code carries over from simulation to the real car without changes.
- The evaluation framework drives the system through the same interface.

---

## D3. Unity ↔ Python over local HTTP/JSON

**Status:** Proposed (to be confirmed at Milestone 8)

**Decision:** Unity runs a small HTTP server on `localhost`. Each tool is one JSON endpoint. Events are fetched by polling or streamed as server-sent events.

**Alternatives considered:**
- *WebSocket.* Two-way and lower latency, but harder to test by hand. We don't need it because the agent makes decisions only occasionally.
- *gRPC.* Strongly typed, but adds code generation and build complexity that isn't worth it in a 10-day project.
- *Unity ML-Agents.* Designed for reinforcement learning, not for an LLM calling tools.

**Consequences:** Endpoints can be tested with `curl`, logs are easy to read, and a Raspberry Pi can run the same server with Flask or FastAPI.

---

## D4. Road graph + A* instead of NavMesh

**Status:** Accepted

**Context:** Cars must stay in lanes, drive on the right, and turn only at intersections. Unity's NavMesh is designed for walking characters.

**Decision:** The road graph has a node at every intersection and landmark and an edge for every road segment between them. Routing uses A*, with a rule that stops the route from U-turning at a node.

**Turn penalty:** On a grid, many routes have the same shortest length. Plain A* can pick a zig-zag "staircase" with 7 turns. Each turn therefore adds 20 m to A*'s cost, so among routes of about the same length the one with the fewest turns wins. Home → Hospital went from 7 turns to 2 with no extra distance.

**Consequences:**
- A route reads as a list of street names ("E2 → S6 → E6"). That's easy for an LLM to understand and explain.
- Blocking a road means switching off one edge, which makes rerouting cheap.
- The graph is built from the city grid settings, so it has to be regenerated whenever the city layout changes.

---

## D5. Simple steering model instead of WheelColliders

**Status:** Accepted

**Decision:** The car uses a Rigidbody moved by a simple steering model (known as a kinematic bicycle model). Speed and steering angle set the velocity and turning rate. Wheels still turn and spin visually.

**Alternatives considered:** *WheelColliders.* They're more realistic, but hard to tune, they bounce and slide, and the car can behave differently between runs, which makes evaluations unreliable.

**Consequences:** The car behaves the same every run and is easy to tune, but it doesn't skid or have suspension. That's fine for a flat city and closer to how a small robot car actually moves.

---

## D6. Following the route: pure pursuit + speed profile

**Status:** Superseded by D15

**Decision:**
- The route becomes a line of points in the right-hand lane. At intersections the points follow a smooth curve.
- **Pure pursuit** steering: the car steers toward a point a short distance ahead on that line. The distance grows with speed.
- **Speed profile:** cruise on straights, slow down before turns, and brake to stop exactly at the destination, a stop line, or before an obstacle.

**Consequences:** Pure pursuit is simple and reliable, and it also works on the physical car, where the camera or lane detection can supply the line to follow.

---

## D7. Raycast sensors

**Status:** Accepted

**Decision:** A fan of raycasts: 7 at the front, 1 on each side, and 1 at the rear. The readings (distance and what was hit) go into the vehicle state JSON.

**Consequences:** It's cheap and easy to understand, and it's roughly what cheap ultrasonic or LiDAR sensors give a real robot car. A camera-based stack can be added later.

---

## D8. Discover blocked roads by sensing them

**Status:** Accepted

**Decision:** Road closures are **not** marked on the map in advance. The car finds them with its sensors, reports a `blocked` event, and the road is marked blocked in the graph. The car then does a 3-point turn and gets a new route.

**Consequences:**
- The agent has something real to react to, which gives the evaluations meaningful rerouting scenarios.
- The C# code also reroutes on its own as a fallback, so the demo keeps working when the agent isn't running. With the agent connected, the agent decides.

---

## D9. Build scenery from code

**Status:** Accepted

**Decision:** The city, props, and car are generated by C# scripts from Unity primitives, using a fixed random seed.

**Alternatives considered:** Imported city and car asset packs look better, but they're large, their licensing varies, and every scenario would have to be laid out by hand.

**Consequences:** Every layout can be recreated exactly, the repo stays small, and the evaluation framework can generate scenarios automatically. Imported models can still replace the visuals later without changing any of the logic.

---

## D10. On-screen controls

**Status:** Accepted

**Decision:** The in-game controls (command box, buttons, status panel, minimap) use Unity's IMGUI.

**Context:** The project uses only the new Input System, so the older `UnityEngine.Input` API isn't available.

**Consequences:** No keyboard or controller setup is needed, and the controls work in the Editor and in builds. This is fine for a developer-facing demo but not a polished interface.

---

## D11. Where RAG fits

**Status:** Accepted (stretch goal)

**Decision:** RAG is not used for moving the car, because there's nothing useful to look up when choosing a steering angle. It's saved for situations where knowledge helps, such as looking up traffic rules or what a road sign means (e.g. "Construction Zone, Local Access Only").

---

## D12. Keep secrets and per-developer files out of git

**Status:** Accepted

**Decision:** `.mcp.json` (each developer's own MCP server address), `.claude/settings.local.json`, Unity's `Library/`, `Temp/`, `Logs/` and `UserSettings/` folders, and IDE files are all git-ignored. API keys for the LLM will go in a `.env` file that is also git-ignored.

---

## D13. Name streets by grid position

**Status:** Accepted

**Decision:** North–south roads are named **S1–S7**, numbered west to east. East–west roads are named **E1–E7**, numbered south to north. An intersection is named after the two roads that cross there, e.g. `S3 & E4`.

**Alternatives considered:** Real-sounding names such as "Main St" and "3rd Ave" feel more natural, but you have to look them up to know where they are.

**Consequences:** A location like "on E2 between S3 & S4, heading east" tells you where the car is and which way it's going without a map. That's easier for people debugging, for the LLM reasoning about routes, and for reading logs. The letter tells you which way a road runs; the number tells you where it is.

---

## D14. 3D text that respects depth

**Status:** Accepted

**Decision:** World-space labels use Unity's built-in `TextMesh` with a custom shader, `AIDrive/Text3D`. It's the same as Unity's text shader except that it checks depth.

**Context:** Unity's built-in text shader draws on top of everything, so labels showed through buildings. TextMeshPro would also work, but it needs extra resources imported into the project.

---

## D15. Stanley steering instead of pure pursuit

**Status:** Accepted (replaces D6's steering method; the speed profile is unchanged)

**Context:** With pure pursuit, the car cut into the tight right turns (about 4.4 m radius) and then swung up to 1.7 m wide on the way out. That was too far out of lane on a 3 m lane.

**Decision:** Use the **Stanley** controller, the method Stanford's DARPA Grand Challenge car used. It steers using three things:
- the angle between the car's heading and the path,
- how far the front axle is from the path, which is corrected harder at low speed,
- the upcoming curve, read about 0.3 s ahead so the car starts turning in time.

**Result:** the worst lane error dropped from 1.6 m to at most 0.8 m on every test drive, with no collisions.

**Other changes made while tuning:**
- Turn curves now start 10 m before the intersection centre instead of 8 m, which makes right turns less tight.
- Pulling over now follows a smooth 14 m S-curve.
- Pulling out from the curb merges into the lane within 12 m.
- The car counts as arrived within 1.5 m of the stop point.

---

## D16. Sensors are checked against the route, not the car's heading

**Status:** Accepted

**Context:** Raw rays hit buildings at every corner and hit poles during turns. Braking whenever a ray hit something would stop the car at every intersection.

**Decision:** Each hit point is projected onto the planned path, giving a distance along the path and a sideways offset from it. A hit only counts if it's ahead and within **1.45 m** of a lane centre: half the car's width plus a margin. The same check with the centre moved 2.6 m to the right tells us about the neighbouring same-direction lane.

**Lane probes:** Two extra rays run parallel to the car, one lane to each side, with a 45 m range. When a probe sees nothing, the lane counts as clear only **up to the probe's range**, never "clear forever". Treating "nothing seen" as "clear forever" was a real bug during M3: the car moved over into a lane that was also blocked, because the wall was 0.03 m beyond the probe's original 30 m range.

**Blocked:** Stopped behind an obstacle for 2 s, with no free lane → `Blocked`. The car **waits**. It doesn't turn around or pick a new route by itself here: that decision belongs to the planner (M6) and later the agent, as D1 intends. It continues if the obstacle goes away.

**The ray list is defined in code**, not saved in the prefab. That way, tuning the default rays always reaches the car in the scene.

