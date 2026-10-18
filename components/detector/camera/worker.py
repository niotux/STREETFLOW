"""Pi camera worker: latest-frame capture, CPU inference, bounded metadata delivery."""
import argparse
from collections import deque
from datetime import datetime, timezone
import json
import logging
import os
from pathlib import Path
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from metrics import Measurements

LOG = logging.getLogger("casper.camera")


def utc(seconds):
    return datetime.fromtimestamp(seconds, timezone.utc).isoformat().replace("+00:00", "Z")


class Publisher:
    def __init__(self, url, token):
        self.url, self.token = url, token
        self.queue = deque(maxlen=1)
        self.condition = threading.Condition()
        self.stopping = False
        self.rejected = False
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def submit(self, frame):
        with self.condition:
            if self.queue:
                LOG.warning("Delivery lag: replacing unsent observation with latest state")
            self.queue.append(frame)
            self.condition.notify()

    def run(self):
        delay = 1
        while not self.stopping:
            with self.condition:
                self.condition.wait_for(lambda: self.queue or self.stopping)
                if self.stopping:
                    return
                frame = self.queue.popleft()
            request = urllib.request.Request(self.url, json.dumps(frame).encode(),
                {"Authorization": "Bearer " + self.token, "Content-Type": "application/json"}, method="POST")
            try:
                with urllib.request.urlopen(request, timeout=5) as response:
                    response.read()
                delay = 1
            except (urllib.error.URLError, TimeoutError, OSError) as error:
                # Do not log camera URLs or tokens (RTSP URLs may contain passwords).
                LOG.warning("Observation delivery failed (%s)", getattr(error, "code", type(error).__name__))
                if isinstance(error, urllib.error.HTTPError) and error.code == 409:
                    try:
                        if json.load(error).get("code") == "changed_configuration":
                            LOG.info("Waiting for updated camera calibration")
                            delay = 1
                            continue
                    except (ValueError, OSError):
                        pass
                if isinstance(error, urllib.error.HTTPError) and 400 <= error.code < 500 and error.code != 429:
                    LOG.error("Rejected camera configuration/token; fix settings before restarting")
                    self.rejected = True
                    self.stopping = True
                    return
                with self.condition:
                    if not self.queue:
                        self.queue.append(frame)  # Retry identical sequence/payload.
                    self.condition.wait(timeout=delay)
                delay = min(15, delay * 2)

    def close(self):
        self.stopping = True
        with self.condition:
            self.condition.notify_all()
        self.thread.join(timeout=6)


class Capture:
    def __init__(self, source, loop_video=False):
        self.source = int(source) if source.isdecimal() else source
        self.video = isinstance(self.source, str) and Path(self.source).is_file()
        self.loop_video = loop_video and self.video
        self.loops = 0
        self.latest = None
        self.done = False
        self.stopping = False
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        import cv2
        while not self.stopping:
            cap = cv2.VideoCapture(self.source)
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            if not cap.isOpened():
                cap.release()
                LOG.warning("Camera unavailable; retrying")
                time.sleep(2)
                continue
            fps = cap.get(cv2.CAP_PROP_FPS)
            if not 0 < fps < 300:
                fps = 25
            start_mono, start_utc, index = time.monotonic(), time.time(), 0
            try:
                while not self.stopping:
                    ok, image = cap.read()
                    if not ok:
                        break
                    if self.video:
                        elapsed = index / fps
                        time.sleep(max(0, start_mono + elapsed - time.monotonic()))
                        observed = start_utc + elapsed
                    else:
                        observed = time.time()
                    self.latest = (observed, image)
                    index += 1
            finally:
                cap.release()
            if self.video:
                if not self.loop_video:
                    self.done = True
                    return
                self.latest = None
                self.loops += 1
                LOG.info("Sample video starting again")
                continue
            LOG.warning("Camera disconnected; reconnecting")
            time.sleep(2)

    def close(self):
        self.stopping = True
        self.thread.join(timeout=1)


def fetch_calibration(base, camera_id, token):
    url = base.rstrip("/") + "/api/v1/cameras/" + urllib.parse.quote(camera_id, safe="") + "/calibration"
    request = urllib.request.Request(url, headers={"Authorization": "Bearer " + token})
    with urllib.request.urlopen(request, timeout=5) as response:
        return json.load(response)


