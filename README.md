# STREETFLOW

STREETFLOW contains the three MAGI components and a shared web frontend:

- **Melchior / Streetflow logistics:** controls traffic lights through tlcapi. The current server provides manual access; automatic management remains future work.
- **Casper / Detector:** will turn camera footage into traffic observations using detection, tracking, and lane/camera calibration.
- **Balthasar / Simulator:** wraps SUMO to run traffic, expose observations, and provide a browser inspector and experiment controls.

The separate **MAGI frontend** starts on a component selector with pages for Melchior and Balthasar. Casper is disabled for now.

The simulator on `better-simulation` uses **C# / .NET 10, SUMO 1.28, PostgreSQL, and Docker Compose**, with a plain HTML/JavaScript browser inspector. It also runs headless. Windows, Linux, and macOS are target platforms; Linux native execution and Linux containers have been exercised. Dataset capacity depends on available resources. Existing legacy tools are retained.

Start with the [documentation workflow](docs/README.md), then the [component architecture](docs/components/architecture.md) and [simulator guide](docs/components/simulator/README.md).

## Repository layout

| Path | Contents |
| --- | --- |
| `docs/active/` | Current ideas and plans, grouped by component |
| `docs/components/` | Settled product, contract, and implementation documentation |
| `docs/research/` | Traffic-model literature and Iași infrastructure studies |
| `docs/archive/` | Superseded proposals and completed plans kept for context |
| `assets/images/` | Screenshots and other image assets |
| `components/` | .NET simulator and logistics servers, MAGI frontend, shared contracts, and detector implementation location |
| `scenarios/` | Ready-to-run tiny intersection and central Iași SUMO packages |
| `legacy/simulator/` | Preserved C++ simulator, browser viewer, tools, tests, and map examples |

## Run the simulator

```sh
docker compose up --build -d --wait
curl http://localhost:8080/health
curl http://localhost:8080/api/v1/projects
```

Open **http://localhost:8090** and choose **Balthasar**. Add a project folder path such as `/app/projects/podu-ros` (Compose mounts `experiments/drafts/` there), then load and start traffic. Choose **Melchior** from the landing page to select a running or paused city run and manually control its lights. In Compose, Melchior's tlcapi address is `http://simulator`, port `8080`. Drag the map to pan and scroll to zoom. The legacy inspector is at `/balthasar/legacy-inspector.html`; the simulator retains its standalone UI on port 8080.

See the [Melchior guide](components/streetflow/README.md), [frontend guide](components/frontend/README.md), and [tlcapi contract](docs/components/contracts/tlcapi.md). Melchior's API is on port 8081 and the frontend on port 8090. Set `SIMULATOR_PORT`, `LOGISTICS_PORT`, and `FRONTEND_PORT` to change host ports.

Only Docker with Compose and a browser are required to run it; .NET and SUMO are inside the containers; PostgreSQL recording is optional. See the [city run guide](components/simulator/README.md) for project folder registration, replay, limits, and troubleshooting; the [legacy backend guide](docs/components/simulator/backend.md) describes observations and recording. The API is at `http://localhost:8080/api/v1`; OpenAPI is at `/openapi/v1.json`. Project folders stay on the host. Docker stores the folder registry and run snapshots in the simulator volume; no database is needed to use the project files.

On this development machine, Docker Desktop uses the `desktop-linux` context. Use `docker --context desktop-linux compose ...` to keep the services visible in Docker Desktop. The system daemon (`default`) is separate, with its own containers, images, and volumes; use it only when intentionally running outside Docker Desktop.

## Run the preserved simulator

The root build still produces the original `streetflow` executable. This is the legacy custom engine, not the planned SUMO wrapper.

```sh
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --parallel
ctest --test-dir build --output-on-failure
./build/streetflow
```

Load the included central Iași map:

```sh
./build/streetflow legacy/simulator/examples/iasi/CITY.IN
```

The existing executable requires a C++20 compiler, CMake, and Linux/POSIX facilities. Optional legacy map tools and checks still use Python; they are not dependencies chosen for the new simulator. Detailed controls and standalone build instructions are preserved in [legacy/simulator/README.md](legacy/simulator/README.md).

## Current design decisions

Use SUMO for vehicle and junction behavior. Keep Balthasar around one SUMO process, Melchior in a separate service for traffic light control, and the MAGI frontend in its own server. Simulated observations remain interchangeable with detector output. Manual tlcapi commands are implemented; automatic management remains future work.

The backend implements run management, ideal simulated detector observations, inspection APIs, trip and checkpoint records, persistent history, and manual traffic light control through Melchior. Scenario calibration, detector error profiles, and automatic management remain later work. Plans are under [`docs/active/plans/`](docs/active/plans/); settled details are under [`docs/components/`](docs/components/).
