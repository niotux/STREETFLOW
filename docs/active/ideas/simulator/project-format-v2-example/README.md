# Traffic model weights example

This is a complete synthetic package for the [version 2 format](../project-format-v2.md), supported by the Transformer Routes loader. For a test using all four entry approaches, use [four-way-transformer-routes](../../../../../experiments/drafts/four-way-transformer-routes/README.md).

| File | Contents |
| --- | --- |
| [experiment.json](experiment.json) | Manifest and file references |
| [network.net.xml](network.net.xml) | Existing synthetic tiny SUMO network |
| [terminals.json](terminals.json) | Three entries and four exits |
| [journey-model.json](journey-model.json) | Architecture identifier, ordered axes, and provenance |
| [weights.json](weights.json) | Arrival-rate, journey-score, and movement-score tensors |

There are no named journey records or fixed routes. The axes describe physical entries, exits, classes, time periods, and legal road movements. Runtime journey contexts come from sampling combinations of these axes using the stored weights and network feasibility.

All traffic values are synthetic. The morning period is 07:00–09:00 and evening is 16:00–19:00. Arrival weights favor south-entry demand in the morning and north-entry demand in the evening. Journey scores favor northbound destinations for morning south/east arrivals and southbound destinations for evening north/east arrivals. These are invented examples, not Podu Roș measurements.

The weight tensor shapes are:

- `arrival_rates`: `[5, 3]`, 15 values.
- `journey_logits`: `[5, 3, 4, 1, 1]`, 60 values.
- `movement_logits`: `[5, 3, 4, 1, 1, 12]`, 720 values.

Values are flat, with the last dimension varying fastest. Structurally infeasible origin–destination combinations still have tensor coordinates; the runtime derives their feasibility mask from the network. An entry's destination on its own approach is unreachable in this tiny network because it has no U-turn connections.

The graph has one junction, so it does not exercise alternative multi-junction routes or cycles. The single category is `default`; no city-bound/home-bound journey catalog is authored. Adjacent [schemas](../project-format-v2-schemas/) describe the JSON structure; the specification defines the additional shape, reference, and network validation.
