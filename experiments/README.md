# City projects

These three projects follow the accepted city-project format and were accepted in task 2. Their folders retain the drafts/ location. The backend now supports [folder/ZIP import and map loading](../docs/simulator/project-loading.md); [continuous traffic and the city UI](../components/simulator/README.md) are now implemented.

| Project | Contents |
| --- | --- |
| [Central Iași](drafts/iasi-central-day/README.md) | Real road graph, explicit daily profiles for 713 directed edges, generation settings, and empty detector placeholder |
| [Tiny graph](drafts/tiny-graph/README.md) | Synthetic road graph and traffic rules for testing the same generator |
| [Podu Roș](drafts/podu-ros/README.md) | Fresh local intersection map, 106 edge profiles, evening camera-sample evidence, and explicit estimates outside camera coverage |

Projects contain simulation inputs. Vehicles and routes are generated during execution. Source-paper preparation files are kept separately under docs/research/data and are not loaded by the simulator.

All three folders are complete draft inputs awaiting task 2 acceptance; the existing loader does not run them yet. Each contains a manifest, SUMO network, `demand.json`, empty `detectors.json`, and preparation notes. Iași's 17,112 hourly edge weights reproduce the verified approximate source curve with explicit lane/priority estimates; the tiny project's eight profiles are synthetic. Those two use an estimated full-day mean of 600 departures/hour. Podu Roș uses a separate local camera sample: 14 edges have geometrically inferred evening preferences, 92 use declared estimates, and unobserved hours are flat extrapolations. Its approximately 1,944/hour generation baseline is a selected-camera-track throughput proxy, not measured full-intersection demand. Review the evidence and model assumptions in each README before integrating them.

Review [the project format](../docs/simulator/experiment-project-format.md) and [the task plan](../docs/simulator/experiment-integration-plan.md).
