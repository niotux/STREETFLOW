# City project format

This document defines version 1, now named **Edge Population**. The simulator also loads version 2 [**Transformer Routes**](transformer-routes.md) and version 3 [**Neural Routes**](neural-routes.md). Existing version 1 files and generation semantics remain supported.

Status: format and generation rules accepted by the user on 2026-10-11 (task 1). All three projects were accepted in task 2. Loading, generation, controls, UI, and Docker integration are implemented; manual review is deferred under the user’s 2026-10-12 authorization.

An edge is one directed road segment. A traffic profile belongs to that edge and describes how busy it should be over the day. Every ordinary road edge that the configured vehicles can use has its own profile. Junction connector edges remain part of the network and need no separate profile.

The project contains a map and these edge profiles. The simulator generates vehicles and complete journeys from them using a seed. Profiles contain no vehicle list or fixed routes.

Physical control devices can be supplied with `traffic_lights_file` in either project format. See [physical-device mapping](traffic-light-devices.md).

## Files

~~~text
city-project/
  experiment.json        Name and references to the files
  network.net.xml        Directed roads, lanes, connections, and lights
  demand.json            Per-edge traffic profiles and generation settings
  detectors.json         Optional empty placeholder
  README.md              Optional description
~~~

Folder and ZIP use the same format. Paths are relative and contained inside the project. Import validates versions, references, and traffic settings. Opening loads the map; starting runs traffic.

## Manifest

~~~json
{
  "format_version": 1,
  "id": "iasi-central-day",
  "name": "Central Iași",
  "network": {
    "file": "network.net.xml",
    "attribution": "© OpenStreetMap contributors"
  },
  "demand_file": "demand.json",
  "detectors_file": "detectors.json",
  "additional_files": [],
  "defaults": {
    "start_time_seconds": 25200,
    "step_seconds": 0.5
  }
}
~~~

The project id is just its internal label; name is shown in the UI. It is not a traffic value. Description, attribution, detector reference, and additional signal files are optional. The road network itself defines its road labels. The edge field below refers to exactly one of those road labels so the loader knows which road the traffic belongs to. There is no separate profile ID or city-wide profile selector.

## Per-edge traffic

~~~json
{
  "format_version": 1,
  "repeat_daily": true,
  "profiles": [
    {
      "edge": "west_in",
      "intervals": [
        { "start_seconds": 0, "end_seconds": 25200, "weight": 0.2 },
        { "start_seconds": 25200, "end_seconds": 32400, "weight": 0.8 },
        { "start_seconds": 32400, "end_seconds": 86400, "weight": 0.4 }
      ]
    },
    {
      "edge": "east_in",
      "intervals": [
        { "start_seconds": 0, "end_seconds": 25200, "weight": 0.1 },
        { "start_seconds": 25200, "end_seconds": 32400, "weight": 0.3 },
        { "start_seconds": 32400, "end_seconds": 86400, "weight": 0.2 }
      ]
    }
  ],
  "generation": {
    "arrival_model": "poisson",
    "mean_departures_per_hour": 600,
    "endpoint_selection": "boundary_and_internal",
    "min_trip_distance_m": 200,
    "max_trip_distance_m": 5000,
    "candidate_limit": 1000
  },
  "routing": {
    "cost": "free_flow_travel_time",
    "max_alternatives": 3,
    "max_detour_ratio": 1.5
  },
  "vehicle_types": [
    { "id": "car", "vehicle_class": "passenger", "weight": 1 }
  ]
}
~~~

This abbreviated example shows two edges; a complete project covers all usable ordinary road edges. The named roads are examples from the tiny graph, not a required junction layout.

A weight is a relative traffic preference for this road at this time. Greater weight means greater intended use. It is not a spawning probability, an independent probability summing to one over the entire map, or a measured density. The generator converts weights into probabilities among legal journey alternatives. Route connectivity means road usage cannot be selected independently.

Each edge has exactly one profile. Its intervals are ordered, contiguous, nonoverlapping, half-open, and cover 0–86400 seconds. Weights are finite/nonnegative; values need not sum to one. An unknown, duplicate, missing, or non-covered usable edge is a validation error rather than a request for the runtime to invent a replacement profile. Internal junction connectors do not need profiles. Daily repetition wraps city time across midnight. With repeat_daily false, new traffic generation ends at the first midnight after the configured starting city time, not after 24 elapsed hours. For example, a run starting at 07:00 generates new traffic for the remaining 17 hours of that day; existing and pending vehicles continue to be simulated afterward.

The generator uses the edge weights directly. It does not substitute a runtime road-capacity prior or flatten each edge's daily curve to the same average. An edge may therefore be much busier than another, and its own morning/night pattern may differ.