class PreviewPublisher(Publisher):
    def submit_image(self, image, observed, revision, status):
        with self.condition:
            self.queue.append((image, observed, revision, status))
            self.condition.notify()

    def run(self):
        while not self.stopping:
            with self.condition:
                self.condition.wait_for(lambda: self.queue or self.stopping)
                if self.stopping:
                    return
                image, observed, revision, status = self.queue.popleft()
            request = urllib.request.Request(self.url, image, {
                "Authorization": "Bearer " + self.token, "Content-Type": "image/jpeg",
                "X-Observed-At": utc(observed), "X-Calibration-Revision": revision,
                "X-Measurement-Status": status,
            }, method="POST")
            try:
                with urllib.request.urlopen(request, timeout=5) as response:
                    response.read()
            except (urllib.error.URLError, TimeoutError, OSError) as error:
                LOG.warning("Preview delivery failed (%s)", getattr(error, "code", type(error).__name__))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True)
    parser.add_argument("--casper", default="http://localhost:8082")
    parser.add_argument("--source", default="0", help="USB index, RTSP URL, or recorded video")
    parser.add_argument("--model", help="Official YOLOX nano/tiny ONNX weights")
    parser.add_argument("--size", type=int, default=416)
    parser.add_argument("--decoded", action="store_true")
    parser.add_argument("--fps", type=float, default=3)
    parser.add_argument("--threshold", type=float, default=0.35)
    parser.add_argument("--demo", action="store_true", help="Explicit synthetic measurements; no camera/model needed")
    parser.add_argument("--fetch-calibration", action="store_true", help="Fetch this camera's view calibration from Casper")
    parser.add_argument("--preview", action="store_true", help="Upload low-rate annotated JPEGs for a local demo")
    parser.add_argument("--loop-video", action="store_true", help="Replay a local video for a continuous demo")
    args = parser.parse_args()
    if not 0.5 <= args.fps <= 30 or args.size <= 0 or args.size % 32 or not 0 < args.threshold <= 1:
        parser.error("Invalid inference FPS, input size, or confidence threshold")
    if not args.demo and not args.model:
        parser.error("--model is required for camera processing; use --demo for synthetic input")
    if not args.demo and args.model and not Path(args.model).is_file():
        parser.error("Model file is missing: " + args.model)
    if not args.demo and not args.source.isdecimal() and "://" not in args.source and not Path(args.source).is_file():
        parser.error("Video file is missing: " + args.source)
    if args.demo and args.preview:
        parser.error("Preview requires real video inference")
    config = json.loads(Path(args.config).read_text())
    if not config.get("detector_id") or not config.get("token_environment"):
        parser.error("Camera ID and token environment are required")
    token = os.environ.get(config["token_environment"])
    if not token:
        parser.error("Set the camera token environment variable")
    if args.fetch_calibration:
        try:
            config = fetch_calibration(args.casper, config["detector_id"], token)
        except (urllib.error.URLError, TimeoutError, OSError, ValueError) as error:
            parser.error("Cannot fetch camera calibration (%s)" % getattr(error, "code", type(error).__name__))
    metrics = Measurements(config)
    publisher = Publisher(args.casper.rstrip("/") + "/api/v1/cameras/" +
                          urllib.parse.quote(config["detector_id"], safe="") + "/observations", token)
    session, sequence = uuid.uuid4().hex, 0
    capture = None
    preview = None
    if not args.demo:
        from detector import Detector
        detector = Detector(args.model, args.size, args.threshold, args.decoded)
        capture = Capture(args.source, args.loop_video)
        if args.preview:
            preview = PreviewPublisher(args.casper.rstrip("/") + "/api/v1/cameras/" +
                urllib.parse.quote(config["detector_id"], safe="") + "/preview", token)
    last_frame, last_unavailable = 0, time.time()
    last_preview, last_calibration = 0, time.monotonic()
    recent_counts = {}
    last_status = "partial"
    last_loop = 0
    deadline = time.monotonic()
    try:
        while not publisher.stopping:
            time.sleep(max(0, deadline - time.monotonic()))
            deadline = time.monotonic() + 1 / args.fps
            now = time.time()
            if args.fetch_calibration and time.monotonic() - last_calibration >= 3:
                last_calibration = time.monotonic()
                try:
                    updated = fetch_calibration(args.casper, config["detector_id"], token)
                    if updated["detector_config_revision"] != config["detector_config_revision"]:
                        metrics = Measurements(updated)
                        config = updated
                        last_status = "partial"
                        recent_counts = {}
                        LOG.info("Loaded camera calibration revision %s", config["detector_config_revision"])
                except (urllib.error.URLError, TimeoutError, OSError, ValueError) as error:
                    LOG.warning("Calibration refresh failed (%s)", getattr(error, "code", type(error).__name__))
            if args.demo:
                # Clearly labelled synthetic queue, for testing wiring without model/hardware.
                result = metrics.update([(0.15, 0.4, 0.25, 0.55), (0.3, 0.5, 0.4, 0.65)], now)
            else:
                if capture.loops != last_loop:
                    metrics = Measurements(config)
                    last_loop = capture.loops
                    last_status = "partial"
                    recent_counts = {}
                latest = capture.latest
                if latest is not None and latest[0] > last_frame and now - latest[0] < metrics.max_gap:
                    observed, image = latest
                    boxes = detector.detect(image)
                    result = metrics.update(boxes, observed)
                    last_frame, last_unavailable = observed, now
                    if preview and now - last_preview >= 1:
                        from preview import annotated_jpeg
                        if result:
                            recent_counts = {lane["lane_id"]: lane for lane in result[3]}
                            last_status = result[2]
                        encoded = annotated_jpeg(image, boxes, config, last_status, recent_counts)
                        if encoded:
                            preview.submit_image(encoded, observed, config["detector_config_revision"], last_status)
                        last_preview = now
                elif now - last_unavailable >= metrics.window_seconds:
                    result = metrics.unavailable(now)
                    last_unavailable = now
                else:
                    result = None
            if result:
                start, end, status, lanes = result
                sequence += 1
                frame = {
                    "schema_version": "1", "source": "camera", "time_basis": "utc",
                    "synthetic_demo": args.demo,
                    "session_id": session, "sequence": sequence,
                    "network_revision": config["network_revision"],
                    "detector_config_revision": config["detector_config_revision"],
                    "detector_id": config["detector_id"], "intersection_id": config["intersection_id"],
                    "window": {"start": utc(start), "end": utc(end)}, "status": status, "lanes": lanes,
                }
                publisher.submit(frame)
                LOG.info("%s observation %d (%s)", "SYNTHETIC DEMO" if args.demo else "Camera", sequence, status)
            if capture is not None and capture.done:
                # Publish unavailable at the next interval rather than leaving an EOF queue looking live.
                capture.latest = None
    except KeyboardInterrupt:
        pass
    finally:
        if capture:
            capture.close()
        if preview:
            preview.close()
        publisher.close()
    return 1 if publisher.rejected else 0


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    raise SystemExit(main())
