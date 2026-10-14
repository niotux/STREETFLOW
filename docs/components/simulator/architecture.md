# Simulator architecture

Build one service around a SUMO process. The service loads a scenario, owns simulation time, gathers engine state, generates detector observations, serves the browser, and records run results. Keep these as modules inside one application.

## What SUMO supplies

| SUMO responsibility | What we use |
| --- | --- |
| Road traffic | Individual vehicles, routes, acceleration/braking, and lane changes |
| Road rules | Lane connections, priority, junction interactions, and signal programs |
| Network preparation | netconvert for OSM conversion and netedit for corrections |
| Routing | duarouter and the engine's routing facilities |
| Additional road users | Vehicle classes, transit, and pedestrians when included in a scenario |

These are engine/toolchain capabilities, not proof that an imported map matches actual conditions. [SUMO overview](https://sumo.dlr.de/docs/SUMO_at_a_Glance.html)

SUMO also provides detector primitives and trip output. Use them where their meanings match our contract; normalize units, empty values, and aggregation windows instead of exporting raw engine fields. [Lane-area detectors](https://sumo.dlr.de/docs/Simulation/Output/Lanearea_Detectors_(E2).html), [Trip output](https://sumo.dlr.de/docs/Simulation/Output/TripInfo.html)

## What the wrapper supplies

| Module | Responsibility |
| --- | --- |
| Scenario loader | Validate map files, demand, parameters, signal programs, and camera/lane mappings |
| Run coordinator | Start/stop SUMO, apply lifecycle commands, and advance simulation time |
| SUMO adapter | Read engine state and apply supported requests through one owned connection |
| Observation builder | Measure configured lane regions and counting lines; noise, latency, and outages are later extensions |
| API handlers | Expose management, inspection, and product observations with stable data contracts |
| Recorder | Save configuration, provenance, observations, events, and per-car/run metrics |
| Browser inspector | Draw geometry and traffic, inspect cars/lanes, and operate a run |

An HTTP request does not directly run a simulation step. The coordinator serializes state-changing requests. API handlers read a consistent snapshot or enqueue a request for a documented step.

## Integration and time

Use a separate SUMO child process: its lifecycle is explicit, sumo-gui remains available for debugging, and engine faults can be reported independently. The .NET backend implements the required TraCI subset in C#, using subscriptions and batched queries. Keep the adapter boundary so embedding via libsumo can be evaluated later if measurements justify it. See [the stack decision](stack-decision.md). [TraCI](https://sumo.dlr.de/docs/TraCI/), [libsumo](https://sumo.dlr.de/docs/Libsumo.html)

Simulation time drives demand departures, vehicle dynamics, detector windows, actions, and metrics. Wall-clock time drives browser requests and optional real-time pacing. Changing playback speed changes pacing, not vehicle speed, demand, or the integration time step.

Proposed initial settings are a 0.5-second simulation step, 5-second observation windows, and browser polling up to 5 times/second. They are configuration choices to validate, not accuracy guarantees. A detector window is published once and carries a sequence number. Pausing stops advancement; polling a paused run does not manufacture new observations.

Reset creates a new run identity from the same scenario and seed. A run fixes its scenario revision, demand realization, SUMO version, and configuration. Changes that alter demand or physics belong to a new run or an explicitly recorded scenario event. Replay of a pinned run should reproduce its key outputs; cross-platform numerical behavior must be tested rather than assumed bit-for-bit identical.

## Virtual cameras and product observations

A virtual camera defines the intersection, incoming approach, lane regions, count lines, observable classes, and measurement windows. Its output is constrained to those regions. SUMO's lane-region detectors can supply some measurements; the wrapper may derive line crossings or richer region observations from vehicle state when needed.

Start with ideal observations and declared coverage. Add deterministic error profiles for missed detections, wrong-lane assignment, delivery delay, and outages when testing product robustness. Synthetic errors are assumptions until measured against a real detector. Observation generation is not a rendered-video or YOLO test.

For a phase-control product, actual signal state is a separate observation from a detector only when the scenario declares an equivalent real controller/camera source. The app does not receive exact routes, destinations, global vehicle identities, or speeds beyond sensor coverage through the detector contract.

## Browser inspection and per-car metrics

The inspector receives lane polylines, junction geometry, signals, camera regions, and bounded vehicle snapshots. It can pan/zoom, select a lane or car, inspect routes, and inspect travel time between checkpoints. Use an explicit camera overlay so users see the difference between traffic present and traffic observable by Streetflow.

For each car, record planned departure, actual insertion, origin/destination, route, arrival or other termination, elapsed travel time, stopped time, and engine time loss. Departure delay and in-network travel duration are separate. SUMO's tripinfo reports duration, waiting time, time loss, and departure delay; configure unfinished-trip output as needed. [TripInfo fields](https://sumo.dlr.de/docs/Simulation/Output/TripInfo.html)

Define a checkpoint as an oriented crossing line on a lane/edge. An A-to-B measurement starts at the first qualifying A crossing and ends at the first B crossing after it, in the declared direction. Preserve passage events for repeated visits instead of merging them. Incomplete passages stay incomplete; live elapsed time is not a completed travel-time result. This supports lane-to-lane movements, corridor journeys, and complete trips without confusing their meanings.

Store final trip records on disk so inspection survives cars leaving the active snapshot. Exact car data belongs to the inspector/evaluator API, not the product traffic-observation API.

## Dataset size and performance

Accept arbitrary valid SUMO networks and demand sets without a corridor-specific node/car cap. A large dataset still needs enough memory, disk, and compute. Lane/junction complexity, active vehicle count, route searches, time step, observation coverage, and output volume all affect speed. Resource exhaustion must report a failed run, not silently drop roads, cars, or measurements.

Large runs can use headless mode and run slower than real time. In the browser, request only a viewport's geometry/vehicles and show a visible rendering limit when sampling. Every admitted car remains simulated regardless of rendering. Keep large results in bounded, paginated queries and on-disk records.

SUMO's microscopic core is primarily single-core, with additional threading available for routing and limited other work. Separate simulations can later run in separate processes for experiment batches. Do not promise arbitrary-size real-time simulation, automatically change model fidelity, or introduce a distributed engine in the first implementation. [SUMO parallelism FAQ](https://sumo.dlr.de/docs/FAQ.html#can-sumo-be-run-in-parallel-on-multiple-cores-or-computers)

## Default and optional product behavior

A scenario's ordinary routes and signal programs run without Streetflow. Product disconnection preserves that baseline or activates an explicitly configured fallback.

Initially expose observations. If navigation is selected, add routing advice plus a scenario-defined driver compliance fraction; record how often advice changes a trip. If signal control is selected, add allowed plan/phase requests through a controller that enforces configured transitions, minimum green, yellow, and all-red. Those capabilities are optional additions, not a decision about the product today. [SUMO traffic-light behavior](https://sumo.dlr.de/docs/Simulation/Traffic_Lights.html)
