# Podu Roș morning Neural Routes

This is a trained version 3 project for a **scenario**, not a calibrated reconstruction of a measured morning. The starting city time is 07:30. From 07:00–09:00 the authored rates request 600 cars/hour from Șoseaua Nicolina toward Strada Palat and 60 cars/hour in the reverse direction. Two background routes request 120 cars/hour from Socola to Palat and 80 cars/hour from Cantemir to Palat. Other hours request no new cars. These numbers implement the user's directional description; no published source found for a local morning turning split. The [evidence audit](../../../docs/research/data/podu-ros/v3-evidence.md) records what is measured and what remains unknown.

The model was trained on the four complete legal routes in [`route-dataset.json`](route-dataset.json), using the standalone trainer, seed 42 and 200 epochs. Its [report](training-report.json) samples 32 routes per origin with zero route failures and reports training loss 0.000975 versus a memoryless per-road/per-period training baseline of 0.111171. These are fit checks against the authored scenario, not independent validation of real Podu Roș traffic.

## Crossings and pedestrians

The network was derived from the existing Podu Roș road network using SUMO 1.28.0 `netconvert --sidewalks.guess --crossings.guess`. It retains 106 ordinary road edges and has 73 inferred crossing and 126 walking-area edges. The original road network has no explicit pedestrian crossings. The retained OSM source has 30 tagged crossing nodes in the conversion box, but SUMO 1.28.0 crashed on a direct import with `--osm.crossings` in this environment. The 73 inferred crossings have **not** been checked against each tagged location or surveyed in person.

[`pedestrian-flows.add.xml`](pedestrian-flows.add.xml) adds two assumed demonstration flows, 30 pedestrians/hour each, for 90 simulated minutes after run start. SUMO test output confirmed people traversing the Nicolina `:31202499_c0` and Palat `:276516001_c0` crossing edges. These flows are not measured pedestrian demand. They start relative to simulation time zero, independent of the displayed city start time. SUMO models vehicle–pedestrian interaction at crossings; Balthasar's current city snapshot and TACO traffic API report vehicles, not pedestrian positions or pedestrian crossing counts.

The network encodes passenger-lane speed limits of 30–50 km/h (median 50 km/h across 190 lanes). Balthasar leaves this project's car length, minimum gap and desired headway at SUMO passenger defaults: 5 m, 2.5 m and 1 s respectively. They are model inputs, **not observed average speeds or distances between cars**.

An [exploratory speed estimate](../../../docs/research/data/podu-ros/visible-speed-estimate.json) from the authors' selected **evening camera** car tracks gives 19.26 km/h across camera-time samples and 27.45 km/h when averaging per-track mean speeds. The [method and limits](../../../docs/research/data/podu-ros/v3-evidence.md) matter: these are our projected estimates, not author-reported ground truth or morning intersection speeds. No observed average spacing was found.

A [recorded Balthasar evaluation](evaluation.json) advanced the exported project for 1,000 simulated seconds with seed 42 and a 07:30 start. It inserted 255 vehicles, recorded 241 arrivals, and had zero gridlock recoveries, pending entries, or backlog. Ten ordinary-lane snapshots gave a car-weighted mean speed of 36.83 km/h and a mean adjacent same-lane bumper separation of 43.89 m. These two values are **model output only**. The [evaluation script](evaluate.py) records the method and can be rerun against a running simulator.

## Run and reproduce

Place this folder or a ZIP of its contents in `projects/`, refresh Loader, choose `podu-ros-neural`, seed 42 and 07:30, then load and advance the paused run. External traffic lights start off, so the generated signal plans are not real-world timed plans. The package has no authored physical-device mapping.

To rebuild the network with SUMO 1.28.0:

```sh
netconvert --sumo-net-file experiments/drafts/podu-ros/network.net.xml \
  --output-file /tmp/podu-ros-pedestrian.net.xml \
  --sidewalks.guess --crossings.guess
```

The V3 dataset is bound to the exact `network.net.xml` bytes, so regenerating a network with a different SUMO release requires regenerating and reviewing the dataset before training. With the trainer's Python environment installed, from the repository root:

```sh
PYTHONPATH=components/route-trainer python3 docs/research/data/podu-ros/prepare_v3_dataset.py \
  experiments/drafts/podu-ros-neural/network.net.xml /tmp/podu-ros-morning-dataset.json
PYTHONPATH=components/route-trainer python3 -m route_trainer train \
  --project experiments/drafts/podu-ros-neural \
  --dataset /tmp/podu-ros-morning-dataset.json \
  --output /tmp/podu-ros-neural-retrained \
  --id podu-ros-neural-retrained --name "Podu Ros morning neural routes" \
  --epochs 200 --seed 42
```

The network, trained ONNX model, authoring snapshot, report, and pedestrian flows are already included in this folder. The city run uses the model and arrival periods; it does not read the authoring snapshot or report at runtime.
