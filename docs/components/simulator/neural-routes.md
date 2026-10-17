# Neural Routes: project format version 3

Version 3 generates complete routes using a time-conditioned neural sequence model. Versions 1 (Edge Population) and 2 (Transformer Routes) retain their loaders and generators. The [standalone trainer](../../../components/route-trainer/README.md) authors datasets, trains, evaluates, and exports v3 ZIPs independently of the simulator Compose project.

## Package

```text
experiment.json
network.net.xml
neural-model.json
route-model.onnx
route-dataset.json       Authoring snapshot, not required at runtime
training-report.json     Training/export report, not required at runtime
...                     Referenced optional signals, devices, terminals
```

The manifest has `format_version: 3` and `neural_model_file`. It must omit non-null `demand_file` and `journey_model_file`. Network, defaults, optional `additional_files`, `traffic_lights_file`, `terminals_file`, and `detectors_file` retain their existing meaning. Additional files may supply signal programs or simple pedestrian flows; pedestrian routes are separate from the neural vehicle model. V3 routes use whole ordinary roads rather than terminal positions; optional terminals remain display assets.

```json
{
  "format_version": 3,
  "id": "four-way-neural-routes",
  "name": "Four-way neural routes",
  "network": {"file": "network.net.xml"},
  "neural_model_file": "neural-model.json",
  "defaults": {"start_time_seconds": 25200, "step_seconds": 0.5}
}
```

## Metadata and inference contract

`neural-model.json` uses these required fields:

| Field | Contract |
| --- | --- |
| `format_version`, `architecture` | `3`, `local_turn_gru_v1` |
| `model_file` | Contained relative path to a self-contained ONNX model |
| `network_sha256` | Lowercase SHA-256 of the exact network bytes |
| `dataset_sha256` | Lowercase SHA-256 of the trainer's canonical dataset snapshot |
| `vehicle_type` | One `{id, vehicle_class, weight: 1}` |
| `repeat_daily` | Whether arrivals repeat at midnight |
| `hidden_size` | Positive hidden-state width, at most 256; trainer emits 64 |
| `max_turns` | Maximum successor count over usable roads, at least 1, at most 128 |
| `maximum_route_movements` | Explicit failure bound, 1..10000 |
| `roads` | Every usable ordinary road in ordinal ID order |
| `periods` | Contiguous ordered periods covering `[0,86400)` |

Each road has `edge_id`, `next_edge_ids`, and `can_exit`. Successor IDs cover all distinct legal road movements for the configured class, sorted ordinally. Internal connector permissions participate. Multiple lane realizations do not duplicate road choices. Multilane maps are supported; path masks propagate feasible lanes and use a 20 m lane-change planning allowance. Physical motion and queueing remain SUMO's responsibility.

Each period contains `start_seconds`, `end_seconds`, and `arrivals`. Each arrival is `{start_edge, vehicles_per_hour}`. An empty arrivals list means no demand. Origins are unique within each period; values are finite and positive. V3 preserves these authored totals instead of attempting to encode absolute traffic volume in normalized next-token probabilities.

The ONNX model is a single recurrent step with fixed batch size 1:

| Name | Direction | Type / shape |
| --- | --- | --- |
| `road` | Input | int64 `[1]`, index in `roads` |
| `previous_turn` | Input | int64 `[1]`; initial value `max_turns + 1` |
| `period` | Input | int64 `[1]`, planned departure period |
| `hidden` | Input | float32 `[1, hidden_size]`, initially zero |
| `logits` | Output | float32 `[1, max_turns + 1]` |
| `next_hidden` | Output | float32 `[1, hidden_size]` |

Output coordinates `0..max_turns-1` are local successor indices. Output coordinate `max_turns` is `END`; it is legal only where `can_exit` is true. Nonexistent or lane-infeasible movements are masked. Remaining finite scores are normalized using stable softmax and sampled with the run's choice RNG. Update the road, previous turn, and hidden state until `END`.

The route is calculated completely before insertion into SUMO. Planned departure time selects a period once for the entire route, including when congestion delays insertion or travel crosses another period. There is no destination sampled first, fixed-route catalog, or destination-conditioning solve. Movement caps fail explicitly; routes are never truncated or resampled to hide nontermination. A model can generate legal loops and new combinations absent from the authored dataset.

## Runtime and compatibility

Nonhomogeneous Poisson timing uses the sum of origin rates and samples an origin proportionally to its rate. Uncovered authored hours export as empty arrival periods. Nonrepeating demand ends at the first midnight; pending/existing traffic continues. Existing bounded generation, backpressure, and dynamic-route reuse apply.

Import checks metadata, daily periods, network fingerprint, vocabulary order/coverage, movement identities, origin-to-exit reachability, ONNX signature, and finite smoke inference. The model is loaded from bytes to avoid requiring external tensor files. V3 limits are 100000 roads, 1440 periods, 64 MiB of model bytes, 128 local choices, and hidden width 256. The trained architecture has learned parameters even though the old v2 JSON weight tensors are absent.

`Simulator__MaximumNeuralRouteMovements` defaults to 10000 and caps the package's own route bound. `Simulator__MaximumDynamicRoutes` also applies. Resolved bounds, seed, and generator version `neural-routes-v1` are saved with a run. Metadata and ONNX bytes participate in project revisions and are copied into saved run packages. Restart uses those saved artifacts. The authoring dataset/report are not runtime inputs and do not participate in the runtime revision; the model metadata records dataset provenance.

`GET /api/v1/projects/{id}/model` and `/demand` return v3 metadata. `/weights` remains v2-only and returns HTTP 409 for a neural project. The city UI shows `Neural Routes`, the active period's requested rate, and the configured vehicle type. SUMO and signal-control APIs are unchanged.

The [synthetic four-way package](../../../experiments/drafts/four-way-neural-routes/README.md) demonstrates import and replay. It does not validate complex route-history learning or measured city traffic.