The mean departure rate sets the overall amount of traffic; road preferences alone cannot supply its absolute scale. The initial generation rule shapes that rate over time using total edge weight relative to its full-day mean. Within that traffic, route preference uses the distance-weighted mean of edge weights along a legal candidate path. These are approximate generation rules whose resulting movement will be reviewed. They preserve both spatial differences and daily intensity instead of normalizing daily changes away.

Poisson controls random arrival timing only. Spatial choices come from the edge profiles. A zero daily weight total means no arrivals; a positive requested rate with an all-zero day is invalid. No Gaussian spatial distribution is assumed.

A zero edge weight expresses zero preference for that edge, not a road closure. A legal journey may pass through it when other edges give the journey a positive distance-weighted score. Access restrictions belong in the network. A zero total current edge weight means no new arrivals during that interval. If the requested arrival rate is positive but no legal candidate has a positive score, report a generation error instead of substituting uniform choices or silently discarding requested traffic.

## Generated journeys

Discover reachable start/end choices from the graph and vehicle permissions, then generate bounded legal paths. Candidate/alternative limits and distance bounds cap work. Weight candidate paths using the relevant per-edge values and sample choices with the seed. Endpoints and paths are generated run state, not manually specified project inputs.

Generate continuously in bounded batches until stopped. Congestion delays entry; reaching the destination removes the car from active traffic. The same inputs, seed, and compatible generator version reproduce choices, regardless of batch boundaries. Bounds or disconnected graph portions must not silently remove requested traffic.

Record the generator version and resolved operational limits in technical run configuration, alongside the project inputs and seed. These are run metadata rather than additional project-authored files. Replay guarantees apply to the same inputs, generator version, and operational settings; they do not promise identical engine movement across different SUMO versions.

Keep pending entries and generated batches bounded. Apply backpressure before generating additional requests beyond capacity, preserving pending entries and generator state without silently dropping vehicles or skipping random choices. Persistent blockage must produce a visible overload condition rather than unbounded storage. The concrete limits and pause/drain behavior will be implemented and reviewed in task 5; bounded storage does not promise indefinite delivery of a positive arrival rate into permanently blocked roads.

## Iași draft

The existing graph has an explicit profile for every passenger-usable ordinary edge. Each holds 24 hourly weights. The daily shape comes from the prepared PMUD curve; differences between roads are estimated during project preparation from road priority and lane supply. Those values are written into the project, inspectable/editable, and not reconstructed by a runtime fallback. They are estimates rather than published per-road measurements.

Source-paper processing remains outside the application. The project needs only the resulting edge profiles to run. It includes no source comparison, analysis tables, detector output, or reports.

## Controls and detectors

The UI selects a project and seed, shows the city, and provides start/pause/resume/step/stop/restart and playback speed. Defaults specify the starting city time and engine step. There is no profile selector: all edge profiles operate together according to the clock.

detectors.json is an empty reserved array with no behavior defined yet. The city runs until stopped. Imported projects and technical state/files use the existing Docker storage.

See [the integration tasks](../../archive/experiment-integration-plan.md).

## Optional lane terminals

`experiment.json` can reference `"terminals_file": "terminals.json"`. The file is an array of named points on ordinary lanes:

```json
[
  {"id":"west-entry", "lane_id":"west_in_0", "role":"entry", "position_m":6, "label":"West approach"},
  {"id":"east-exit", "lane_id":"east_out_0", "role":"exit", "position_m":200, "label":"East approach"}
]
```

Positions are SUMO distances along lanes; the example exit value must match the actual map. IDs and lane/role pairs are unique. Both roles are required for a nonempty file. Terminals participate in the input revision and saved run package. Map responses expose their computed coordinates.

Set `generation.endpoint_selection` to `"terminals"` to generate only entrance-to-exit journeys. This requires a nonempty terminal file. Existing `"boundary_and_internal"` keeps city endpoint behavior, even if terminal markers are present for display. Terminals define endpoints, not fixed routes or measured departure probabilities.

Terminal routing follows permitted lane connections, constrains the departure and arrival lane/position, and avoids relying on lane changes across tiny road fragments. Its conservative planning allowance is 20 m available road for a lane change, with a 3-second manoeuvring cost in path search and detour comparisons. SUMO still owns movement and safety; this allowance is not a driver calibration or a guarantee that congestion cannot block a journey.

Generator `city-weights-v3-through` excludes network connections marked `dir="t"` from generated through journeys. Physical U-turn links remain in the network; they are not used as candidate shortcuts or detours. Saved runs retain their generator version and are not exact replays under a different version.
