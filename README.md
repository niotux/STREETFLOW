# STREETFLOW

STREETFLOW contains two planned products and a simulator used to test the decision-making product:

- **Streetflow app:** consumes traffic observations and makes decisions. Navigation, signal control, or a combination remain open product choices.
- **Detector:** turns camera footage into traffic observations using vehicle detection, tracking, and lane/camera calibration.
- **Simulator:** wraps SUMO to run road traffic, expose camera-like observations, and provide a browser inspector and experiment controls.

The simulator on `better-simulation` uses **C# / .NET 10, SUMO 1.28, PostgreSQL, and Docker Compose**, with a plain HTML/JavaScript browser inspector. It also runs headless. Windows, Linux, and macOS are target platforms; Linux native execution and Linux containers have been exercised. Dataset capacity depends on available resources. Existing legacy tools are retained.

Start with the [component architecture](docs/architecture.md), then the [simulator design](docs/simulator/README.md).

## Repository layout

| Path | Contents |
| --- | --- |
| `docs/simulator/` | Simulator scope, architecture, APIs, demand strategy, and implementation plan |
| `docs/streetflow/` | Decision-making product boundaries and open product questions |
| `docs/detector/` | Camera/AI detector boundaries and requirements |
| `docs/contracts/` | Shared traffic-observation contract |
| `docs/research/` | Traffic-model literature and Iași infrastructure studies |
| `docs/archive/` | Superseded design proposals, kept for context |
| `assets/images/` | Screenshots and other image assets |
| `components/` | .NET simulator backend, observation contracts, and product implementation locations |
| `scenarios/` | Ready-to-run tiny intersection and central Iași SUMO packages |
| `legacy/simulator/` | Preserved C++ simulator, browser viewer, tools, tests, and map examples |

## Run the simulator

```sh
docker compose up --build -d --wait
curl http://localhost:8080/health
curl http://localhost:8080/api/v1/scenarios
```

Open **http://localhost:8080**, choose a scenario, click **Load scenario**, then **Run**. The inspector shows roads, cars, signal states, detector coverage, trips, and checkpoint travel times. Drag to pan, scroll to zoom, and click a car to inspect it.

Only Docker with Compose and a browser are required to run it; .NET, SUMO, and PostgreSQL are inside the containers. See the [run guide](docs/simulator/backend.md) for platform setup, how the components work, APIs, and development. The API is at `http://localhost:8080/api/v1`; OpenAPI is at `/openapi/v1.json`. Docker stores results and PostgreSQL data in named volumes.

On this development machine, the system Docker daemon works under the `default` context. If Docker Desktop is stopped and the selected context points to its missing socket, use `docker --context default compose ...`.

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

Use SUMO for vehicle and junction behavior. Build one simulator service around one SUMO process, with a web inspector and two logical API surfaces: experiment management and product-facing traffic observations. Make the simulated observations interchangeable with the detector's output. Decide the product's traffic-control actions separately.

The backend implements run management, ideal simulated detector observations, inspection APIs, trip and checkpoint records, and persistent experiment history. Scenario calibration, detector error profiles, and product actions remain later work. See [the implementation plan](docs/simulator/implementation-plan.md).
