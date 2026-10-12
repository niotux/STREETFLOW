# City simulator

Load a city project, run continuously generated traffic in SUMO, and watch roads and moving cars. The C#/.NET 10 backend serves plain HTML/JavaScript. No Python or Rust is needed to run it.

## Start with Docker

Install Docker Engine with Compose, or Docker Desktop. From the repository root:

```sh
docker compose up --build -d
docker compose ps
```

Open [localhost:8080](http://localhost:8080). The first start downloads/builds .NET, SUMO, and PostgreSQL images; later starts reuse them. Allow a few GB for images and persistent data. Set `SIMULATOR_PORT` if port 8080 is occupied.

In the sidebar, expand **Import a server folder** and import one of these bundled examples:

- `/app/projects/tiny-graph` — small synthetic map.
- `/app/projects/iasi-central-day` — central Iași, aggregate daily curve and estimated road weights.
- `/app/projects/podu-ros` — local intersection map, evening camera sample and documented estimates.

The map opens before traffic starts. Choose a seed, starting time, and speed, then click **Start traffic**. Pause freezes the city clock and vehicles; Step advances one engine step; Resume continues. Stop closes the engine and cancels remaining traffic. Restart creates a new paused run from the saved inputs, seed, generator version, and operational settings. Closing the browser leaves the run active. Reopening attaches to the active city run.

You can also import a ZIP containing a project's files or its enclosing folder. To prepare one locally:

```sh
(cd experiments/drafts/podu-ros && zip -r /tmp/podu-ros-project.zip .)
```

Imported IDs must be unique; duplicate imports report an error rather than replacing the original. The existing fixed-scenario inspector remains at [legacy-inspector.html](http://localhost:8080/legacy-inspector.html).

## Stop and restart

```sh
docker compose stop
docker compose up -d
```

Imported projects and technical run packages live on the `simulator-data` volume; PostgreSQL uses `postgres-data`. `docker compose down` removes containers but retains these volumes. Active runs stop during a clean shutdown; restarting a saved run replays from the beginning, rather than resuming its previous in-memory state. Avoid `down -v` unless you intend to delete stored projects and records.

## Generation and limits

The arrival rate follows total current road weight relative to its full-day mean. Legal routes are chosen by their distance-weighted road preference. A seed reproduces choices with the same project inputs, generator version, and limits; engine movement also depends on SUMO version and step length. Weights are approximate preferences, not measured densities or independent road probabilities.

Generation prepares a bounded catalog of shortest free-flow paths and detour-limited alternatives for permitted vehicles. Endpoints include boundary and internal roads. The catalog explores starts in seeded round-robin order; a finite candidate budget may omit some legal journeys. Missing positive legal choices cause a visible generation error; the runtime never silently substitutes uniform routing. Distances/costs use ordinary-edge permitted lane geometry and speed; junction connector travel time is not included in candidate scoring. SUMO owns movement, lane choice, and signals.

Repeating profiles wrap at midnight. Nonrepeating profiles stop generating at the first midnight after the starting city time; pending/active cars still drain, and the run stays open until stopped. No-departure intervals do not complete a run.

| Setting | Default |
| --- | --- |
| `Simulator__MaximumLiveVehicles` | 5000 active + pending vehicles |
| `Simulator__MaximumPendingEntries` | 512 submitted vehicles awaiting insertion |
| `Simulator__GenerationBatchSize` | 64 arrivals per engine step |
| `Simulator__MaximumBacklogSeconds` | 300 simulation seconds |
| `Simulator__MaximumProjectBytes` | 256 MiB uploaded/expanded package bytes |
| `Simulator__MaximumProjectFiles` | 4096 files |

Configure these through the simulator environment in Compose or a local override. Pending capacity must not exceed live capacity; all limits must be positive. Resolved limits are recorded in each run's status. When entry/live capacity is reached, generation waits without consuming further choices or losing arrivals. Simulation continues so existing cars can clear. A pending entry or ungenerated arrival more than the configured backlog limit behind the clock fails the run with a visible overload message. Pause freezes those timers. Persistent blockage is therefore bounded and explicit.

The city workflow retains live positions, bounded candidate routes and pending IDs, aggregate technical status, and saved input files. It does not record completed vehicle history, observations, detectors, trip tables, or passage reports. Imported projects and run metadata accumulate on disk until deliberately removed while the service is stopped. Map size and active traffic determine memory use and speed; playback speed is a target, not a guarantee on every device.

## Native development and API

Native development needs .NET 10 and SUMO/netconvert on PATH:

```sh
dotnet restore Streetflow.slnx --locked-mode
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile --urls http://localhost:8080
dotnet test Streetflow.slnx
```

Import/map inspection alone works without SUMO. Native storage defaults to `.data/simulator`; set `Simulator__DataRoot` or `Simulator__SumoBinary` as needed. PostgreSQL is optional for native use and remains the legacy recording store.

[Import API](../../docs/simulator/project-loading.md): list/import projects and inspect maps. Continuous runs use:

```sh
curl -H 'Content-Type: application/json' \
  --data '{"seed":42,"start_paused":true}' \
  http://localhost:8080/api/v1/projects/tiny-graph/runs
```

Use the returned run ID with `GET /api/v1/city-runs/{id}`, `/map`, or `/snapshot`. Send a JSON body to `POST /api/v1/city-runs/{id}/{pause|resume|step|stop|restart|pacing}`. Step accepts `{"steps":10}`; pacing accepts `{"pacing":{"mode":"realtime","multiplier":5}}`. Empty controls accept `{}`. Start accepts optional `start_time_seconds`, `step_seconds`, `start_paused`, and `pacing`. Only one SUMO run can be active across city and legacy workflows.

## Troubleshooting and verification

- Import failed: read the displayed filename/edge error; check [the format](../../docs/simulator/experiment-project-format.md).
- No movement: check whether the run is paused and whether the chosen city time has positive weights.
- Overload: reduce the project's mean departure rate or increase operational limits within available memory.
- Engine failed: inspect `docker compose logs simulator` and the run's `engine.log` on the data volume.
- Port occupied: choose another `SIMULATOR_PORT` before starting Compose.

Tests cover generation repeatability, weights, legal paths, midnight, controls, real SUMO movement/replay on all three maps, overload, imports, and persistence. Linux x64 with SUMO 1.28.0 is the verified engine platform. Windows/macOS portable-code CI does not validate native engine behavior; ARM64 and those host engines remain untested. [Deferred manual checks](../../docs/simulator/review-checklist.md) cover the behavior and visual review still owed by the user.

## Intersection terminals

The Podu Roș example includes `terminals.json`: 22 entrance and 27 exit lane points at the exterior approaches, displayed as entry circles and exit diamonds. Nearby lanes are grouped at map scale; click a numbered group to zoom or choose a terminal and click Show lane. Three interior cul-de-sacs and roundabout fragments have no terminal markers.

Its explicit `endpoint_selection: terminals` generates entrance-to-exit journeys, including lane/position constraints and continuous lane paths. The minimum trip distance is 40 m so short clipped boundary journeys can participate. Edge weights and the 1943.855422/hour camera-track proxy are unchanged. That rate is not calibrated capacity; prolonged runs can still expose congestion in inferred junction/signal geometry. The 2000-second automated flow check uses an explicitly reduced 600/hour test fixture; it does not claim the original rate is congestion-free.

Old imported packages remain immutable. Reimport the updated folder under a distinct ID (the review instance uses `podu-ros-terminals`) to compare it without losing old runs. Generator version `city-weights-v2-terminals` records the new behavior; old-version runs remain inspectable but cannot be restarted as exact replays with this generator. Overload errors now identify blocked entries separately from departure backlog and preserve the last live positions for inspection.
