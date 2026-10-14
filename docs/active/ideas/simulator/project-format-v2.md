# Traffic model weights project format, version 2

Status: weight-file contract implemented as **Transformer Routes** for single-lane networks. It replaces the earlier version 2 draft's named journey catalog. Version 1 remains supported as **Edge Population**. Example weights are synthetic; see the [runtime guide](../../../components/simulator/transformer-routes.md) for current support and operational limits.

The project stores a parameterized stochastic model. It does not store vehicles, routes, named origin–destination journeys, or a list of permitted journey intentions. The runtime samples a journey context from model weights, then samples its complete route before spawning the vehicle.

Both formats also accept an optional `traffic_lights_file` for [physical-device mapping](../../../components/simulator/traffic-light-devices.md), independently of these generation weights.

## Package

```text
city-project/
  experiment.json       Identity, input references, run defaults
  network.net.xml       SUMO roads, lanes, legal connections, lights
  terminals.json        Entry and exit points on lanes
  journey-model.json    Model architecture identifier and axis metadata
  weights.json          Numerical model parameters
  detectors.json        Optional empty reserved array
  README.md             Optional notes
```

Separating model metadata from weights makes the parameter file replaceable without rewriting a catalog of journeys. Both files belong to the project and its input revision. The JSON representation is inspectable and does not require a training framework or executable model code.

Folder and ZIP packaging remain the same. Keep `experiment.json` as the entry point. All referenced paths, including `weights_file`, are relative to the package root, contained inside it, and distinct. Preserve existing restrictions on unsafe paths, external XML includes, authored SUMO demand, and signal-only additional XML files.

## Manifest and terminals

```json
{
  "format_version": 2,
  "id": "tiny-journey-model-v2",
  "name": "Traffic model weights example",
  "network": { "file": "network.net.xml", "attribution": "Synthetic network" },
  "terminals_file": "terminals.json",
  "journey_model_file": "journey-model.json",
  "defaults": { "start_time_seconds": 27000, "step_seconds": 0.5 }
}
```

These fields are required except `network.attribution`. Optional manifest fields are `description`, `detectors_file`, and `additional_files`; omitted additional files mean none. Starting time is in `[0, 86400)` seconds; engine step is in `[0.05, 5]` seconds. There is no version 2 `demand_file` or concurrent road-weight generator.

Retain the existing terminal array shape, with optional `label`:

```json
[
  { "id": "south-entry", "lane_id": "south_in_0", "role": "entry", "position_m": 6 },
  { "id": "north-exit", "lane_id": "north_out_0", "role": "exit", "position_m": 186 }
]
```

Terminals must have unique IDs and lane/role pairs, reference ordinary lanes, and have positions within lane length. Both roles must be present. The model's origin axis covers every entry terminal exactly once. Each exit terminal belongs to exactly one destination group. There is no automatic division of an approach's rate between entry lanes.

## Model metadata

`journey-model.json` has these required fields:

| Field | Meaning |
| --- | --- |
| `format_version` | `2` |
| `architecture` | `"contextual_journey_v1"`, identifying the interpretation of the weight tensors |
| `arrival_model` | `"nonhomogeneous_poisson"` |
| `repeat_daily` | Whether new demand repeats after midnight |
| `route_time` | `"planned_departure"` |
| `weights_file` | Relative path to the numerical parameter file |
| `axes` | Ordered vocabularies defining tensor dimensions |
| `evidence` | Provenance of the stored weights |

The architecture serializes the existing [mathematical design](traffic-route-model-proposal.md): arrival intensity, a joint journey-context distribution, and contextual movement scores with destination conditioning. It does not introduce a neural network. The written mathematics does not define transformer layers, attention operations, embeddings, or a trained checkpoint. This is a weight-file representation of its agreed probabilistic model, suitable for authored parameters now and fitted parameters later.

The `axes` object has exactly six fields:

