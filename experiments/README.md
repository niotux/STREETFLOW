# City project catalog

This folder contains inputs for the current simulator workflow. The projects remain under `drafts/` for path continuity, although their format and contents were accepted. They can be imported through the [project loader](../docs/components/simulator/project-loading.md); runtime behavior and open review items are in the [simulator guide](../components/simulator/README.md) and [review checklist](../docs/active/plans/simulator/review-checklist.md).

| Project | Topic and evidence | Status |
| --- | --- | --- |
| [Four-way physical-device fixtures](../docs/components/simulator/traffic-light-devices.md) | Edge Population, Transformer Routes baseline, and north-to-east copies with four authored devices | Original projects/imports preserved; shared world enables cached-map reuse |
| [Central Iași](drafts/iasi-central-day/README.md) | City road graph with estimated daily edge profiles | Accepted input; estimates are documented in its README |
| [Tiny graph](drafts/tiny-graph/README.md) | Synthetic graph for generator and workflow checks | Accepted input; synthetic traffic values |
| [Four-way Transformer Routes](drafts/four-way-transformer-routes/README.md) | Same four-way map with version 2 model weights and directional/turn probabilities | Runnable synthetic test; version 1 Tiny graph remains Edge Population |
| [Four-way north-to-east experiment](drafts/four-way-north-east/README.md) | Same map with 900 north arrivals/hour and 96% selecting the east exit, all day | Synthetic weight-tuning experiment; baseline preserved |
| [Podu Roș](drafts/podu-ros/README.md) | Real intersection map with evening camera-sample evidence and explicit estimates outside coverage | Accepted input; not calibrated full-day demand |
| [Podu Roș commute estimate](drafts/podu-ros-commute/README.md) | Directional morning/evening commute variant on the Podu Roș network | In-progress estimate; derived from observed direction and existing camera proxies |

The projects contain maps, demand inputs, and detector placeholders. SUMO generates vehicle trips and routes at runtime. Research source files and preparation scripts are listed in the [research index](../docs/research/README.md); individual source folders describe whether their contents are measured, estimated, or archived.
