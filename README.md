# AIDrive

**An agentic AI that drives a car through a simulated city from a natural-language destination.**

> "Take me to Park B."

The agent works out where Park B is, plans a route, and sends the car. While the car drives, the agent watches for problems: a closed road, an obstacle, a red light. It decides what to do and replans when needed, until the car pulls up at the curb.

AIDrive is an AI course capstone. The simulator is built in Unity. The long-term goal is to run the same agent on a real 3D-printed car with a Raspberry Pi and a camera.

---

## How it works

```
 User ─ "Take me to Park B"
          │
 ┌────────▼─────────┐   Python (LangGraph + LLM)
 │  Driving Agent   │   plan · call tools · observe events · replan
 └────────┬─────────┘
          │ HTTP/JSON tools
 ┌────────▼─────────┐   Unity (C#)
 │  Vehicle API     │   get_state · list_landmarks · plan_route
 │                  │   drive_route · stop · replan
 ├──────────────────┤
 │ Autopilot        │   steering · speed · lane keeping · braking
 │ Sensors          │   raycast fan · obstacle detection
 │ Road graph + A*  │   streets · intersections · landmarks
 │ City + props     │   cones · barriers · traffic lights
 └──────────────────┘
```

The system has two layers:

- **The agent (Python)** is the high-level brain. It understands the goal, calls tools, reacts to events, and chooses when to reroute. It makes decisions only when something happens, never every frame.
- **The simulator (Unity/C#)** is the low-level driver. It handles steering, throttle, braking, lane following, and sensing, and behaves the same way every run.

The tool API is the contract between the two layers. A physical car can serve the same API later, so the agent code doesn't need to change.

See [docs/DESIGN.md](docs/DESIGN.md) for the design decisions and why we made them.

## Features

| Area | Features |
|------|----------|
| City | 6×6 block grid, two-lane roads in each direction, lane markings, crosswalks, buildings, parks |
| Navigation | Named streets and landmarks, road graph, A* routing |
| Vehicle | Autopilot that keeps its lane and turns smoothly, raycast sensors, emergency braking |
| Scenarios | Cones, barriers, road closures, parked cars, traffic lights |
| Agent | Natural-language goals, tool calls, rerouting around blocked roads |
| Evaluation | Automated scenario runs that measure success, collisions, route efficiency, and latency |

Progress is tracked milestone by milestone in [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md).

## Getting started

### Requirements

- **Unity 6000.6.2f1** (Unity 6) with the Universal Render Pipeline
- Git
- *(from Milestone 9)* Python 3.11+ for the agent

### Run the simulator

1. Clone the repo:
   ```bash
   git clone git@github.com:bee-honey/AIDrive.git
   ```
2. Open the folder in Unity Hub with editor version **6000.6.2f1**. The first import takes a few minutes because Unity rebuilds `Library/`.
3. Open `Assets/Scenes/SampleScene.unity`.
4. Press **Play**.

### Optional: AI-assisted editor tooling

The project includes the [AI Game Developer](https://ai-game.dev) Unity MCP plugin, which lets AI coding assistants edit the scene. Each developer needs their own server address. Put it in a local `.mcp.json` file, which is git-ignored and must never be committed:

```json
{
  "mcpServers": {
    "ai-game-developer": { "type": "http", "url": "<your-personal-mcp-url>" }
  }
}
```

## Repository layout

```
AIDrive/
├── Assets/
│   ├── City/Materials/     # URP materials for roads, buildings, props
│   ├── Scenes/             # SampleScene — the city
│   └── Plugins/            # Unity MCP plugin
├── docs/
│   ├── DESIGN.md           # architecture & design decisions
│   └── IMPLEMENTATION.md   # milestone plan & progress tracker
├── Packages/               # Unity package manifest
├── ProjectSettings/        # Unity project settings
└── .claude/skills/         # AI assistant skills for Unity editor automation
```

## Roadmap

1. ✅ City and roads
2. Map with named streets and landmarks, road graph, A*
3. Car and autopilot
4. Raycast sensors
5. Props and scenarios
6. Traffic lights
7. Rerouting
8. In-game controls and minimap
9. Unity ↔ Python HTTP bridge
10. LangGraph driving agent
11. Evaluation framework
12. *Stretch:* RAG over traffic rules, and a Raspberry Pi car

## License

[Apache 2.0](LICENSE)
