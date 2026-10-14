# Simulator API design

One REST/JSON service exposes experiment-management, inspection, and traffic-observation endpoints. The core routes below are implemented; the [backend run guide](backend.md) describes exact request shapes and current limits. OpenAPI is available at `/openapi/v1.json`. The plain HTML/JavaScript inspector will be added separately. Detector error profiles and product actions remain planned extensions.

## Experiment management

The operator/test runner controls scenarios and runs. Streetflow's normal observation client does not need experiment-management operations.

| Method and path | Behavior |
| --- | --- |
| `GET /api/v1/scenarios` | List available scenario IDs, revisions, coverage, and calibration status |
| `GET /api/v1/scenarios/{id}` | Inspect a scenario's parameters, map summary, camera setup, and assumptions |
| `POST /api/v1/scenarios/validate` | Validate submitted demand/parameter configuration against an existing registered map and return issues |
| `POST /api/v1/runs` | Validate scenario and overrides, start SUMO, and create a run; may start paused |
| `GET /api/v1/runs/{id}` | Return state, simulation time, configuration, progress, and failure details |
| `POST /api/v1/runs/{id}/pause` | Finish the current step, then pause |
| `POST /api/v1/runs/{id}/resume` | Continue a paused run |
| `POST /api/v1/runs/{id}/step` | Advance a specified bounded number of steps while paused |
| `PATCH /api/v1/runs/{id}/pacing` | Change wall-clock playback multiplier or select unpaced/headless execution |
| `POST /api/v1/runs/{id}/stop` | Stop gracefully, preserve records, and terminate the child process |
| `POST /api/v1/runs/{id}/reset` | Stop this run and create a new run ID from the same resolved configuration and seed |
| `GET /api/v1/runs/{id}/results` | Retrieve accounting, summary metrics, and output references |

Accept one active run initially; creating another returns a conflict unless the first is explicitly stopped. Retain completed run metadata and outputs. Initially register maps/scenario packages locally and configure demand through their files plus validated API overrides; remote map upload is not required. Scenario parameters change before a run; baseline demand and physical parameters are immutable during a run unless changed by a predefined, recorded event.

Example creation:

```json
{
  "scenario_id": "iasi-central",
  "request_id": "create-001",
  "scenario_revision": "1",
  "seed": 42,
  "start_paused": true,
  "overrides": {
    "demand_multiplier": 1.0,
    "duration_seconds": 9000,
    "step_seconds": 0.5,
    "observation_window_seconds": 5.0,
    "pacing": {"mode": "realtime", "multiplier": 1.0}
  }
}
```

The scenario ID is illustrative; the bundled runnable IDs are `tiny-intersection` and `iasi-central`. Demand overrides scale explicit vehicle routes. Departure windows and clearance are authored in scenario demand and total duration rather than separate API overrides. Request and response limits bound individual operations, not the dataset's total simulated cars.

Run states are `starting`, `running`, `paused`, `completed`, `stopped`, and `failed`. Creation returns the run identity and initial state. Engine startup and configuration failures become inspectable failed runs. A failed/completed/stopped run can be inspected and reset but cannot resume. `completed` means the configured experiment ended; results still report any unfinished traffic.

## Web inspection

| Method and path | Behavior |
| --- | --- |
| `GET /api/v1/runs/{id}/map` | Lane shapes, junctions, signals, cameras, and ID mappings; supports viewport/pagination |
| `GET /api/v1/runs/{id}/snapshot` | Consistent timestamped vehicle/signal snapshot with visible limits and coverage |
| `GET /api/v1/runs/{id}/lanes/{lane_id}` | Lane properties, traffic state, and detector coverage |
| `GET /api/v1/runs/{id}/vehicles/{vehicle_id}` | Selected car's origin/destination, route, current status, and trip metrics |
| `GET /api/v1/runs/{id}/trips` | Paginated trip records, including completed/unfinished status |
| `GET /api/v1/runs/{id}/passages` | Recorded checkpoint-pair travel times with direction and completion status |
| `GET /api/v1/runs/{id}/observations` | Paginated durable observation archive after retention or restart |

Viewport filtering and vehicle sampling affect inspection only. Snapshot metadata includes simulation time, run identity, sequence, returned count, total active count, and whether display sampling occurred. The browser must not present a sampled subset as all traffic. Use polling first; add a push transport only after the measured load warrants it.

Do not expose these privileged endpoints through the Streetflow observation adapter. They can reveal full routes and exact vehicle state that real roadside cameras cannot supply.

## Traffic data for Streetflow

| Method and path | Behavior |
| --- | --- |
| `GET /api/v1/traffic/catalog` | Current source session, network revision, camera configuration, lanes, and supported measurement fields |
| `GET /api/v1/traffic/observations` | Latest completed observation frames, or bounded retrieval after a sequence cursor; can filter intersection/detector |

Frames follow the [shared observation contract](../contracts/traffic-observations.md). Completed windows retain their original sequence and timestamps. A client can retrieve missed frames from retained output; if a cursor is older than retention, the response states the gap and earliest available frame. Reset starts a new session so the app cannot confuse old and new traffic.

The client selects a live or recorded traffic source during configuration. Real detection can implement the same contract through an HTTP producer/adapter; sharing semantics does not require deploying the simulator in production. Initially, use explicit polling and bounded recorded frames. Keep the transport choice independent from the measurement definitions.

Return missing measurements as unavailable/null values, not a successful zero-traffic observation. Include supported fields in the catalog so a client does not assume every camera provides speed, vehicle class, or complete turning counts.

## Manual traffic light control (tlcapi)

City runs implement [tlcapi v1](../contracts/tlcapi.md): `GET /api/v1/city-runs/{id}/signals` reads controller states and `PUT /api/v1/city-runs/{id}/signals/{signalId}` sets a lamp action, optionally scoped by `link_index`. Geometry and movement indices are in the run map's `signal_points`. Commands require a running or paused run and return applied state without advancing simulation time.

New city runs start with lights off. Melchior supplies a separate server and manual UI for these commands. Direct lamp writes do not impose phase conflict checks or clearance intervals; automatic management remains future work. See [Melchior](../streetflow/README.md) and the [running guide](../../../components/streetflow/README.md).

## Future product actions

Autonomous control will need permitted phases/transitions, clearance intervals, requested-versus-applied command records, and stale-session handling. Route guidance would also need advice delivery, participating-driver configuration, and compliance records. These richer action interfaces are outside the manual-control milestone.

## Consistency and errors

State changes are applied by the coordinator at a known step. Current management requests return the run status after the command is applied; failures use the error response. Future product actions may need separate pending/applied outcomes. Duplicate requests must not advance time or apply the same action twice; create/reset/step use a caller-supplied request ID for persistent deduplication.

Use a consistent error object with a stable code, message, relevant run/request ID, and validation details. Reject unknown scenarios, invalid parameter ranges, illegal lifecycle transitions, unsupported observations/actions, and competing active runs explicitly. An engine crash reports a failed run and final known state; it must not leave a browser apparently watching fresh traffic.

Define units, timestamps, allowed overrides, maximum page sizes, retention, and polling expectations in the published schema. Start with local operation. Remote production access requirements belong to deployment design once the target environment is defined.