| Axis | Ordered items |
| --- | --- |
| `period` | `{ start_seconds, end_seconds }` intervals covering `[0, 86400)` |
| `origin` | Entry terminal IDs |
| `destination` | `{ id, terminal_ids }` exit groups, optionally with `label` |
| `category` | Category IDs; use a single `"default"` category when no additional intent classes are needed |
| `vehicle_type` | `{ id, vehicle_class }`, optionally with `label` |
| `movement` | `{ incoming_edge_id, outgoing_edge_id }` pairs for distinct legal directed road movements |

Each axis is nonempty and has unique identifiers/items. There is no `contexts`, `journeys`, or `movement_profiles` catalog. All combinations of origin, destination, category, and vehicle type exist as model coordinates. Network feasibility determines which combinations can be sampled; they do not need names or individually authored records.

The vocabulary names physical map locations and available classes, just as a model needs identifiers for its inputs and outputs. Vocabulary membership does not enumerate complete journeys. Axis order is part of the checkpoint: reordering an axis requires correspondingly reordering its weights.

The movement axis includes every ordinary-to-ordinary road movement permitted for at least one configured vehicle class, exactly once. Multiple lane connectors for the same road pair do not create extra weighted choices. Internal SUMO connectors remain in the network rather than becoming model vocabulary items.

## Parameter file

`weights.json` contains `format_version: 2` and a `tensors` object with exactly three tensors:

| Tensor | Fixed axis order | Values |
| --- | --- | --- |
| `arrival_rates` | `period, origin` | Requested vehicles/hour, finite and nonnegative |
| `journey_logits` | `period, origin, destination, category, vehicle_type` | Finite real scores for sampling the joint context distribution |
| `movement_logits` | `period, origin, destination, category, vehicle_type, movement` | Finite real scores for base movement probabilities |

Each tensor has exactly `dtype`, `axis_order`, `shape`, and `values`. `dtype` is `"float64"`; `values` is a flat JSON array of numbers. Shape lengths must equal the corresponding axis lengths, and value count must equal the product of shape lengths. Flattening uses row-major order: the last axis varies fastest.

For example, an arrival tensor for one period and three origins is:

```json
{
  "dtype": "float64",
  "axis_order": ["period", "origin"],
  "shape": [1, 3],
  "values": [600, 120, 100]
}
```

These values correspond to the three origins in their declared order. They are model parameters, not three vehicles.

For one period, two origins, two destinations, one category, and one vehicle type, a journey-score tensor could be:

```json
{
  "dtype": "float64",
  "axis_order": ["period", "origin", "destination", "category", "vehicle_type"],
  "shape": [1, 2, 2, 1, 1],
  "values": [2, 0, 0, 2]
}
```

This gives the first origin a greater preference for the first destination and the second origin a greater preference for the second destination, where those destinations are reachable. It does not create two named journey templates. The runtime converts scores into probabilities and samples combinations. The complete example contains five periods, three origins, four destinations, one category, one vehicle type, and twelve movements.

There are no omitted weights, implicit zero-filled arrays, per-journey overrides, or tensor broadcasting rules. Every declared coordinate has a value. Zero is an ordinary logit, not an excluded outcome. All finite journey logits give positive probability to their feasible outcomes; this initial architecture does not author exact-zero probabilities for otherwise feasible contexts. Zero arrival rates disable an origin's demand during a period.

## How the weights are interpreted

For the selected departure period and origin, normalize `journey_logits` jointly across destination, category, and vehicle type, using softmax over feasible combinations. Feasibility is derived from the entry lane, destination terminals, vehicle permissions, and network connectivity. It is not a stored whitelist of journey records. Values at infeasible coordinates remain in the dense checkpoint but are masked from context selection.

This defines the mathematical design's joint distribution pi. It does not take an already-declared probability distribution and silently redirect its failed journeys. A positive-rate origin with no feasible combination is an error. Category membership alone introduces no physical restrictions.

