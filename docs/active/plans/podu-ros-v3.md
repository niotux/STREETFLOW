# Podu Roș V3 project: ordered work

## Intended result

Run a trained version 3 Podu Roș project in Balthasar with a morning scenario favoring trips from Nicolina toward Strada Palat and few reverse trips. Keep observed facts, map-derived values, and scenario assumptions distinct. Include pedestrian crossings in the network before training, and demonstrate whether pedestrian demand interacts with cars.

## Order and acceptance checks

1. **Evidence audit.** Inventory the local study, PMUD, released camera data, OSM map tags, and SUMO behavior. Record the observation window, coverage, measured quantities, and unavailable morning turning shares, car spacing, speeds, and pedestrian counts. Do not turn evening camera throughput into full-intersection demand.
2. **Network and crossing audit.** Identify crossings in the OSM source. Build a pedestrian-capable candidate network, inspect its crossings, sidewalks, signal links, and vehicle connectivity. Verify with a SUMO run before using it for training. Mark inferred crossings as model assumptions.
3. **Vehicle behavior and demand.** Record road speed limits and SUMO's car-following settings separately from measured speeds and spacing. Author complete legal morning routes from Nicolina toward Palat and a smaller reverse flow, plus a modest background flow. Use explicit, documented scenario rates and a time curve; do not claim calibration without directional counts.
4. **Train and export V3.** Validate the dataset against the final network, train with a fixed seed, inspect the fit and sampled routes, and export a self-contained project including crossing assets.
5. **Balthasar verification.** Import the fresh export through `projects/`, advance the city run, verify the requested direction and moving vehicles, and record run settings, arrivals, insertion, recoveries, and limitations. Verify pedestrian crossing behavior independently of the neural route model.

## Decision points

- A video trajectory study of one evening camera view cannot establish the morning Nicolina–Palat split, local mean speed, average bumper gap, or pedestrian demand. Those remain scenario assumptions until local measurements are obtained.
- The current Balthasar city API exposes vehicles and vehicle traffic. Pedestrian simulation in SUMO and pedestrian display/measurement in Balthasar are separate capabilities; report each explicitly.
- Training must follow the final network conversion because V3 binds the model and dataset to the exact network SHA-256 and legal turn order.

## Completion record

The ordered work above was carried out in October 2026. The [evidence audit](../../research/data/podu-ros/v3-evidence.md), [trained project](../../../experiments/drafts/podu-ros-neural/README.md), [training report](../../../experiments/drafts/podu-ros-neural/training-report.json), and [1,000-second Balthasar evaluation](../../../experiments/drafts/podu-ros-neural/evaluation.json) contain the results. The network supports inferred crossings and test pedestrians. A measured morning turning split, pedestrian demand, and car spacing remain unavailable; site survey and counts would be needed to calibrate the scenario.
