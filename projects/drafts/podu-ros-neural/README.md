# Podu Roș full-day Neural Routes

This is a trained version 3 project for an authored **scenario**, not a calibrated reconstruction of measured traffic. The starting city time is 07:30. Vehicle arrivals cover the entire repeating day in 96 consecutive 15-minute intervals. The four original complete legal routes are retained, giving 384 dataset records. There are no uncovered hours or overlapping records for a given route.

## Daily demand and rush hours

[`prepare_dataset.py`](prepare_dataset.py) produces [`route-dataset.json`](route-dataset.json). For each route, demand is the sum of a positive overnight floor, a broad daytime Gaussian, and morning/evening Gaussian rush-hour peaks. The morning peak is centered at **08:00** with standard deviation **1.25 hours**; the evening peak is centered at **18:00** with standard deviation **1.5 hours**. The daytime component is centered at 13:00 with standard deviation 4.5 hours. Daily periodic copies make the underlying curve continuous across midnight.

Each stored vehicles/hour value is the **integrated average over its 15-minute interval**, rounded to six decimals. The simulator uses constant rates within each interval, approximating the smooth bell curves while preserving their volumes. Rates are positive at night and repeat daily.

| Route | Overnight floor | Daytime amplitude | Morning amplitude | Evening amplitude |
| --- | ---: | ---: | ---: | ---: |
| Nicolina → Palat | 12 | 48 | 540 | 270 |
| Palat → Nicolina | 1.2 | 4.8 | 54 | 594 |
| Socola → Palat | 2.4 | 9.6 | 108 | 54 |
| Cantemir → Palat | 1.6 | 6.4 | 72 | 36 |

All parameters above are in vehicles/hour. Amplitudes are added to the floor and other components; they are not total peak rates. The larger morning flow toward Palat and evening flow toward Nicolina are explicit assumptions. The background routes retain their original directions. Expected total demand is approximately **7,201 vehicles/day** across these four routes.

These choices extend the previous flat 07:00–09:00 morning scenario; they are **not measured hourly counts, observed rush-hour timing, or a complete intersection demand survey**. The [evidence audit](../../../docs/research/data/podu-ros/v3-evidence.md) describes the original evidence and its limits. Edit the constants and rate amplitudes in the generator, regenerate, and retrain to change the schedule.

The [updated training report](training-report.json) records seed 42 and 691 completed epochs (early stopping from a 1,000-epoch limit), training loss 0.000143, and 12,288 generated route samples (32 per origin per interval) with zero termination failures. Every sampled route followed an authored template. The ONNX export was verified against PyTorch. These are fit checks against authored templates, not independent validation of real traffic.

## Crossings and pedestrians

The network was derived from the existing Podu Roș road network using SUMO 1.28.0 `netconvert --sidewalks.guess --crossings.guess`. It retains 106 ordinary road edges and has 73 inferred crossing and 126 walking-area edges. The original road network has no explicit pedestrian crossings. The retained OSM source has 30 tagged crossing nodes in the conversion box, but SUMO 1.28.0 crashed on a direct import with `--osm.crossings` in this environment. The 73 inferred crossings have **not** been checked against each tagged location or surveyed in person.

[`pedestrian-flows.add.xml`](pedestrian-flows.add.xml) adds two assumed demonstration flows, 30 pedestrians/hour each, for 90 simulated minutes after run start. SUMO test output confirmed people traversing the Nicolina `:31202499_c0` and Palat `:276516001_c0` crossing edges. These flows are not measured pedestrian demand. They start relative to simulation time zero, independent of the displayed city start time. SUMO models vehicle–pedestrian interaction at crossings; Balthasar's current city snapshot and TACO traffic API report vehicles, not pedestrian positions or pedestrian crossing counts.

The network encodes passenger-lane speed limits of 30–50 km/h (median 50 km/h across 190 lanes). Balthasar leaves this project's car length, minimum gap and desired headway at SUMO passenger defaults: 5 m, 2.5 m and 1 s respectively. They are model inputs, **not observed average speeds or distances between cars**.

An [exploratory speed estimate](../../../docs/research/data/podu-ros/visible-speed-estimate.json) from the authors' selected **evening camera** car tracks gives 19.26 km/h across camera-time samples and 27.45 km/h when averaging per-track mean speeds. The [method and limits](../../../docs/research/data/podu-ros/v3-evidence.md) matter: these are our projected estimates, not author-reported ground truth or morning intersection speeds. No observed average spacing was found.

The [recorded Balthasar evaluation](evaluation.json) belongs to the **previous morning-only dataset**. Its speeds, gaps, arrivals and other diagnostics do not evaluate this full-day demand profile. It is retained as historical output and explicitly marked with the previous dataset hash. The [evaluation script](evaluate.py) can produce fresh diagnostics against a running simulator after loading the updated project. The full-day project has not been reevaluated in a running traffic simulation.

## Run and reproduce

Refresh Loader, choose `podu-ros-neural`, then load a new run. Existing runs retain their loaded configuration. Seed 42 and 07:30 remain the defaults. Lights initially start off; Melchior enables automatic cycling from the project’s existing timing programs on discovery. Its Automatic light cycling switch enables manual override. Generated signal plans remain scenario assumptions, not real-world timed plans. The package has no authored physical-device mapping.

The V3 dataset is bound to the exact `network.net.xml` bytes. A changed network requires reviewing the route indices before training. From the repository root, using Python with the route-trainer requirements installed:

```sh
PYTHONPATH=components/route-trainer python3 projects/drafts/podu-ros-neural/prepare_dataset.py \
  projects/drafts/podu-ros-neural/network.net.xml /tmp/podu-ros-full-day-dataset.json
PYTHONPATH=components/route-trainer python3 -m route_trainer validate \
  --project projects/drafts/podu-ros-neural --dataset /tmp/podu-ros-full-day-dataset.json
PYTHONPATH=components/route-trainer python3 -m route_trainer train \
  --project projects/drafts/podu-ros-neural \
  --dataset /tmp/podu-ros-full-day-dataset.json \
  --output /tmp/podu-ros-full-day-retrained \
  --id podu-ros-neural --name "Podu Ros - full-day neural routes" \
  --epochs 1000 --seed 42
```

Use a new output directory for each training job. Deploy the resulting `route-dataset.json`, `neural-model.json`, `route-model.onnx` and `training-report.json` together: changing the dataset alone does not change simulation arrivals. The exported model contains the actual runtime arrival periods.

The network, trained ONNX model, dataset, report, and pedestrian flows are included in this folder. The city run reads the exported model and its arrival periods; it does not read the authoring dataset or training report at runtime.

## Congestion-aware signals

Melchior prepares the existing signal phases and road capacities ahead of time. Its default policy holds buffered long approaches red when the receiving corridor is full, retaining yellow clearance and reopening when space returns. Rush-hour weighting uses stable A/B phase groups: A × k and B ÷ k, with k in [0.2, 1.8], derived from recent arrival/queue demand by default. A/B identities, live k, occupancy and green budgets appear in Melchior. Set a k override there or leave it blank for automatic weighting. These rules use model geometry, assumed car spacing and simulated observations; they are not a calibrated real-world signal plan. See the [control rules](../../../components/streetflow/README.md#downstream-protection-and-ab-weighting).
