"""Optional real-model/video test: set CASPER_TEST_MODEL and CASPER_TEST_IMAGE."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

from test_api import ROOT, free_port, request, start_service


@unittest.skipUnless(os.environ.get("CASPER_TEST_MODEL") and os.environ.get("CASPER_TEST_IMAGE"),
                     "Set model/image paths to exercise real YOLOX inference and video capture")
class VideoTests(unittest.TestCase):
    def test_recorded_video_reaches_casper_as_camera_measurements(self):
        import cv2
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            image = cv2.imread(os.environ["CASPER_TEST_IMAGE"])
            self.assertIsNotNone(image)
            height, width = image.shape[:2]
            video = directory / "traffic.avi"
            writer = cv2.VideoWriter(str(video), cv2.VideoWriter_fourcc(*"MJPG"), 10, (width, height))
            self.assertTrue(writer.isOpened())
            for _ in range(65):
                writer.write(image)
            writer.release()
            processes = []
            with (directory / "services.log").open("w+") as log:
                try:
                    service, base = start_service("components/detector/Streetflow.Casper", free_port(), {
                        "Casper__ConfigurationFile": str(ROOT / "components/detector/examples/configuration.json"),
                        "CASPER_DEMO_CAMERA_TOKEN": "video-test-token"}, log)
                    processes.append(service)
                    worker = subprocess.Popen([sys.executable,
                        str(ROOT / "components/detector/camera/worker.py"), "--config",
                        str(ROOT / "components/detector/examples/camera.json"),
                        "--casper", base, "--source", str(video),
                        "--model", os.environ["CASPER_TEST_MODEL"]],
                        env={**os.environ, "CASPER_DEMO_CAMERA_TOKEN": "video-test-token"},
                        stdout=log, stderr=log)
                    processes.append(worker)
                    session = request(base, "/api/v1/taco/session")[1]["id"]
                    for _ in range(100):
                        sample = request(base, "/api/v1/taco/sessions/" + session + "/traffic")[1]
                        lane = sample["lanes"][0]
                        if lane["queued_vehicle_count"] is not None and lane["queued_vehicle_count"] >= 1:
                            self.assertEqual(lane["measurement_method"], "camera_image_tracking")
                            self.assertGreaterEqual(lane["visible_vehicle_count"], 1)
                            self.assertIsNone(lane["mean_speed_mps"])
                            break
                        self.assertIsNone(worker.poll(), "Camera worker unexpectedly exited")
                        time.sleep(0.1)
                    else:
                        self.fail("Real video did not deliver detected stationary traffic")
                except Exception:
                    log.flush()
                    log.seek(0)
                    print(log.read()[-12000:])
                    raise
                finally:
                    for process in reversed(processes):
                        process.terminate()
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()
