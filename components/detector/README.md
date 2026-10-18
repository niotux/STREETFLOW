# Casper and Raspberry Pi camera workers

Camera → Pi detection/tracking → Casper → TACO → Melchior.

The worker processes video locally and sends small JSON measurements without footage, licence plates, or track IDs. An optional local-demo preview sends one bounded annotated JPEG at a time to Casper; it is disabled in the base stack. Casper is a .NET 10 service that maps camera lanes to streets, intersections, and stoplights. The Python worker supports USB indices, RTSP, and recorded files. No paid inference API, accelerator, message broker, or database is required.

## Try the sample video

Start Docker Desktop, then run this from the repository root:

```sh
./scripts/casper-demo.sh start
```

Open **http://localhost:8090/casper/**. The map shows the sample traffic light and its camera. Click either icon to see the camera picture and detected car counts. The 16-second clip repeats automatically. In that camera’s details, choose **Adjust this camera’s counting area** to drag a box over the road and press **Save changes**. Use **Back to device map** to return, or the Streetflow logo at the top left to leave Casper. You do not need to edit JSON, find a snapshot, name a revision, or enter a token.

The video is included at `assets/casper-demo/cars-night-960.mp4`. It is a resized copy of [Cars Passing by at Night](https://commons.wikimedia.org/wiki/File:Cars_Passing_by_at_Night.webm) by Maximilian Schönherr, [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). The launcher downloads the official YOLOX Nano model on first use into ignored `.data/casper-demo/`, creates a private demo token, and copies inputs into Docker Desktop volumes. It uses the `desktop-linux` Docker context and does not require Docker Desktop to share this worktree for file mounts. Docker images and volumes are stored in Docker Desktop, not the default Docker context.

To stop the demo:

```sh
./scripts/casper-demo.sh stop
```

The saved counting area stays in the demo volume for the next start. The clip is an illustration, not a verified traffic count. Melchior continues using the simulator by default.

## Demo without hardware

From the repository root, set a token in every terminal that runs Casper or the demo worker:

```sh
export CASPER_DEMO_CAMERA_TOKEN="$(openssl rand -hex 24)"
```

Privately copy that same value to the worker terminal. Start Casper:

```sh
Casper__ConfigurationFile="$PWD/components/detector/examples/configuration.json" \
  dotnet run --project components/detector/Streetflow.Casper --urls http://localhost:8082
```

In another terminal, with the same token:

```sh
python3 components/detector/camera/worker.py \
  --config components/detector/examples/camera.json --demo
```

Start the other services in separate terminals:

```sh
dotnet run --project components/streetflow/Streetflow.Logistics
dotnet run --project components/frontend/Streetflow.Frontend
```

Open http://localhost:8090/casper/. The two synthetic vehicles become queued after the dwell time; their data is marked **SYNTHETIC DEMO**. In Melchior, connect to http://localhost, port 8082. Select North stoplight to see its counts. Light controls remain disabled because Casper has no physical controller adapter.

Or start the ordinary Compose stack, including Casper, without a camera token:

```sh
docker --context desktop-linux compose up --build -d --wait
```

Containerized Melchior still defaults to the simulator. Casper starts without a token and shows unavailable camera data until a worker is configured; camera writes require the configured token. Compose binds Casper to localhost; remote Pis need an explicitly configured private-network endpoint or authenticated TLS reverse proxy.

## Real camera

Use a 64-bit OS and Python 3.12. Start with available Pi hardware; benchmark representative footage before choosing hardware for a city. The default is CPU inference at 3 samples/second, 416×416 input. Faster motion or narrow counting regions can require more samples. Actual throughput is not established by desktop tests.

```sh
python3.12 -m venv .venv
.venv/bin/pip install -r components/detector/camera/requirements.txt
mkdir -p .data/models
curl -L --fail -o .data/models/yolox_nano.onnx \
  https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx
```

The adapter uses [official YOLOX Nano ONNX weights](https://github.com/Megvii-BaseDetection/YOLOX/tree/main/demo/ONNXRuntime), OpenCV DNN, COCO output [1,N,85], and BGR preprocessing. YOLOX publishes its code under [Apache 2.0](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/LICENSE). Tiny at the same input size is an optional larger alternative. Official raw exports are decoded by default; --decoded is only for models already exporting decoded boxes.

Copy the example camera JSON for each Pi. Open http://localhost:8090/casper/calibration.html, load a camera snapshot and its JSON, draw the visible lane polygons and counting lines, and download the updated configuration. Outside the opt-in local preview, images remain in the browser. Line endpoint order determines the counted direction. Coordinates are normalized; a new camera crop/position requires recalibration. A worker using `--fetch-calibration` gets the saved revision from Casper; otherwise update `detector_config_revision` on Casper and the worker together.

```sh
.venv/bin/python components/detector/camera/worker.py \
  --config components/detector/examples/camera.json \
  --casper http://localhost:8082 --source 0 --model .data/models/yolox_nano.onnx
```

Replace --source 0 with an RTSP URL or filename. Pi camera modules need an OS-supported capture device or stream; a dedicated libcamera adapter is not included. Recorded files play in real time using a new UTC epoch, not their historical recording date. Keep source credentials out of shared shell histories.

## Mapping and measurement meaning

Casper loads Casper__ConfigurationFile on startup. Configure local-metre map geometry, stoplight-to-lane membership, street/intersection/approach IDs, camera lane ownership, revisions, and freshness limits. Each camera references an environment variable holding its own bearer token. Add those variables to your deployment for additional cameras; example Compose passes only CASPER_DEMO_CAMERA_TOKEN.

Each lane has exactly one authoritative camera, enforced at startup. Distinct lane polygons must cover distinct physical road regions. Do not sum two labels for the same queue. Casper does not infer geography from camera names.

Visible counts use tracked bottom-centre reference points inside a configured polygon. Queue counts are **image-motion estimates**: reference points remaining within stationary_radius of an anchor for queue_duration_seconds. That radius is a fraction of image dimensions, not metres or m/s. Slow creeping traffic, camera shake, occlusion, and box jitter can affect estimates. Physical speed remains null.

Crossings are oriented finite-line passages in completed half-open windows [start,end). Missing counting lines produce null, not zero. Snapshot counts are sampled at actual window end. Initial windows and tracking gaps are partial. The simple IoU tracker does not establish identity through long occlusions.

Casper rejects invalid counts, revision mismatches, overlapping/out-of-order windows, conflicting duplicate sequences, future camera clocks, and retired producer sessions. Identical retries are idempotent. UTC clocks must be synchronized.

Stale/outage lanes return unavailable/null counts with original timestamps and age. Measured empty lanes return zero. Totals are withheld if any contributing value is unavailable. Camera windows are independent; do not interpret a multi-camera response as one simultaneous frame.

## API

| Request | Result |
| --- | --- |
| GET /health | Service health |
| POST /api/v1/cameras/{id}/observations | Authenticated camera ingestion |
| GET/PUT /api/v1/cameras/{id}/calibration | Authenticated, camera-specific view calibration |
| GET /api/v1/cameras | Camera IDs, observation/preview age and status, sequence, and revision |
| GET/POST /api/v1/cameras/{id}/preview | Latest in-memory JPEG; POST authenticated and enabled only for local demo |
| GET /api/v1/taco/session | Casper session/world/revision |
| GET /api/v1/taco/capabilities | Traffic supported; signal control unavailable |
| GET /api/v1/taco/sessions/{id}/map | Configured lanes/stoplights |
| GET /api/v1/taco/sessions/{id}/devices | Unknown lamp states |
| GET /api/v1/taco/sessions/{id}/traffic | Counts by lane, street, intersection, stoplight |

Ingestion and calibration writes use Authorization: Bearer CAMERA_TOKEN. The request body limit is 256 KiB, and the preview JPEG limit is 256 KiB. Physical control returns 501. Read endpoints assume a trusted network, like the existing manual-control stack; protect the network and terminate TLS before remote deployment.

Melchior forwards traffic through GET /api/v1/sessions/{id}/traffic and the frontend through /melchior-api/sessions/{id}/traffic. Casper's page uses /casper-api.

## Tests and city expansion

```sh
dotnet build Streetflow.slnx
python3 -m unittest discover -s components/detector/tests -v
```

Tests cover window boundaries, queues, repeat/reverse passages, gaps, null/zero, authentication, validation, duplicate delivery, staleness, producer resets, and an actual Melchior-to-Casper HTTP request. They do not establish real-camera accuracy.

An optional actual ONNX/video test runs when CASPER_TEST_MODEL points to a Nano ONNX file and CASPER_TEST_IMAGE points to a vehicle image; run it using a Python environment with the camera dependencies. It encodes a short stationary video and verifies capture → real model → queue measurement → authenticated Casper ingestion. This is a pipeline smoke test, not a labelled traffic benchmark.

Capture retains one latest frame; delivery retains one unsent observation with bounded retry/backoff. Excessive detections/tracks are capped and reported partial. Lost delivery windows create sequence gaps and are not reconstructed. Casper stores one latest frame plus sixteen retired session IDs per configured camera; restarting creates a new session and initially unavailable data. No video or measurement history is persisted.

For city rollout, measure real-video counting errors in daylight/night/rain and Pi thermal/CPU performance, add fleet configuration and monitoring, and add durable history only if needed. Physical signal control requires a separately validated adapter. This is a hackathon pipeline, not an evaluated city traffic controller.
