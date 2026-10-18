# Casper camera demo: plan and status

**Branch:** `casper-can-demo`, based on `origin/casper-camera-taco` at `5a64f98`.
**Last reviewed:** 18 October 2026, after the one-command sample demo check.
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
| Camera worker accepts a file, stream or USB source and runs YOLOX ONNX | Implemented and packaged | `components/detector/camera/Dockerfile`; missing file/model paths fail at startup. |
| One worker posts authenticated, timestamped lane observations | Exists | `POST /api/v1/cameras/{id}/observations`; keep sequence, revision and stale-data checks. |
| Casper maps multiple camera IDs to its own lanes and TACO map | Exists for configured cameras | `components/detector/Streetflow.Casper/Configuration.cs` and `ObservationStore.cs`; verify two-camera configuration without double-counting lanes. |
| Casper starts in ordinary Compose | Verified on local Docker daemon and Docker Desktop | All four base services healthy without a token. Docker Desktop needs the worktree shared for direct bind mounts; a temporary named-volume override verified startup there. |
| A real video camera container starts for the demo | Verified on local Docker daemon and Docker Desktop | `compose.preview.yaml` runs actual YOLOX inference with configured read-only video/model inputs. |
| Browser shows camera detections and lane assignment | Implemented; HTTP preview verified | Casper uses Melchior’s map renderer to show traffic-light icons plus camera detectors. Selecting either device opens the related camera picture and counts. |
| Quick calibration from Casper | Implemented; API verified | The local demo editor loads the camera picture, accepts a dragged counting box, and saves without JSON, a revision name, or a user-entered token. Box accuracy still depends on the selected clip. |
| Melchior reads Casper through TACO | Exists, optional for this demo | Select `http://casper:8080` if desired; default may stay `http://simulator:8080`. Casper has no signal control. |
| Clean-checkout CI for simulator projects | Still outside this branch's scope | The parent branch removed tracked `experiments/drafts` while tests still load them; coordinate the `projects/` migration separately. |

## Evidence from 18 October

- Source: [Cars Passing by at Night](https://commons.wikimedia.org/wiki/File:Cars_Passing_by_at_Night.webm), Maximilian Schönherr, CC BY-SA 4.0. A resized 960×540 H.264 copy and attribution are included under `assets/casper-demo/`. Model: official YOLOX Nano ONNX release `0.1.1rc0`, 416×416 input, confidence threshold 0.35, worker sampling 3 fps. The model remains under ignored `.data/casper-demo/` and is downloaded by the launcher if absent.
- Direct worker → Casper run produced nine observations: seven from video, then two unavailable after end of file. Maximum visible count was two; crossed and queued counts stayed zero in the sampled windows. These are not hand-labelled accuracy results. TACO traffic identified `north-1`, `north-street`, `junction-1`, `north-light`, UTC time, and `camera_image_tracking`; binary queue response was 26 bytes. Preview JPEG was 46–53 KiB.
- Local Docker daemon: all five services started; Casper and frontend were healthy, `camera-demo` posted video observations, frontend `/casper-camera-api` exposed preview status, and TACO traffic reported two visible vehicles. Base Compose then started all four services healthy without a camera token. Both stacks were stopped with `compose down`.
- Authenticated calibration save changed `camera-1` to `camera-2` during the real video run; the worker fetched the new revision and emitted a fresh partial observation while staying alive. A separate Compose run saved `camera-compose-2` to `casper-data`, restarted Casper, and read back that revision. All 12 detector tests passed with the official model and a detected frame from the licensed clip.
- Docker Desktop `desktop-linux`: direct Compose bind mounts were still denied after moving to this sibling worktree. With temporary named volumes containing the config, clip, model and projects, all five services started; the four base services were healthy. Through the frontend, camera `pi-01` reported an `ok` measurement and preview, sequence 5, and TACO traffic showed two visible vehicles on `north-1`. The queue response was 26 bytes and the JPEG preview was 57,609 bytes. The temporary stack, its seven volumes and its five images were removed after the check. The direct documented Compose command still needs this worktree added to Docker Desktop file sharing.
- After the local-daemon checks, the test containers, all five project images, the three project volumes, and the cache records created by those builds were removed from that daemon. The five images and three volumes from the earlier failed Docker Desktop bind-mount attempt were also removed. Further Docker checks must use `desktop-linux`.
- `./scripts/casper-demo.sh start` now starts the full demo on `desktop-linux` using named volumes, avoiding Docker Desktop host file sharing. The sample video loops. All five services became healthy, observations continued after the loop, the preview returned JPEG, and the local editor saved a new revision without a typed token. The worker fetched it and resumed observations. Twelve detector tests passed. The easy demo is left running for browser inspection; `./scripts/casper-demo.sh stop` stops it.
- Casper’s main page now reuses Melchior’s map renderer. The demo TACO map supplies one traffic light and one detector; the camera is associated with them by its configured lane. Selecting a light or camera opens its picture and counts. The counting-area editor is linked from the selected camera, and both Casper pages link back to the MAGI main page. The shared renderer still contains the traffic-light icon drawing; the previous Casper page simply never displayed that map. The demo has no live signal color, so the light is shown as unreported.

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
