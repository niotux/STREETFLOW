# City project catalog

This folder contains inputs for the current simulator workflow. The projects remain under `drafts/` for path continuity, although their format and contents were accepted. They are registered by folder path through the [project loader](../docs/components/simulator/project-loading.md); runtime behavior and open review items are in the [simulator guide](../components/simulator/README.md) and [review checklist](../docs/active/plans/simulator/review-checklist.md).

| Project | Topic and evidence | Status |
| --- | --- | --- |
| [Four-way projects](../docs/components/simulator/traffic-light-devices.md) | Edge Population, Transformer Routes, north-to-east experiment, and Neural Routes share one authored four-device map inventory | The `*-devices` folders remain compatibility copies; canonical projects include the mapping |
| [Central Iași](drafts/iasi-central-day/README.md) | City road graph with estimated daily edge profiles | Accepted input; estimates are documented in its README |
| [Four Way - Edge Population](drafts/tiny-graph/README.md) | Synthetic four-way graph for generator and workflow checks | Accepted input; synthetic traffic values |
| [Four Way - Transformer Routes](drafts/four-way-transformer-routes/README.md) | Same four-way map with version 2 model weights and directional/turn probabilities | Runnable synthetic test; version 1 Edge Population remains available |
| [Four Way - NE [experiment]](drafts/four-way-north-east/README.md) | Same map with 900 north arrivals/hour and 96% selecting the east exit, all day | Synthetic weight-tuning experiment; baseline preserved |
| [Four Way - Neural Routes [experiment]](drafts/four-way-neural-routes/README.md) | Same four-way map with the version 3 neural route model | Synthetic training example; traffic-device mapping is shared with the other four-way formats |
| [Podu Roș](drafts/podu-ros/README.md) | Real intersection map with evening camera-sample evidence and explicit estimates outside coverage | Accepted input; not calibrated full-day demand |
| [Podu Roș commute estimate](drafts/podu-ros-commute/README.md) | Directional morning/evening commute variant on the Podu Roș network | In-progress estimate; derived from observed direction and existing camera proxies |

Compose mounts `drafts/` into the simulator; the runtime image does not bundle these projects. Registered folders remain editable on disk, and other tools can use them without starting the simulator or a database. Refresh in Loader after editing; removal from the registry leaves the folder intact.

The projects contain maps, demand inputs, and detector placeholders. SUMO generates vehicle trips and routes at runtime. Research source files and preparation scripts are listed in the [research index](../docs/research/README.md); individual source folders describe whether their contents are measured, estimated, or archived.
