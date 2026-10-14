# Four-way intersection — Transformer Routes

This version 2 project uses the same synthetic SUMO geometry as [tiny-graph](../tiny-graph/README.md), which remains a version 1 **Edge Population** project. Import this folder as `four-way-transformer-routes` to compare the two models on the same map. Its default start is 07:30. Use 17:00 to see the opposite commute direction.

The model is **Transformer Routes**, using the version 2 `contextual_journey_v1` architecture specified in the [format document](../../../docs/active/ideas/simulator/project-format-v2.md). This name identifies the project's traffic model; the supplied numbers are authored probabilistic parameters, not a trained neural transformer checkpoint. All rates, shares, and time windows are synthetic test assumptions. They illustrate the requested morning citybound/evening homebound pattern and do not claim to reproduce measured Podu Roș traffic.

The folder contains no authored vehicles or predefined routes. The generator samples departures and destinations from the tensors, calculates a complete legal route, and gives that route to SUMO before insertion.

## Inputs and size

| File | Purpose |
| --- | --- |
| `experiment.json` | Version 2 identity, references, and run defaults |
| `network.net.xml` | Unchanged four-way road geometry, legal connectors, and signal assets |
| `terminals.json` | Four entries at 6 m and four exits at 186 m on 192.8 m lanes |
| `journey-model.json` | Ordered axes, supported architecture, and provenance |
| `weights.json` | Dense numerical parameters |
| `prepare.py` | Rebuilds the package from the readable assumptions below |

There are five periods, four origins, four destinations, one `default` category, one passenger vehicle type, and twelve legal directed movements. The checkpoint therefore has **20 arrival rates, 80 journey logits, and 960 movement logits: 1,060 scalars**. Storage stays small because this is a single junction. Dense size grows multiplicatively when more axes are added.

All four origin and destination axes use this order: **south, north, east, west**. Movement pairs are sorted by incoming and outgoing edge IDs. The final axis varies fastest in the flat tensors; array positions depend on the declared axes, not the order of JSON object properties.

## Requested departures

Rates below are vehicles/hour per entry, with independent Poisson arrivals. They determine demand rather than road occupancy. Each period uses half-open intervals; daily demand repeats at midnight.

| Period | South | North | East | West | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| 00:00–07:00 | 30 | 30 | 20 | 20 | 100 |
| 07:00–09:00 | 180 | 60 | 80 | 80 | 400 |
| 09:00–16:00 | 80 | 80 | 70 | 70 | 300 |
| 16:00–19:00 | 60 | 180 | 80 | 80 | 400 |
| 19:00–24:00 | 40 | 40 | 30 | 30 | 140 |

The moderate peak of 400 vehicles/hour is intended for testing generation and boundary exits with signals off and SUMO priority rules. It is not a guarantee of queue-free travel under every signal command or random sample. Signal decisions stay outside these demand files.

## Destination shares

The following percentages are the expected normalized shares **within each origin**. A dash means the map forbids the corresponding same-side U-turn; it is not a hand-authored journey exclusion. The same-side tensor coordinate still stores the finite placeholder `0`, which the loader/generator masks by network feasibility. A zero logit itself never means zero probability.

| Period | Origin | South exit | North exit | East exit | West exit |
| --- | --- | ---: | ---: | ---: | ---: |
| Morning, 07:00–09:00 | South | — | 80% | 10% | 10% |
| Morning, 07:00–09:00 | North | 50% | — | 25% | 25% |
| Morning, 07:00–09:00 | East | 15% | 65% | — | 20% |
| Morning, 07:00–09:00 | West | 15% | 65% | 20% | — |
| Evening, 16:00–19:00 | South | — | 50% | 25% | 25% |
| Evening, 16:00–19:00 | North | 80% | — | 10% | 10% |
| Evening, 16:00–19:00 | East | 65% | 15% | — | 20% |
| Evening, 16:00–19:00 | West | 65% | 15% | 20% | — |
| Other periods | South | — | 60% | 20% | 20% |
| Other periods | North | 60% | — | 20% | 20% |
| Other periods | East | 25% | 25% | — | 50% |
| Other periods | West | 25% | 25% | 50% | — |

At 07:30, expected south-entry → north-exit flow is 180 × 80% = **144 vehicles/hour**; north-entry → south-exit is 60 × 50% = **30 vehicles/hour**. At 17:00 those figures reverse. East and west entries each send an expected 52 vehicles/hour toward north in the morning and south in the evening. Sampled short runs will vary around these expectations.

The stored `journey_logits` are the natural logarithms of these reachable shares. The runtime softmax recovers the probabilities after masking infeasible destinations. Since there is only one category and vehicle type, no additional mixture changes those shares.

Each contextual `movement_logits` row assigns `1.5` to movements leading to its chosen destination and `0` to the others. These are base scores, before destination conditioning. **This particular graph has exactly one route for each feasible origin–destination pair.** Once the destination is sampled, the conditioned movement is therefore certain. Changing a movement logit alone cannot change the observed destination shares here; change journey logits to test turning shares. Testing route choice between alternative paths requires a graph with multiple paths to the same destination.

## Editing and reproducing weights

Run `python3 experiments/drafts/four-way-transformer-routes/prepare.py` from the repository root to regenerate this package. Edit `RATES` to change entry rates and `SHARES` to change reachable destination probabilities. Each share row must sum to one and keep its infeasible same-side entry at zero. The script converts shares to logits and emits the dense arrays; these readable tables are preparation inputs, not a catalog loaded by the simulator.

You can also edit the checkpoint directly. Increasing a journey logit raises that feasible destination's relative probability. Every tensor must remain finite, correctly shaped, and aligned with the metadata axes. Keep weights and metadata together when importing a package. Import updated inputs under a new project ID if an existing immutable project import already uses this ID.

The package has no extra detectors or signal program overrides. The original network contains its signal assets; startup/controller policy is the simulator's separate responsibility. Version 1 `tiny-graph` is preserved for compatibility testing.
