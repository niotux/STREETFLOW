# Four-way intersection — North to east experiment

This **Transformer Routes** project isolates a visible weight change on the same four-way SUMO map as [the baseline](../four-way-transformer-routes/README.md). Import it as `four-way-north-east`; the baseline stays unchanged. All five daily periods use identical weights, so any starting hour should show the same strong direction.

The numbers are synthetic experimental assumptions, not measured traffic or a trained neural network. The project stores probabilistic model parameters, not individual vehicles or authored routes. The simulator samples complete legal routes before vehicle insertion.

## Expected traffic

| Entry | Requested vehicles/hour | South exit | North exit | East exit | West exit |
| --- | ---: | ---: | ---: | ---: | ---: |
| North | 900 | 2% | Infeasible | **96%** | 2% |
| South | 30 | Infeasible | 60% | 20% | 20% |
| East | 30 | 25% | 25% | Infeasible | 50% |
| West | 30 | 25% | 25% | 50% | Infeasible |

Total requested demand is **990 vehicles/hour**. North contributes about **91% of departures**; north-to-east alone contributes **864 vehicles/hour**, or about **87% of all departures**. In ten simulated minutes that is roughly 144 north-to-east departures versus 21 other departures. Poisson sampling means actual counts vary. Demand specifies departures, not guaranteed inserted throughput or queue size.

On the map, most cars should enter from the **top**, approach the junction, and exit toward the **right**. North-to-east has a legal `north_in → east_out` connection. Signals and priority rules still determine when vehicles can move; these weights do not control traffic lights.

## Parameters and reproduction

`journey-model.json` declares the same five periods, four origins, four destinations, one category, one vehicle type, and twelve legal movements as the baseline. The origin/destination order is **south, north, east, west**. `weights.json` contains 20 arrival rates, 80 journey logits, and 960 movement logits, totaling **1,060 scalar parameters**.

Each north-origin destination row stores the natural-log probabilities for south 0.02, east 0.96, and west 0.02. Its same-side north coordinate is a finite zero placeholder masked by network infeasibility; a zero logit is not a zero probability. Other origins retain the baseline off-peak shares. The movement logits stay unchanged because this graph has exactly one legal route per feasible origin/destination pair; destination weights determine the visible turning distribution.

Run `python3 experiments/drafts/four-way-north-east/prepare.py` from the repository root to reproduce the package. The script copies the original tiny-graph network and writes this experiment's metadata, terminals, provenance, and weights. Compare it with the baseline at the same seed and starting hour. Existing imported projects are immutable, so use a new project ID for later changed experiments.

The supported model architecture and file contract are documented in [project-format-v2.md](../../../docs/active/ideas/simulator/project-format-v2.md). This fixture does not alter Edge Population projects or server settings.

Live Docker verification on 2026-10-14 compared both projects at 07:30 with seed 42 for 600 simulated seconds, inspecting positions every 5 seconds. Observed north-to-east journeys rose from **3 in the baseline to 169 in this experiment**; 169 of the experiment's 175 observed completed north-origin journeys went east. Neither run recorded a teleport. The tuned run had one pending insertion at the final sample, with no persistent backlog.
