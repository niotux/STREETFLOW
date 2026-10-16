# Route trainer

A separate utility for authoring time-dependent traffic datasets and training version 3 **Neural Routes** projects. It opens SUMO maps and ZIP projects from versions 1, 2, and 3. It does not require a running simulator, SUMO process, PostgreSQL, or GPU. The simulator only needs the exported project.

## Run separately

From the repository root:

```sh
docker compose -f components/route-trainer/compose.yaml up --build -d
```

Open <http://localhost:8092>. This is its own Compose project and volume. It is not included in the main simulation Compose file. Set `TRAINER_PORT` to change its port. Stop it with the same `-f` path and `stop` or `down`; retain the volume to retain uploaded projects and saved datasets.

The purple workspace follows the simulator's map-and-windows layout. Route editing, dataset management, and training have separate tool windows. Move and resize them to suit your screen, reopen them from the toolbar, or reset the layout. Window positions are saved in your browser; on narrow screens the tools stack vertically.

For local development, use Python 3.12:

```sh
cd components/route-trainer
python3 -m venv .venv
.venv/bin/pip install torch==2.14.1 --index-url https://download.pytorch.org/whl/cpu
.venv/bin/pip install -r requirements.txt
.venv/bin/python -m route_trainer serve
```

The default listener is `127.0.0.1:8092`. `TRAINER_DATA_ROOT` controls local storage. Training uses CPU, one PyTorch thread, and a separate worker from HTTP requests. The server accepts one training job at a time.

## Author, train, export

1. Open a ZIP project or a standalone `.net.xml`. The utility copies the upload into its own storage. A ZIP may contain one enclosing project folder.
2. Click a starting road or enter its ID. Select successive roads with the map or the numbered next-road buttons. The start is a **directed road**, because a junction alone does not identify the car's approach.
3. Enter a time span and vehicles/hour. Finish the path to add a complete route. Its last road becomes an allowed trip endpoint. Add other routes and periods manually or import dataset JSON.
4. Export JSON to collaborate with someone editing the dataset. The complete JSON editor can also apply and validate changes. Saved route edits survive a browser reload. Unapplied text edits are drafts; apply or export them before leaving.
5. Enter a new project ID/name and train. Each job uses a snapshot of the saved dataset. Subsequent edits require another training job.
6. Inspect the report, then download the v3 project ZIP and put it in the simulator's `projects/` folder; it appears in Loader after **Refresh list**.

The tool validates routes, lane connectivity, time spans, IDs, and values. It does **not** invent sensible traffic or infer that an authored pattern represents reality. The bundled example is explicitly synthetic.

## Dataset JSON

Dataset format version 1 is independent of project format version 3:

```json
{
  "format_version": 1,
  "network_sha256": "COPY THE HASH FROM THE EXPORTED EMPTY DATASET",
  "vehicle_class": "passenger",
  "repeat_daily": true,
  "routes": [
    {
      "id": "morning-south-north",
      "start_seconds": 25200,
      "end_seconds": 32400,
      "start_edge": "south_in",
      "turns": [1],
      "vehicles_per_hour": 300
    }
  ]
}
```

Time spans are daily, half-open `[start_seconds, end_seconds)`, in seconds after midnight. Split overnight spans into two records. Uncovered hours have zero arrivals; the trainer does not interpolate or extrapolate measurements. With `repeat_daily: false`, new traffic ends at the first midnight after the run's starting time.

Turn indices are **zero based**. For each current road, legal successor road IDs are sorted using .NET ordinal ordering. Index `0` means the first successor, not a particular turn direction. The map view/buttons show the mapping. Multiple lane connections to the same next road form one turn choice. The exact network file's SHA-256 binds the dataset and model to that ordering. Changing the map requires deliberate dataset review and retraining; old indices are never silently remapped.

`turns: []` describes a complete trip along the start road. An explicit `END` output token terminates a route; no integer in the authored turn array is reserved as an end marker. Record complete trips, not truncated observations.

Rows active in the same period are additive. Two same-origin routes at 300 and 100 vehicles/hour request a total of 400 vehicles/hour and contribute training influence in a 3:1 ratio. The loss is weighted directly, without physically duplicating rows. Duplicate paths with different record IDs also add their influence. Route influence across periods accounts for period duration.

This version trains one vehicle class per model. Project imports choose the first positive-weight v1 vehicle type, first v2 vehicle type, or v3 vehicle class. A standalone map uses `passenger`. Dataset class must match. Geometry is used to display/validate the map and check lane feasibility, never as a neural-network feature.

## Model and evaluation

The initial architecture is a 64-unit GRU cell. Each recurrent step consumes embeddings for the current road, previous local turn, and departure period, plus the previous hidden state. Embedding dimensions are 16, 8, and 8. Its outputs are scores for local turn indices and `END`, plus the next hidden state. There is no preselected destination.

The vocabulary and embeddings grow with the number of roads; network work per recurrent step does not require scoring every road. With `R` roads, `P` daily periods, and `K` maximum outgoing choices, this implementation has `16R + 8P + 73K + 18897` learned parameters. Generation cost also grows with cars generated and movements per route. Long routes and large training datasets increase training work and memory. Benchmark your map instead of treating the tiny example's throughput as a guarantee.

Illegal turns are masked during training and generation. Lane feasibility propagates across the selected path, with a conservative 20 m lane-change allowance, matching the existing simulator planning convention. This is not a guarantee against congestion or a calibrated driving model. `END` is allowed only on authored endpoint roads. Legal but unobserved paths can still be generated; learned finite scores are not hard exclusions.

The report contains training loss, a memoryless per-road/per-period turning-frequency baseline loss, authored route shares, sampled generated paths, termination failures, parameter count, and a Python/ONNX CPU generation benchmark. There is no independent observed validation dataset in this workflow: these are checks of fit to authored templates, not a claim of realism or unseen-trip generalization. Sample counts are bounded statistical probes, not exact probability estimates. Reaching the route-length cap is a reported failure, never a fabricated successful exit.

The ONNX export is checked against PyTorch outputs before export completes. The simulator runs ONNX directly in C# on CPU; Python is unnecessary in the simulator container. Training snapshots include the source dataset and report in the ZIP for collaboration. Runtime arrival schedules are exact sums derived from authored rates; they are separate from the learned route-choice distribution so absolute demand is preserved.

## Command line

Run from `components/route-trainer` with the environment above:

```sh
.venv/bin/python -m route_trainer validate \
  --project ../../experiments/drafts/tiny-graph \
  --dataset examples/four-way-routes.json

.venv/bin/python -m route_trainer train \
  --project ../../experiments/drafts/tiny-graph \
  --dataset examples/four-way-routes.json \
  --output /tmp/my-neural-project \
  --id my-neural-project --name "My neural traffic" --epochs 200 --seed 42

.venv/bin/python -m unittest discover -s tests -v
```

The output must be a new folder outside the input project. It contains a ready-to-import v3 project, not just model weights. Inputs are preserved. Export copies the network, optional map assets/devices/terminals, and run defaults, replacing the old demand/model references. It does not include obsolete v1 demand or v2 tensor files.

See [the v3 runtime contract](../../docs/components/simulator/neural-routes.md).
