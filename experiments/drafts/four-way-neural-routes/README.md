# Four-way intersection: Neural Routes

A synthetic version 3 project trained from the manually authored [example dataset](../../../components/route-trainer/examples/four-way-routes.json) on the unchanged `tiny-graph` map. It demonstrates time-dependent entry volume and route shares, CPU model export, simulator import, and deterministic restart. The values are test assumptions, not traffic measurements.

Morning demand runs 07:00–09:00: south entry requests 400 vehicles/hour, of which 75% go north and 25% east; north entry requests 40 vehicles/hour toward south. Evening demand runs 16:00–19:00: north requests 400 vehicles/hour, of which 75% go south and 25% west; south requests 40 vehicles/hour toward north. Other hours have zero demand. These are training targets; generated samples fluctuate and the neural model is an approximation.

`route-dataset.json` preserves the training input. `training-report.json` records a CPU benchmark and fit checks, not independent validation against observed traffic. The ONNX model has 19284 learned parameters. This one-junction example cannot demonstrate complex history-dependent decisions.

Import this folder or a ZIP of its contents through the simulator loader. Compare 07:30 with 17:00. The model and arrival schedule operate independently of traffic-light control. To retrain, run from `components/route-trainer`:

```sh
python -m route_trainer train \
  --project ../../experiments/drafts/tiny-graph \
  --dataset examples/four-way-routes.json \
  --output /tmp/four-way-neural-routes \
  --id four-way-neural-routes --name "Four-way neural routes" --epochs 100 --seed 42
```

See the [trainer guide](../../../components/route-trainer/README.md) and [v3 runtime contract](../../../docs/components/simulator/neural-routes.md).
