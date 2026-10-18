# Casper camera demo: plan and status

**Branch:** `casper-can-demo`, based on `origin/casper-camera-taco` at `5a64f98`.
**Last reviewed:** 18 October 2026. Update the status table and evidence links as work lands.
**Goal:** From one configurable local traffic video, show detected vehicles and their assigned lanes in Casper, then show fresh lane counts traveling through the camera worker → Casper → TACO path. Keep the same design usable by a later Raspberry Pi worker without requiring the Pi to know the city map.

## Demo boundary

- The local demo uses a video file and an explicit demo camera service. The file path and model path come from local configuration; neither is embedded in code or committed as a machine-specific path. A real clip and model must be available before the video demo can run. Report missing inputs clearly.
- Casper is a normal Compose service and remains visible when the base stack starts. The video worker is an opt-in `camera-demo` service for local testing. One documented Compose command starts the complete demo. Do not require a physical USB camera.
- Melchior may remain connected to the simulator. Casper's own page and TACO endpoints show that observations arrive; no special Melchior mode or fake light controller is needed. Switching Melchior to Casper is an optional read-only demonstration: Casper advertises `signal_control=false` and does not control lamps.
- The camera worker performs detection and counting locally. Normal observations contain counts, timestamps, status, camera identity and lane IDs; they contain no video or tracking IDs. An explicitly enabled local preview may send a bounded, annotated image to Casper for the demo. Disable that preview by default outside the local demo and keep only the latest image in memory.

## What the camera box needs

At startup: `camera_id`, a per-camera token, Casper's URL, and a video source. Bundle or configure the detector model in the image. Casper owns the city map: camera-to-lane ownership, streets, intersections, stoplights, and TACO geometry. The worker needs only its **view calibration**: image-space lane polygons, optional directed counting lines, and measurement thresholds. These are needed to turn detected boxes into per-lane counts; they do not give the worker the road network.

For the first working demo, the existing `camera.json` may be mounted read-only. The target setup is camera registration followed by an authenticated `GET` for its versioned view calibration. Casper should serve only that camera's lanes and image-space settings. A revision change should make the worker reload calibration and mark the first measurement window partial. A changed camera crop, angle, or placement requires a new visual calibration.

## Status on this branch

| Area | State on 18 Oct | Evidence / next move |
| --- | --- | --- |
| Camera worker accepts a file, stream or USB source and runs YOLOX ONNX | Exists, not packaged for Compose demo | `components/detector/camera/worker.py`, `detector.py`; package dependencies and model for the opt-in video service. |
| One worker posts authenticated, timestamped lane observations | Exists | `POST /api/v1/cameras/{id}/observations`; keep sequence, revision and stale-data checks. |
| Casper maps multiple camera IDs to its own lanes and TACO map | Exists for configured cameras | `components/detector/Streetflow.Casper/Configuration.cs` and `ObservationStore.cs`; verify two-camera configuration without double-counting lanes. |
| Casper starts in ordinary Compose | Missing | `casper` is under the `cameras` profile; make its startup and token/configuration behavior explicit. |
| A real video camera container starts for the demo | Missing | `compose.preview.yaml` has only a synthetic `--demo` worker. Add a video-file service with a configurable source and model. |
| Browser shows camera detections and lane assignment | Missing | Casper shows aggregate counts; add opt-in annotated preview with boxes, lane polygons, line direction, age, and measurement status. Label synthetic versus video-derived data. |
| Quick calibration from Casper | Partial | Existing `/casper/calibration.html` draws on a manually uploaded snapshot and downloads JSON. Add an opt-in live snapshot and a save/fetch path for versioned worker calibration. |
| Melchior reads Casper through TACO | Exists, optional for this demo | Select `http://casper:8080` if desired; default may stay `http://simulator:8080`. Casper has no signal control. |
| Clean-checkout CI for simulator projects | Broken upstream | The parent branch removed tracked `experiments/drafts` while tests still load them; coordinate the `projects/` migration separately. Do not commit possibly older local projects as a shortcut. |

## Work order and acceptance checks

1. **Make the boxes observable.** Put Casper in the ordinary Compose stack. Add a separate opt-in `camera-demo` service using a local file, bundled Python dependencies, configured model, per-camera token and read-only mounts. Document the exact Compose command, expected container names, health results, and stop/cleanup command. A missing clip or model must produce one clear startup error.
2. **Prove real video ingestion.** Run a rights-cleared traffic clip through the actual detector rather than `--demo`. Verify detection frames, valid lane observations, Casper's latest per-camera status, and TACO traffic/queue responses. Record the clip, model version, source settings, observed sequence/time, and any missed or double-counted cars. Synthetic counts remain a separately labelled wiring check.
3. **Give Casper visual feedback.** Show a current annotated frame for the selected camera in Casper: detector boxes, lane polygon outlines, counting-line direction, which lane each vehicle was assigned to, and visible/crossed/queued counts. Show frame age and unavailable/partial status. Preview is explicit, low rate, authenticated, memory-only, and local-demo scoped; counts continue if preview is off.
4. **Make calibration quick.** Start from the camera's latest snapshot in Casper, draw or adjust polygons and counting lines, preview assignments on the clip, then save a revision. Have the worker fetch the camera-specific calibration by ID after authenticating. Keep the central map and stoplight ownership in Casper. Reject or clearly mark observations from an old revision.
5. **Run the full demo and record it.** A clean local setup starts the main stack and optional video worker with one documented Compose command. Casper's page shows the image and live counts; its TACO map and binary queue frame match those lanes. Melchior remains on the simulator and works normally. Optionally switch Melchior to Casper to show the same observations with signal commands unavailable. Stop the stack and list any retained named volumes/images.

## Checks worth keeping

- A small parser/configuration test checks that camera ID, token, source and calibration revision are validated. State exactly what it proves.
- A real clip smoke test uses an available model and video file, and checks that at least one video-derived observation reaches Casper. Skip with an explicit reason when those inputs are absent; do not treat the synthetic path as proof of detection.
- A Compose smoke check verifies the expected services, health, TACO responses and preview status. Run against Docker Desktop's `desktop-linux` context, use Compose for startup and cleanup, and report images, volumes and files left behind.
- A two-camera ingestion check verifies separate IDs, unique lane ownership and independent freshness. It does not require two physical cameras.

## Open decisions and limits

- Select a legal, representative traffic clip and a model file for the demo. Video content must not be hardcoded into the worker or Compose; configure file paths locally. The current example polygons are placeholders until drawn against that clip.
- Decide whether preview frames are uploaded by the worker or fetched from a temporary worker endpoint. Prefer the route that keeps only one bounded frame per camera in Casper and avoids exposing a new camera port. Keep preview independent of the observation/TACO contract.
- The current tracker uses simple box overlap, and queueing uses image motion and dwell. A visually convincing demo needs inspection against the chosen clip; these values are estimates, not validated road measurements.
- Remote Pi networking, USB integration, permanent video storage and physical signal control are outside this demo. The camera ID/token/configuration flow should still allow them later.
