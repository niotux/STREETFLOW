# City simulator

Load a city project, run continuously generated traffic in SUMO, and watch roads and moving cars. The C#/.NET 10 backend serves plain HTML/JavaScript. No Python or Rust is needed to run it.

The simulator supports version 1 **Edge Population**, version 2 **Transformer Routes**, and version 3 [**Neural Routes**](../../docs/components/simulator/neural-routes.md). The [route trainer](../route-trainer/README.md) is a separate utility and Compose project for manually authoring/importing route datasets, training on CPU, and exporting v3 packages.

## Start with Docker

Install Docker Engine with Compose, or Docker Desktop. From the repository root:

```sh
docker compose up --build -d
docker compose ps
```

Open [localhost:8080](http://localhost:8080). The first start downloads/builds .NET and SUMO images; later starts reuse them. PostgreSQL is optional and is not started by the default Compose workflow. Allow a few GB for images and persistent data. Set `SIMULATOR_PORT` if port 8080 is occupied.

Projects live in the repository's `projects/` folder, which Compose mounts read-only into the simulator. Each project folder, or project `.zip`, placed there appears in Loader after **Refresh list**. Copy the bundled examples to start:

```sh
cp -r experiments/drafts/* projects/
```

- `tiny-graph-devices` — Edge Population with four configured physical devices.
- `four-way-north-east-devices` — north-to-east Transformer Routes experiment with those same four devices.
- `tiny-graph` — small synthetic map using Edge Population (version 1).
- `four-way-transformer-routes` — the same four-way map using Transformer Routes (version 2); compare 07:30 and 17:00.
- `four-way-neural-routes` — synthetic version 3 neural model; compare 07:30 and 17:00.
- `iasi-central-day` — central Iași, aggregate daily curve and estimated road weights.
- `podu-ros` — local intersection map, evening camera sample and documented estimates.

Choose a project, seed and start time in Loader, then click **Load**: the map opens with a paused simulation. Start it in Runner. Pause freezes the city clock and vehicles; Step advances one engine step; Resume continues. Stop closes the engine and cancels remaining traffic, keeping the project loaded; Reset run creates a new paused run from the saved inputs, seed, generator version, and operational settings. Loading another project at any time replaces the current simulation. **Unload** returns to an empty map; Runner is disabled until something is loaded. Closing the browser leaves the simulation loaded, and reopening attaches to it.

The projects folder is the source of truth. Edit, add or remove projects with your other tools, then click **Refresh list**. Unavailable entries (invalid projects, folders without `experiment.json`, duplicate project IDs) are listed below the selector with the reason. To use another folder, set `STREETFLOW_PROJECTS_DIR=/absolute/path` in `.env` and run `docker compose up -d simulator`. See [Projects folder](../../docs/components/simulator/project-loading.md).

The existing fixed-scenario inspector remains at [legacy-inspector.html](http://localhost:8080/legacy-inspector.html).

## Stop and restart

```sh
docker compose stop
docker compose up -d
```

Projects remain on the host. ZIP extractions and saved run snapshots live on the `simulator-data` volume. Optional PostgreSQL recording uses `postgres-data`. `docker compose down` removes containers but retains these volumes. Active runs stop during a clean shutdown; restarting a saved run replays from the beginning, rather than resuming its previous in-memory state. Avoid `down -v` unless you intend to delete saved runs and records.

## Generation and limits

The following road-weight rules describe **Edge Population**, which remains supported. **Transformer Routes** reads model weight tensors, samples journey contexts, and calculates complete destination-conditioned routes before spawning. Its first implementation supports single-lane roads. See [Transformer Routes](../../docs/components/simulator/transformer-routes.md) for the new format, four-way probabilities, model inspection APIs, and operational limits.

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

Configure these through the simulator environment in Compose or a local override. Pending capacity must not exceed live capacity; all limits must be positive. Resolved limits are recorded in each run's status. When entry/live capacity is reached, generation waits without consuming further choices or losing arrivals. Simulation continues so existing cars can clear. A pending entry or ungenerated arrival more than the configured backlog limit behind the clock produces a visible congestion warning. While an entry exceeds that limit, new generation waits and existing traffic continues to advance. Generation resumes when blocked entries clear; requested arrivals and random choices are preserved. Congestion does not terminate the run. City runs also enable SUMO gridlock recovery after the same configured waiting limit: stuck vehicles move past the blocked section and rejoin downstream. The status and map note report the cumulative recovery count. Live capacity includes vehicles temporarily off-map during recovery. This keeps the city view progressing but is an artificial recovery, not a calibrated solution to junction capacity. Pause freezes those timers. Persistent blockage remains bounded and explicit.

The city workflow retains live positions, bounded candidate routes and pending IDs, aggregate technical status, and saved input files. It does not record completed vehicle history, observations, detectors, trip tables, or passage reports. Saved run metadata remains on disk until deliberately removed; a ZIP's extracted copy is deleted when the ZIP leaves the projects folder. Map size and active traffic determine memory use and speed; playback speed is a target, not a guarantee on every device.

## Native development and API

Native development needs .NET 10 and SUMO/netconvert on PATH:

```sh
dotnet restore Streetflow.slnx --locked-mode
dotnet run --project components/simulator/Streetflow.Simulator --no-launch-profile --urls http://localhost:8080
dotnet test Streetflow.slnx
```

Import/map inspection alone works without SUMO. Native storage defaults to `.data/simulator`; set `Simulator__DataRoot` or `Simulator__SumoBinary` as needed. PostgreSQL is optional and remains the legacy recording index. Default Compose runs without it. To enable it, start the `recording` profile and set `SIMULATOR_CONNECTION_STRING` to the desired connection string (with host `postgres` inside Compose).

[Projects folder API](../../docs/components/simulator/project-loading.md): list the projects folder, inspect projects and maps, and load or unload a simulation. Continuous runs use:

```sh
curl -H 'Content-Type: application/json' \
  --data '{"seed":42,"start_paused":true}' \
  http://localhost:8080/api/v1/projects/tiny-graph/runs
```

Use the returned run ID with `GET /api/v1/city-runs/{id}`, `/map`, or `/snapshot`. Send a JSON body to `POST /api/v1/city-runs/{id}/{pause|resume|step|stop|restart|pacing}`. Step accepts `{"steps":10}`; pacing accepts `{"pacing":{"mode":"realtime","multiplier":5}}`. Empty controls accept `{}`. Start accepts optional `start_time_seconds`, `step_seconds`, `start_paused`, and `pacing`. Only one SUMO run can be active across city and legacy workflows.

## Physical traffic-light control

Logistics now uses the [physical-device tlcapi](../../docs/components/contracts/tlcapi.md), which exposes the map and authored physical controls. Both project formats accept [traffic-lights.json](../../docs/components/simulator/traffic-light-devices.md). The simulator map displays one marker per configured device. The raw signal API below remains a technical interface for compatibility; it is not the Logistics control surface.

## External signal control

New city runs start with **all traffic lights off**, using SUMO's underlying priority rules (`--tls.all-off true`). Embedded and additional timing programs do not run automatically. Run configuration records `signal_control_mode: "external-v1"`. The legacy scenario inspector retains its scenario programs.

- `GET /api/v1/city-runs/{id}/signals` returns current controller IDs and SUMO state strings, including before the first step.
- `PUT /api/v1/city-runs/{id}/signals/{signalId}` accepts `{"action":"green","link_index":0}`. Find controller IDs and movement indices in `/map` → `signal_points`, or in a light's hover details.

Actions are `on` (enable at red), `red`, `green`, `off`, `yellow`, and `red-yellow`. Omit `link_index` to apply the action to every movement on that controller. Provide it to change one movement while preserving the others. `off` restores the movement's original SUMO off-state (`o` for yielding or `O` for priority), rather than giving all movements unconditional priority. Green uses `G`, red uses `r`, yellow uses `y`, and red + yellow uses `u`. The response is the controller's actual state after SUMO applies the command.

```sh
curl -X PUT -H 'Content-Type: application/json' \
  --data '{"action":"green","link_index":0}' \
  http://localhost:8080/api/v1/city-runs/RUN_ID/signals/1846743533
```

Commands work on running and paused city runs and immediately update their snapshots without advancing simulation time. Invalid actions/indices return 400, unknown controllers return 404, and inactive runs return 409. Manual states hold until another command or a new/restarted run; there is no automatic cycle, timeout or inferred transition. A controller application supplies the desired phase sequence, including yellow and red + yellow. Commands act on SUMO, so they affect vehicle movement, not just displayed colors. The map is read-only; a future controller service uses this API.

Restart starts from zero with every signal off; it does not replay past external commands. Runs saved under the previous automatic-control behavior remain inspectable, but their restart returns `incompatible_signal_control`; create a new run on the same project instead. The same seed reproduces requested arrivals; external control affects the resulting traffic movement.

## Troubleshooting and verification

- Import failed: read the displayed filename/edge error; check [the format](../../docs/components/simulator/experiment-project-format.md).
- No movement: check whether the run is paused and whether the chosen city time has positive weights.
- Overload: reduce the project's mean departure rate or increase operational limits within available memory.
- Engine failed: inspect `docker compose logs simulator` and the run's `engine.log` on the data volume.
- Port occupied: choose another `SIMULATOR_PORT` before starting Compose.

Tests cover generation repeatability, weights, legal paths, midnight, controls, real SUMO movement/replay on all three maps, overload, the projects folder, loading and unloading, and persistence. Linux x64 with SUMO 1.28.0 is the verified engine platform. Windows/macOS portable-code CI does not validate native engine behavior; ARM64 and those host engines remain untested. [Deferred manual checks](../../docs/active/plans/simulator/review-checklist.md) cover the behavior and visual review still owed by the user.

## Intersection terminals

The [Podu Roș commute estimate](../../experiments/drafts/podu-ros-commute/README.md) is a separate prepared variant with morning traffic toward the city/Palas and evening traffic toward the southern approaches. Its direction, peak hours and strengths are estimates based on the user's observation. It combines a citywide daily shape with the existing evening camera proxies; it is not measured full-day directional density. Compare 08:00 and 17:00 with the same seed. Project folders reflect disk edits; saved runs keep their original input snapshots.

The Podu Roș example includes `terminals.json`: 22 entrance and 27 exit lane points at the exterior approaches, displayed as entry circles and exit diamonds. Nearby lanes are grouped at map scale; click a numbered group to zoom or choose a terminal and click Show lane. Three interior cul-de-sacs and roundabout fragments have no terminal markers.

The city map also draws live traffic lights per controlled lane-to-lane movement, using the controller ID and `linkIndex` from the SUMO network. Every visible controlled movement has a three-lamp traffic-light symbol, including when the run is stopped or its lights are off. Dark lamps mean off or no live state; hover to distinguish them. Crowded symbols are separated with lines to their modeled stop-line positions. The simulator map is read-only; [Melchior's separate page](../streetflow/README.md) provides manual controls through the external API. The [MAGI frontend](../frontend/README.md) serves this simulator UI under `/balthasar/`. See the [signal and gridlock audit](../../experiments/drafts/podu-ros/README.md#signal-and-gridlock-audit-2026-10-14) for the historical generated timings, saved-run evidence and calibration work required.

Its explicit `endpoint_selection: terminals` generates entrance-to-exit journeys, including lane/position constraints and continuous lane paths. The minimum trip distance is 40 m so short clipped boundary journeys can participate. Edge weights and the 1943.855422/hour camera-track proxy are unchanged. That rate is not calibrated capacity; prolonged runs can still expose congestion in inferred junction/signal geometry. The 2000-second automated flow check uses an explicitly reduced 600/hour test fixture; it does not claim the original rate is congestion-free.

Refresh the list after editing a project's inputs, then load it again. Saved runs retain their original input snapshots; use a separate folder and project ID only when keeping several variants in the projects folder. Generator version `city-weights-v3-through` records the current through-routing behavior; prior-version runs remain inspectable but cannot be restarted as exact replays with this generator. Overload errors now identify blocked entries separately from departure backlog and preserve the last live positions for inspection.

Generated city journeys use `city-weights-v3-through`, which excludes immediate SUMO U-turn connections from both edge and lane route searches. This prevents the Nicolina → Aleea Rozelor → U-turn → Nicolina shortcuts that blocked the main approach. Existing network links and the external traffic-light API are retained. Vehicle symbols now have a pale windshield marking their forward direction. See the [Nicolina correction](../../experiments/drafts/podu-ros-commute/README.md#nicolina-gridlock-correction-2026-10-14).
