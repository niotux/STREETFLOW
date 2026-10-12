# STREETFLOW simulator implementation plan

**Archived proposal.** Superseded by [docs/simulator](../simulator/README.md). The Python/uv recommendation and traffic-light-first assumption below are historical, not active decisions.

Use SUMO as the traffic simulation engine and build a small STREETFLOW service around it. The simulator supplies a reproducible road network, vehicles, demand, detectors, and ordinary traffic control. The product under test reads observations and sends commands through a separate interface. Its optimization and AI logic live outside the simulator.

This proposal targets the `better-simulation` branch. Start with a Python service managed by uv, use TraCI to control a separate SUMO process, and retain the current browser interface as a viewer. SUMO performs vehicle simulation in native C++; the service orchestrates it. Keep the existing custom engine available during migration, but use SUMO as the authority for new scenarios.

## SUMO capabilities and limits

SUMO means Simulation of Urban MObility. It is an open-source microscopic traffic simulator: individual vehicles follow routes through a network with lanes, signals, and priority rules. Its toolchain includes `netconvert` for network conversion, `netedit` for corrections, `duarouter` for routing, and `sumo-gui` for inspecting runs. It supports lane changes, multiple vehicle classes, public transport, and pedestrian simulation. [SUMO overview](https://sumo.dlr.de/docs/SUMO_at_a_Glance.html)

SUMO is a strong candidate for this testbed because it supplies the vehicle and junction models described in the literature study. An imported scenario still needs corrected turn connections, demand estimates, and signal plans. Import heuristics do not establish that those details match Iași. [OSM import documentation](https://sumo.dlr.de/docs/Networks/Import/OpenStreetMap.html)

The microscopic core runs primarily on one CPU core; routing can use additional threads. Parallel experiments should use separate simulation processes. Treat real-time performance as something to measure on our scenario and hardware. [SUMO performance FAQ](https://sumo.dlr.de/docs/FAQ.html#can-sumo-be-run-in-parallel-on-multiple-cores-or-computers)

SUMO is a configurable model, not ground truth about Iași. Establish local credibility by matching observed counts, queue discharge, and travel times, then validate against observations not used for tuning. Its default collision handling, teleports, departure delays, and unfinished trips must be recorded when interpreting results. [SUMO safety behavior](https://sumo.dlr.de/docs/Simulation/Safety.html)

## Technology choices

| Choice for our service | Fit | Tradeoff |
| --- | --- | --- |
| Python with uv | Preferred for the simulator adapter, scenario tools, and evaluation. The official TraCI ecosystem is readily accessible. | Requires a managed Python environment and native SUMO packages. Keep CPU-intensive simulation and large data operations out of Python loops. |
| C++ with CMake and one package manager | Valid if native integration or keeping one existing backend matters most. SUMO provides libtraci and libsumo. | Native library installation, linking, and cross-platform packaging require more work. Language change is not necessary for correctness. |
| Go with Go modules | A good candidate for a compact product service with straightforward deployment. | Use our simulator HTTP interface; this plan does not rely on an unverified Go SUMO binding. |
| C# with .NET and NuGet | A good candidate for a typed product backend with an integrated toolchain. | Direct SUMO C# integration involves generated bindings/native libraries; our simulator HTTP interface avoids that coupling. |

Sources: [Python TraCI](https://sumo.dlr.de/docs/TraCI/Interfacing_TraCI_from_Python.html), [libtraci](https://sumo.dlr.de/docs/Libtraci.html), [libsumo](https://sumo.dlr.de/docs/Libsumo.html), [uv](https://docs.astral.sh/uv/), [Go dependency management](https://go.dev/doc/modules/managing-dependencies), [NuGet](https://learn.microsoft.com/en-us/nuget/consume-packages/overview-and-workflow).

Aim for a small, pinned dependency set. Reimplementing SUMO to eliminate a dependency would create a much larger modeling and maintenance obligation. Initially use Python, TraCI, sumolib, and SUMO applications; add FastAPI with an ASGI server for the service interface. Keep uv and a lockfile as the project tooling. Preserve the existing browser code initially; a frontend framework is unnecessary for proving the integration. [FastAPI documentation](https://fastapi.tiangolo.com/)

Official `eclipse-sumo` Python packages contain compiled SUMO applications; `traci`, `sumolib`, and `libsumo` are also packaged. Prefer matching pinned releases managed in the project environment after checking wheel availability and executable discovery on supported platforms. macOS can require additional native dependencies. An operating-system package is an alternative, but its version must also be recorded. [SUMO package installation](https://sumo.dlr.de/docs/Downloads.php#python-packages--virtual-environments)

## Simulator and product boundary

```text
OSM + corrected lanes and junctions + demand + signal plans
                           |
                    Scenario package
                           |
                  STREETFLOW simulator service
                    |                 |
               SUMO adapter       Run recorder
                    |                 |
                   SUMO         Metrics and replay

Product <---- detector observations / control commands ----> Simulator service
Browser <---- map geometry / vehicle snapshots ------------> Simulator service
```

SUMO owns physical traffic state: vehicles, speed, following, lane changes, junction interactions, and default signal programs. The scenario owns where and when trips occur, vehicle characteristics, detector placement, and any prescribed disruptions. The service owns simulation time, lifecycle, validated command application, snapshots, and logs. The product owns the decisions being tested.

Keep the product observation channel separate from privileged viewer/evaluation data. Renderers and evaluators may access exact vehicle state; the product receives the observations that the intended real sensors could provide. Add sensor noise, delay, outages, and coverage limits as explicit scenario parameters.

Begin with traffic-light observations and control. Route guidance can use the same boundary if it is part of the product: define which vehicles can receive advice and their compliance before evaluating it. Driver routes and ordinary baseline signal behavior remain necessary even when no product is connected.

## Integration approach

Start with a separate `sumo` process controlled by Python TraCI. One worker owns the connection and advances simulation time. HTTP handlers enqueue commands; they do not concurrently step SUMO. Use subscriptions to collect observations and vehicle state in batches. SUMO's GUI provides a useful debugging reference alongside the browser viewer. [TraCI integration and subscriptions](https://sumo.dlr.de/docs/TraCI/Interfacing_TraCI_from_Python.html)

Consider libsumo only after profiling the adapter. It offers similar calls with the engine in-process, avoiding the TraCI socket cost, but has GUI and concurrency differences. Separate worker processes are required for parallel Python libsumo runs. [libsumo documentation](https://sumo.dlr.de/docs/Libsumo.html)

Import the original OSM source into SUMO using netconvert. The old four-column CITY.IN format lacks the lane shapes, permitted turns, speed limits, and junction connections that SUMO needs. Retain OSM snapshots and metadata from the current importer work, but make the SUMO network the canonical simulation network. Node IDs and lane IDs will change, so introduce stable mappings rather than assuming the old graph indices still identify the same objects.

Translate SUMO lane polylines, vehicle positions, headings, and signal states into a versioned browser snapshot format. Replace the viewer's straight-edge interpolation with actual shapes. Use metres and seconds throughout the new interface. UI frame rate and simulation time step must be independent.

## Scenario contents

Each versioned scenario should include:

- Raw map snapshot with source, retrieval time, attribution, and checksum.
- Corrected SUMO network, plus the reproducible conversion options and explicit corrections.
- Routes or demand definitions, vehicle types, detector definitions, and signal programs.
- SUMO configuration with time step, seed, duration, warm-up, and departure/teleport settings.
- A manifest describing coverage, data provenance, assumptions, known omissions, and calibration status.

Each run records the scenario checksum, SUMO release, service release, seed, commands with simulation timestamps, configuration, and completion status. Synthetic demand is explicitly labeled; a scenario becomes calibrated only after comparison with observations.

## Implementation phases and acceptance criteria

### 1 Establish a runnable SUMO scenario

Pin a supported SUMO release and Python environment. Build a tiny single-junction scenario, then import an Iași pilot area. Use the existing central extract to test conversion, but obtain the study's Podu Roș–Nicolina–Cantemir area for the actual pilot; the current bundled map is not the complete recommended corridor. Initially use predefined demand and ordinary fixed signal plans.

Acceptance: one documented command runs headless; the same scenario can be inspected in sumo-gui; arrivals, departures, and errors are recorded; repeating a pinned run with the same seed reproduces the key metrics. No product connection is necessary.

### 2 Build the adapter and run lifecycle

Implement scenario loading, start, pause, resume, deterministic stepping, reset, speed multiplier, and clean shutdown. Distinguish an experiment's seed and generated demand from the viewer's playback speed. Keep a single authority for time; process commands at recorded simulation steps. A reconnecting browser must receive a current full snapshot.

Acceptance: reset reproduces a run; disconnected viewers do not affect headless outcomes; failed workers report errors; no SUMO process is left behind after normal termination or cancellation.

### 3 Connect the existing browser viewer

Serve lane geometry once and stream bounded vehicle/signal snapshots at a separate display cadence. Update selection and IDs to the SUMO network. Retain useful controls and map interaction. Remove assumptions that lanes are straight graph edges and that all distances use the old abstract scale.

Acceptance: car positions and signal states agree with sumo-gui on the same scenario; pausing stops simulation time; playback speed changes pacing rather than physical parameters; rendering limits are visible and do not change which cars are simulated.

### 4 Expose the product interface

Publish a versioned observation schema and action schema. Suggested operations are `get_observations`, `request_signal_plan`, and `request_phase`; route guidance adds an explicitly scoped `request_route`. Keep experiment controls such as reset separate from traffic-control commands.

Accept phase/plan requests through a local signal controller that enforces configured minimum greens, allowed transitions, yellow, and all-red. Do not expose unrestricted lamp-state writes as the normal product API. Invalid or stale requests return a reason and do not modify the run. Record requested and applied commands separately. Define controller disconnection behavior, initially a return to the scenario's default plan. [SUMO traffic-light control](https://sumo.dlr.de/docs/Simulation/Traffic_Lights.html)

Acceptance: a separate test client reads only its allowed observations and changes permitted behavior; invalid phase changes are rejected; actions are reproducible from the log; the simulator continues with its fallback behavior when the product disconnects.

### 5 Correct and calibrate the pilot

Focus on roughly 5–15 signalized junctions. Review lane counts, turn bays, restrictions, crossing placement, and junction clusters. Obtain or measure signal timings and turning counts. Match queue discharge first, demand second, then queue lengths and travel times. Validate on a different observation period. Add trams and pedestrians before treating multimodal conflicts or transit priority as supported experiments.

Acceptance: publish which quantities match observations and within what tolerance; unresolved assumptions remain explicit. Randomly generated traffic is sufficient for interface checks but is not evidence of local traffic accuracy.

### 6 Add repeatable experiment evaluation

Run the same demand realization and scenario under a baseline controller and the product, over several seeds. Use a demand generation window followed by a clearance period or explicitly account for vehicles still waiting/in flight. Compare travel time, delay, queue mean/max, throughput, per-movement waits, and unadmitted/unfinished/teleported vehicles. Log incidents, collisions, and emergency braking where applicable. Observed gridlock can be a real scenario result; silent vehicle removal must not turn it into an apparent improvement.

Acceptance: results include configuration and uncertainty across seeds, account for all demand, and can be reproduced without the UI. Test demand changes, sensor failures, product disconnection, and a lane closure.

## Proposed code organization

```text
simulator/
  engine/       SUMO adapter and serialized stepping
  scenarios/    manifest loading and network metadata
  sensors/      observable detector models
  control/      command validation and signal transitions
  api/          product observations, commands, and viewer snapshots
  evaluation/   run logs and experiment metrics
scenarios/
  tiny-junction/
  iasi-pilot/
web/            existing viewer, adapted to lane shapes
tests/          repeatability, API boundary, lifecycle, and scenario checks
pyproject.toml
uv.lock
```

This is an organization proposal, not a requirement for separate deployable services. One simulator service plus its SUMO child process is enough initially. The product can live in this repository or another repository and can use a different language.

## First deliverable

A pinned SUMO installation, one tiny scenario, an Iași conversion, and a runner that starts, steps, records, and stops the simulation. Establish this before changing the browser or implementing product intelligence. The migration is successful when an independently running product can observe and influence the simulator through the documented interface and be evaluated against reproducible baseline runs.
