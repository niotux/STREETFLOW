# Simulator run guide

The C# service runs SUMO traffic and serves a lightweight plain HTML/JavaScript inspector. It exposes experiment controls, detector-shaped lane observations, and saved results. Running the simulation does not require a browser to remain connected.

## What needs installing

Get a copy of this repository and install Docker with Compose v2. No host .NET SDK, SUMO, PostgreSQL, Node, npm, or Python is required for the packaged application. Internet access is needed for the initial image build; the inspector has no external tile, CDN, or font dependencies.

| Host | Installation |
| --- | --- |
| Windows | [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/) using Linux containers and WSL 2; hardware virtualization must be enabled |
| macOS | [Docker Desktop](https://docs.docker.com/desktop/setup/install/mac-install/), choosing the Intel or Apple Silicon installer |
| Linux | [Docker Engine](https://docs.docker.com/engine/install/) and the [Compose plugin](https://docs.docker.com/compose/install/linux/), or Docker Desktop |

Use a current desktop browser. Phones/tablets can act as viewers of a separately hosted service; they are not the intended Docker simulation hosts. The default port is local to the host machine. Cross-platform support uses Linux containers; this repository has been exercised on Linux x64. ARM64 build inputs exist, but Apple Silicon/ARM64 and Windows/macOS hosts still need execution verification.

Allow several minutes for a first build, depending on download speed and CPU; it downloads the .NET build/runtime images, SUMO and native libraries, NuGet packages, and PostgreSQL. Subsequent starts reuse images and need seconds. These are estimates, not performance guarantees. On this development machine, the runtime simulator image reports about 1 GB and PostgreSQL about 650 MB; build/test caches take additional disk space, and run archives grow with the experiment. A sampled small-scenario run used roughly 170 MB in the simulator container and 62 MB in PostgreSQL, excluding Docker's VM, build tools, and the browser. Larger networks/populations need more CPU and memory. No GPU is required for the simulation.

## How it works

The browser sends configuration and run commands to ASP.NET Core, then polls map snapshots and inspection records. It draws the actual network lane shapes on a canvas; it does not advance the simulation. Static files are served by the same service, with no frontend build or extra server.

The C# coordinator launches one SUMO child process and talks to it over TraCI. SUMO handles vehicles, following, junctions, and traffic lights. C# owns the clock, run lifecycle, observation windows, and recording. Playback speed changes wall-clock pacing, not traffic physics. A paused run advances only when stepped; a running one continues when the browser closes.

The observation builder translates lane visibility and native SUMO counting-line events into the data a future camera detector could supply. The separate Streetflow product will consume `/api/v1/traffic/…`. The inspector can access exact vehicle positions/routes and travel times through `/api/v1/runs/…`. Those privileged details are not supplied by the detector-shaped interface.

The recorder preserves full run files in a Docker volume and indexes runs, observations, and trips in PostgreSQL. Compose starts the database and the C# service; SUMO runs inside the service container. There is one active simulation at a time. Neither the future Streetflow product nor the real camera/AI detector is implemented by this UI.

## Start with Docker

From the repository root:

```sh
docker compose up --build -d --wait
curl http://localhost:8080/health
curl http://localhost:8080/api/v1/scenarios
```

Open **http://localhost:8080**. Choose the synthetic four-way intersection or the central Iași scenario, set the seed/duration/demand, and click **Load scenario**. It loads paused so you can inspect the roads before starting. Click **Run**, **Pause**, or **Step +5s**. **Stop** finalizes a run; **Reset** creates a new paused run with the same saved inputs. Change playback speed or choose **As fast as possible** for an unpaced experiment.

Drag the map to pan, scroll to zoom, and use **Fit map** to return to the whole network. Dark cars are moving, orange cars are stopped, and a selected car is blue with its route highlighted. Signal dots show the colors present in the signal program, not a single green/red state for every approach. Teal segments indicate camera coverage and teal squares indicate counting positions; purple rings are checkpoints. The inspector uses local SUMO coordinates rather than online map tiles.

Click a car or a **Trips** row to inspect its route, departure, current elapsed or completed duration, arrival, and recorded waiting/time loss. Final SUMO waiting/time-loss values may be unavailable until a run ends. **Detectors** shows lane counts and mean speeds for the displayed simulation-time window. **Checkpoints** shows up to 100 recorded checkpoint-pair journeys; the complete list is paginated through the API. **Run history** opens saved experiments. After a service restart, past trip/observation records remain available but live positions do not; the UI labels that explicitly. Snapshot display is capped at 10,000 cars within the current viewport and labels sampling, without limiting simulated car population.

Use `docker --context default compose ...` on this machine if the selected Docker Desktop context is unavailable. HTTP binds to localhost. Set `SIMULATOR_PORT` to use another host port. `POSTGRES_PASSWORD` overrides the local development password. The database is accessible to the simulator on the Compose network without opening a host database port.

`docker compose stop` stops services while preserving results. `docker compose up -d --wait` starts them again. Results and database contents live in the `simulator-data` and `postgres-data` named volumes.

Use `docker compose logs -f simulator` to inspect service/engine failures. Rebuild with `docker compose up --build -d --wait` after changing code or bundled scenarios. If port 8080 is occupied, set `SIMULATOR_PORT=8081` in a local `.env` file and use that port in the browser. An interrupted active experiment is marked failed after restart; restart does not resume its SUMO process. Avoid `docker compose down -v` unless you intend to delete saved run and database volumes.

## Create and step a run

```sh
curl -X POST http://localhost:8080/api/v1/runs \
  -H 'Content-Type: application/json' \
  -d '{"scenario_id":"iasi-central","request_id":"iasi-first-run","seed":42,"start_paused":true,"overrides":{"step_seconds":0.5,"observation_window_seconds":5,"duration_seconds":300,"demand_multiplier":1,"pacing":{"mode":"realtime","multiplier":1}}}'
```

The response includes `id`, state, resolved configuration, engine version, and the output directory. Replace `RUN_ID` below with that `id`.

```sh
curl -X POST http://localhost:8080/api/v1/runs/RUN_ID/step \
  -H 'Content-Type: application/json' \
  -d '{"request_id":"step-001","steps":20}'

curl http://localhost:8080/api/v1/runs/RUN_ID/snapshot
curl http://localhost:8080/api/v1/traffic/catalog
curl 'http://localhost:8080/api/v1/traffic/observations?after=0'
curl http://localhost:8080/api/v1/runs/RUN_ID/trips
```

Twenty 0.5-second steps advance ten simulation seconds. Polling a paused run does not advance it. An observation appears only after a complete window. Use a new `request_id` for each intended action; retry an identical request with the same ID safely. Reusing an ID with different parameters returns 409. Replay records survive service restart. There is one active run at a time.

For continuous execution, POST `{"request_id":"resume-001"}` to `/runs/RUN_ID/resume`. Pause and stop accept the same shape at their respective endpoints. Reset stops the original and returns a new paused run ID using its saved inputs, demand, settings, and seed. Completed/stopped runs remain inspectable.

PATCH `/runs/RUN_ID/pacing` with `{"request_id":"speed-001","mode":"unpaced","multiplier":1}` to run as fast as processing permits. `realtime` pacing uses `multiplier` to control wall-clock playback speed; it does not change traffic physics. A configured simulation duration ends the experiment even if some cars are unfinished. Teleporting is disabled so congestion does not make vehicles disappear.

## Available interfaces

All paths below are under `/api/v1`. OpenAPI is at `/openapi/v1.json`.

| Interface | Operations |
| --- | --- |
| Scenarios | GET `/scenarios`, GET `/scenarios/{id}`, POST `/scenarios/validate` using the run-creation shape |
| Run management | POST/GET `/runs`; GET `/runs/{id}`; POST pause/resume/step/stop/reset; PATCH pacing |
| Inspector | GET `/runs/{id}/map`, `/snapshot`, `/lanes/{laneId}`, `/vehicles/{vehicleId}` |
| Results | GET `/runs/{id}/results`, `/trips`, `/passages`, `/observations` |
| Product data | GET `/traffic/catalog`, `/traffic/observations` |

Lists of map geometry, trips, and passages use `offset` and `limit`. Limits default to 1000 and permit 1–10000 items. Steps permit 1–1000 per request. These are response/request bounds, not a limit on the dataset's car population.

Snapshots accept `min_x`, `min_y`, `max_x`, `max_y` in SUMO local meters, plus `limit`. Responses distinguish total active cars, viewport count, returned count, and sampling. `available: false` means an old run has no position snapshot retained in this process; lane traffic counts are null in that case. Map responses contain local lane shapes, junctions, signal identifiers, cameras, checkpoints, attribution, and independent lane/junction totals. Paging both geometry collections uses the supplied offset; stop after each collection's total.

Live observations accept `session_id`, `after`, `limit`, `detector_id`, and `intersection_id`. Without `after`, they return the latest retained frame per detector. With `after`, they return newer frames in sequence order. A stale session gets 409; a cursor before retention reports `gap`. Reset creates a new session. GET `/runs/{id}/observations?after=0&limit=1000` reads the durable archive after retention or restart.

The product API supplies counts and speeds within configured coverage. Exact car identities, routes, positions, and trip records are inspector data. There are no product action endpoints yet.

## Scenarios and map import

`tiny-intersection` contains 32 synthetic cars on a signalized four-way junction. It has ideal cameras and a west-to-east checkpoint pair, making its counts and travel times easy to check. `iasi-central` converts the existing real OSM extract and supplies 96 explicitly routed synthetic cars. It is not a calibrated city model or a full-city extract. Demand, default signal programs, and camera positions are synthetic.

Register more prepared packages beneath `scenarios/` and restart the service. Each package has `manifest.json`, a SUMO network XML file, a routes XML file, optional `cameras.json`, and optional `checkpoints.json`. The included packages demonstrate their schemas. Camera regions and checkpoint positions use meters along a named SUMO lane. Preserve source data, attribution, and conversion settings.

Native preparation commands, with `netconvert` on PATH:

```sh
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile -- prepare-scenarios
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile -- \
  import-osm --input /path/to/map.osm --output scenarios/my-city --id my-city
```

The OSM helper filters passenger roads, converts geometry, and generates a small fixed synthetic route fixture. It does not infer real demand. The generic package loader accepts prepared networks independently of that helper. Explicit vehicles need numeric departure times for advance scheduling; native flow/trip definitions can be passed through to SUMO and discovered as cars enter the simulation. Activity profiles and additional-file signal/incident configuration remain later extensions.

`demand_multiplier` reproducibly replicates/subsamples explicit vehicles using the seed. It preserves departure times and routes, saves the resolved routes before running, and permits zero-demand experiments. It currently requires explicit vehicles for volume overrides; native flow demand uses its own source settings. Resets reuse resolved demand without multiplying twice.

## Native development and tests

Install .NET 10 and SUMO 1.28; put `sumo` and `netconvert` on PATH. From the root:

```sh
dotnet restore Streetflow.slnx --locked-mode
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile --urls http://localhost:8080
dotnet test Streetflow.slnx
```

Native development defaults to file recording. Set `Simulator__ConnectionString` to enable PostgreSQL. Set `STREETFLOW_TEST_POSTGRES` to a separate test database connection string to enable the PostgreSQL integration test. Without it, that test is explicitly skipped. Other integration tests require the real SUMO executable and use isolated temporary output directories.

| Environment variable | Purpose |
| --- | --- |
| `Simulator__SumoBinary` | Path to SUMO executable; defaults to `sumo` |
| `Simulator__NetconvertBinary` | Path to conversion executable; defaults to `netconvert` |
| `Simulator__ScenarioRoot` | Registered scenario directory; defaults to repository `scenarios/` |
| `Simulator__DataRoot` | Archive directory; defaults to repository `.data/simulator/` |
| `Simulator__ConnectionString` | Optional PostgreSQL connection string |
| `Simulator__EngineTimeoutSeconds` | Engine I/O/startup timeout; defaults to 15 |
| `Simulator__ObservationRetention` | Number of frames retained in memory; defaults to 2000 |

Run the same tests in a container with SUMO already installed:

```sh
docker build --target test -t streetflow-simulator-tests .
docker run --rm streetflow-simulator-tests
```

To include the PostgreSQL test, give the test container access to a separate test database and pass `STREETFLOW_TEST_POSTGRES` through `-e`. The tests assert real engine output, repeatability, failures, counting semantics, archives, and cleanup. CI runs the engine tests in Linux containers and the engine-independent tests on Windows/macOS.

## Recording and current limits

City projects have a separate [import and map-loading API](project-loading.md), with persistent folder/ZIP imports and strict validation. It loads all three accepted projects without starting traffic. The existing scenario/run APIs described here remain available.

Each run preserves its resolved network, routes, configuration, manifest, camera/checkpoint definitions, observations, events, SUMO log/trip output, normalized trip records, and checkpoint passages. PostgreSQL contains `simulator_runs`, `simulator_observations`, and `simulator_trips`. Live state stays in memory; recording writes provide backpressure between steps. A storage or engine error produces a failed run. Interrupted runs become failed when the service restarts; completed records remain available. Resume after restart is not supported.

Crossings and checkpoint times use native SUMO induction-loop events at configured lane positions. Entry times are used once per passage, including cars that leave the lane between snapshots; touching a detector over multiple steps is not multiple crossings. SUMO's vehicle-front reference defines a passage. The observer groups these events into half-open windows and samples visible cars at the window end. Queue classification uses a configured speed threshold and observed stopped duration. Empty-lane counts are zero and its mean speed is null. Detector errors, outages, synthetic video, and calibration are not implemented. [SUMO counting-line data](https://sumo.dlr.de/docs/TraCI/Induction_Loop_Value_Retrieval.html)

The implementation has been exercised on Linux x64, natively and through Compose. Windows/macOS native execution and ARM64 container execution remain platform verification work. Network geometry and retained trip results currently load into memory; arbitrary-size real-time simulation is not a promise. Large runs can run headless and slower than real time, while inspector response limits only affect display.
