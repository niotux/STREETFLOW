# STREETFLOW component architecture

Streetflow and the detector are the two products. The simulator is the environment for developing and evaluating Streetflow. All three communicate through explicit data contracts; they do not share internal vehicle state or an implementation language requirement.

## Responsibilities

| Component | Owns | Inputs and outputs |
| --- | --- | --- |
| Balthasar / Simulator | Scenario configuration, SUMO lifecycle, synthetic observations, and experiment records | Map/demand in; observations, inspector state, results, and tlcapi out |
| Melchior / Streetflow logistics | Traffic light control; currently manual commands and saved target settings | tlcapi discovery/state in; explicit signal commands out |
| Casper / Detector | Future camera calibration, inference, tracking, and traffic measurement | Camera video in; lane/intersection observations out |
| MAGI frontend | Landing page and browser pages for Balthasar and Melchior | Browser interactions and proxied API requests |

The simulator runs independently. City runs start with lights off and road-priority rules; manual tlcapi commands control them. Legacy scenario runs retain default signal programs. Melchior and the frontend run in separate processes. The detector need not run during simulation experiments. There is no automatic logistics management yet.

## Development and real-world operation

```mermaid
flowchart LR
    Scenario["Map and demand scenario"] --> Simulator["Simulator service"]
    Simulator <--> SUMO["SUMO process"]
    Simulator -->|"Traffic observation contract"| App["Streetflow app"]
    Simulator -->|"Inspector data"| Browser["MAGI frontend / Balthasar"]
    Control["MAGI frontend / Melchior"] --> App
    App -->|"tlcapi manual commands"| Simulator
    Cameras["Real cameras"] --> Detector["Detector product"]
    Detector -->|"Same traffic observation contract"| App
    App -.->|"Future product output"| Output["Navigation guidance or signal integration"]
```

In simulation, the simulator fills the detector's producer role. In real-world operation, the detector supplies those observations. Both identify the same kinds of lane, approach, intersection, and measurement region. Map revisions and explicit lane mappings make that correspondence meaningful.

Exact simulated car identities and full routes are available to the inspector and evaluator. Streetflow receives only the measurements its configured sensors could supply. A camera's local track ID is not a city-wide car identity.

## Keep the first implementation small

Use separate C# / .NET 10 servers for Balthasar, Melchior, and the MAGI frontend. Balthasar owns one SUMO child process, one active run, its engine adapter, observations, API handlers, and recording. Melchior uses the shared tlcapi contract and persists its selected target. The frontend serves plain HTML/JavaScript and proxies requests to each backend. REST/JSON, polling, local archives, and PostgreSQL indexing remain the first implementation.

A message broker, distributed scheduler, database cluster, plugin framework, and photorealistic camera renderer are outside the first milestone. Add infrastructure only when a demonstrated requirement needs it.

## Documentation and code boundaries

Each component has a folder under `docs/` and a future code location under `components/`. The runnable original simulator is in `legacy/simulator/`. Research remains in `docs/research/`; images live under `assets/images/`. Historical proposals remain in `docs/archive/` and are not current implementation instructions.

The active simulator and logistics servers use .NET 10; the frontend uses .NET hosting and plain HTML/JavaScript. Preserved Python scripts belong to data preparation and legacy work and do not select the application stack.