After sampling the context, read its `movement_logits` to obtain the design's scores w. At a routing state, normalize only the distinct physically legal outgoing road movements for the sampled vehicle class and lane-feasibility state. Preserve transitions to wrong exits or states unable to complete the destination in this **base** normalization. Apply the existing destination-conditioning equations afterward; the continuation values and conditioned probabilities are derived state, not stored weights.

A movement score applies equally to equivalent lane realizations. Runtime states must preserve later lane feasibility without multiplying road probabilities by the number of lane connectors. Preferences between equivalent lane realizations are outside this representation.

The sampled context stays fixed while the complete route is calculated. Its period is the planned departure period, even if congestion delays insertion. The route is assigned before spawning and retained across subsequent clock boundaries. A model may repeatedly generate similar routes or generate different routes from the same weights; variation comes from sampling with the run seed.

As required by the mathematical design, legal loops and U-turns participate. No named-route catalog, visited-place rule, bounded candidate list, or detour filter changes the modeled distribution. Unsuccessful external exits and unmarked network dead ends have zero destination continuation. Runtime safety/numerical limits produce explicit errors or overload states rather than silently shortening or replacing sampled journeys.

## Time, provenance, and validation

Periods are contiguous, ordered, half-open, and cover the whole modeled day without interpolation. Daily repetition wraps on a fixed 86400-second modeled day; this is not a daylight-saving calendar. With repetition disabled, new scheduled demand ends at the first midnight after the starting city time; existing and pending vehicles continue.

Each `evidence` item requires `tensor_names`, `kind`, and `note`; `source` is an optional descriptive citation or URL. Tensor names must resolve and every tensor needs evidence coverage. Kinds are `measurement`, `derived_estimate`, `user_observation`, and `authored_assumption`. Evidence classifies where parameters came from; it does not claim that authored weights were trained or measured. This first representation records provenance per tensor, not per scalar.

JSON uses UTF-8, case-sensitive snake_case fields, and finite numbers. IDs contain 1–128 non-whitespace characters. Unknown/duplicate object fields are errors. The [structural schemas](project-format-v2-schemas/) additionally enforce required fields, ranks, supported mode identifiers, and basic ranges.

Package validation must also check axis uniqueness and coverage, referenced file safety, daily continuity, network IDs and permissions, tensor shapes and counts, evidence coverage, and positive-rate origin feasibility. File errors must name the relevant tensor, axis, period, or map identifier. Do not normalize or reshape a malformed checkpoint automatically.

Run revisions cover both model files along with the manifest, network, terminals, and referenced optional inputs. Saved run metadata records the seed, generator/solver version, and operational settings. Derived solver caches and generated vehicles/routes belong to the run. Preserve batch-independent generation and backpressure behavior. Dense tensor size grows with the axis dimensions; future compact representations must specify their interpretation rather than silently changing this format.

## Replacing version 1

Version 2 replaces per-road demand profiles, global weight-derived arrival scaling, independent vehicle-type weights, and named route candidates with model parameters. Network geometry, signal assets, terminal positions, and clock defaults retain their existing responsibilities. Signal control remains separate; this format introduces no signal-control UI or signal decision model.

Old road weights do not uniquely determine these model weights. Preparing meaningful Podu Roș parameters still requires explicit estimates or later calibration. Saving weights does not by itself make the generated traffic realistic.

Preserve version 1 projects and saved runs. The loader dispatches explicitly by version, with separate generators; an old run cannot be resumed under the other architecture. The [example package](project-format-v2-example/) is a complete synthetic package accepted by the version 2 loader. The [four-way test project](../../../../experiments/drafts/four-way-transformer-routes/README.md) configures all four entries for comparing both formats on the same geometry. These single-junction examples demonstrate file structure and sampling parameters, not calibration. The initial runtime restriction to one lane per usable road is explicit and does not change version 1's support.
