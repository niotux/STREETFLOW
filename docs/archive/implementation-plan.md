# Simulator implementation plan

The current proposed city-project work follows [the simplified integration plan](experiment-integration-plan.md): project loading, continuous probabilistic traffic, run controls, and the city view. Detectors remain undefined placeholders. The earlier implementation milestones below describe existing capabilities and historical planning; they are not extra requirements for that work.

Use C# / .NET 10 for the backend, with SUMO, PostgreSQL, and Docker Compose. The inspector will use plain HTML and JavaScript. The simulator targets Windows, Linux, and macOS, accepts arbitrary valid network datasets within available resources, and supports a separate Streetflow process. Python and Rust are excluded from the new stack. The [backend run guide](../components/simulator/backend.md) describes implemented scope and [stack decision](../components/simulator/stack-decision.md) records integration evidence.

## Integration acceptance checks

| Requirement or check | Deliverable |
| --- | --- |
| Component boundary | Agree that SUMO owns traffic behavior and the wrapper owns lifecycle, observations, inspection, and records |
| Observation meanings | Confirm visible counts, crossing counts, queue rules, camera coverage, units, lane mapping, and missing-data semantics |
| Scenario inputs | Decide supported demand inputs and overrides; preserve arbitrary network support |
| Inspection | Define selected-car metrics, checkpoint travel times, and large-result queries |
| Platform support | Exercise SUMO installation, child-process startup, packaging, and cleanup on Windows/Linux/macOS |
| Binding feasibility | Read lane shapes, vehicles, signals, detector values, routes, and trip results; test stepping and shutdown without Python |
| Product integration | Demonstrate a separate process consuming the observation contract; leave navigation/control choice open |
| Performance | Measure engine time, adapter time, memory, API volume, and rendering on increasing fixture sizes |
| Reproducibility | Pin engine/scenario versions, seed demand, and preserve resolved run inputs |

Platform support and arbitrary dataset support are requirements already supplied by the user. Performance measurements establish practical hardware limits; they do not restrict the scenario format to a pilot corridor.

## Technical checks that distinguish stack candidates

SUMO supports a native C++ libtraci client and prebuilt Java bindings, while libsumo provides an embedded native alternative. Other languages may need generated bindings, a verified third-party TraCI client, or a native adapter. Test the necessary calls and all target platforms before selecting a language from ecosystem preferences alone. [libtraci interfaces](https://sumo.dlr.de/docs/Libtraci.html), [libsumo interfaces](https://sumo.dlr.de/docs/Libsumo.html)

The selected backend is C# / .NET 10, matching the team's language preference. Its narrow TraCI adapter passes real engine tests on Linux. Continue checking cross-platform packaging, runtime dependencies, supported commands, shutdown behavior, performance, and reproducibility as capabilities expand.

Prefer one wrapper application and a SUMO process. If a candidate requires an additional native bridge process, count that complexity explicitly. Do not select a stack that hides Python behind a helper service. Using compiled SUMO preparation tools is allowed; relying on Python-based SUMO scripts is not part of this design.

The decision and current platform verification scope are recorded in [stack-decision.md](../components/simulator/stack-decision.md).

## Implementation sequence

### 1 Establish engine and contract fixtures

Create a tiny intersection, a small Iași map, and larger generated/imported networks. The small fixtures prove semantics; the larger ones expose resource behavior. Define expected car passages and observation windows by hand on the tiny fixture. Inspect the same run in sumo-gui.

Completion: the native SUMO scenario runs without product code, and expected crossings/trips are known. The wrapper integration checks pass on all three platforms before stack selection is finalized.

### 2 Implement run management

Implement one coordinator, one engine connection, creation, paused stepping, pacing, stop, reset, and basic records. Keep scenario inputs immutable within a run and serialize commands. Use a fixed route/departure list to test reproducibility.

Completion: the management API starts and configures a run; reset reproduces key results; engine failure is visible; clean shutdown leaves no child process. Browser connection state does not affect headless metrics.

### 3 Implement virtual cameras and the traffic API

Map visible lane regions and oriented count lines. Produce ideal observations in the shared schema, including zero versus missing values, session IDs, and sequence numbers. Then add seeded error/delay/outage fixtures. Keep exact engine vehicle data out of this API.

Completion: a separate test client consumes counts per lane/intersection; known crossings are counted once in the correct window; missing and delayed frames preserve their meanings. Streetflow's eventual navigation/control role is not needed to complete this milestone.

### 4 Implement the browser inspector

Implemented: same-service HTML/JavaScript canvas view, run and pacing controls, viewport snapshots, detector coverage, selected vehicles/routes, paginated trip records, checkpoint journeys, and archived run selection. See the [run guide](../components/simulator/backend.md).

Render lane polylines, cars, signals, and camera coverage. Add run controls, selected-car inspection, trip records, and checkpoint-pair travel times. Reuse useful legacy viewer interactions where practical; do not force the new engine into the old straight-edge/CITY.IN assumptions.

Completion: selected positions and signal states agree with engine inspection; car elapsed/completed times are clearly distinct; viewing a subset of a large network does not change the simulated traffic.

### 5 Add graph-based experiment projects and demand generation

Load projects containing road graphs, time-dependent traffic distributions, measurements, and generic generation settings. Infer endpoints and path probabilities, then generate seeded vehicles and routes for each run. Preserve generated trips for controller comparisons. Record evidence and estimates separately. Follow the [project format](../components/simulator/experiment-project-format.md) and [review-gated integration plan](experiment-integration-plan.md).

Completion: synthetic profiles are reproducible and labeled; real measurements have provenance; calibrated claims specify the tested locations and conditions. Demand generation does not silently remove unreachable or unadmitted trips.

### 6 Integrate the Streetflow product and evaluate it

Connect the real Streetflow process using the shared observation contract. Add only the actions needed by the chosen product direction. Compare its outcomes against ordinary baseline behavior using the same scenario and generated demand, over multiple seeds.

Completion: logs account for demand and unfinished/removed traffic; results cover mean and extreme delays, queues, throughput, and measurement failures. The detector product can later replace the simulated producer without changing observation meanings.

## Keep scope proportional

The backend has one active run, a file archive, and optional PostgreSQL indexing enabled by Compose. Larger dataset runs may be unpaced/headless or slower than real time. Concurrent experiment workers, remote operation, sophisticated population synthesis, and synthetic video generation are later requirements to justify individually.

The initial simulator tests Streetflow with detector-shaped data. A later detector evaluation needs footage and labels, and is a different milestone from making simulated cars and lane counts available.
