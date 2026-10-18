"""Real HTTP contract test. Build the solution first; uses only Python's standard library."""
import copy
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[3]


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def request(base, path, body=None, method=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(base + path, None if body is None else json.dumps(body).encode(),
                                 headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            raw = response.read()
            return response.status, json.loads(raw) if "json" in response.headers.get("Content-Type", "") else raw.decode()
    except urllib.error.HTTPError as error:
        with error:
            return error.code, json.load(error)


def start_service(project, port, env, log):
    dll = ROOT / project / "bin/Debug/net10.0" / (Path(project).name + ".dll")
    process = subprocess.Popen(["dotnet", str(dll), "--urls", "http://127.0.0.1:" + str(port)],
                               cwd=ROOT, env={**os.environ, **env}, stdout=log, stderr=log)
    base = "http://127.0.0.1:" + str(port)
    try:
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError("Service exited during startup; inspect test log")
            try:
                if request(base, "/health")[0] == 200:
                    return process, base
            except (OSError, urllib.error.URLError):
                time.sleep(0.05)
        raise RuntimeError("Service did not become healthy")
    except Exception:
        process.terminate()
        process.wait(timeout=5)
        raise


def utc(seconds):
    return datetime.fromtimestamp(seconds, timezone.utc).isoformat().replace("+00:00", "Z")


class ApiTests(unittest.TestCase):
    def test_two_cameras_have_independent_lane_freshness(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            config = json.loads((ROOT / "components/detector/examples/configuration.json").read_text())
            config["cameras"][0]["view_calibration_file"] = str(ROOT / "components/detector/examples/camera.json")
            config["stale_after_seconds"] = 2
            config["lanes"].append({"id": "south-1", "shape": [{"x": 20, "y": 100}, {"x": 20, "y": 0}]})
            config["lane_mappings"].append({"lane_id": "south-1", "street_id": "south-street",
                                             "intersection_id": "junction-1", "approach_id": "southbound"})
            config["cameras"].append({"id": "pi-02", "intersection_id": "junction-1",
                                      "token_environment": "SECOND_CAMERA_TOKEN", "lane_ids": ["south-1"]})
            path = directory / "configuration.json"
            path.write_text(json.dumps(config))
            with (directory / "service.log").open("w+") as log:
                service, base = start_service("components/detector/Streetflow.Casper", free_port(), {
                    "Casper__ConfigurationFile": str(path), "CASPER_DEMO_CAMERA_TOKEN": "first-token",
                    "SECOND_CAMERA_TOKEN": "second-token"}, log)
                try:
                    session = request(base, "/api/v1/taco/session")[1]["id"]
                    now = time.time()
                    frame = {"schema_version": "1", "source": "camera", "time_basis": "utc",
                             "session_id": "camera-one", "sequence": 1, "network_revision": "network-1",
                             "detector_config_revision": "camera-1", "detector_id": "pi-01",
                             "intersection_id": "junction-1", "window": {"start": utc(now - 0.8), "end": utc(now - 0.5)},
                             "status": "ok", "lanes": [{"lane_id": "north-1", "approach_id": "northbound",
                             "visible_vehicle_count": 1, "crossed_vehicle_count": 0, "queued_vehicle_count": 0,
                             "mean_speed_mps": None, "measurement_status": "ok"}]}
                    self.assertEqual(request(base, "/api/v1/cameras/pi-01/observations", frame, token="first-token")[0], 200)
                    traffic = request(base, "/api/v1/taco/sessions/" + session + "/traffic")[1]
                    self.assertEqual(traffic["lanes"][0]["visible_vehicle_count"], 1)
                    self.assertIsNone(traffic["lanes"][1]["visible_vehicle_count"])
                    second = copy.deepcopy(frame)
                    second.update(session_id="camera-two", detector_id="pi-02")
                    second["lanes"][0].update(lane_id="south-1", approach_id="southbound", visible_vehicle_count=2)
                    self.assertEqual(request(base, "/api/v1/cameras/pi-02/observations", second, token="second-token")[0], 200)
                    traffic = request(base, "/api/v1/taco/sessions/" + session + "/traffic")[1]
                    self.assertEqual([lane["visible_vehicle_count"] for lane in traffic["lanes"]], [1, 2])
                    self.assertEqual(request(base, "/api/v1/cameras/pi-02/observations", second, token="first-token")[0], 401)
                finally:
                    service.terminate()
                    service.wait(timeout=5)

    def test_pi_to_casper_to_melchior_and_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            config = json.loads((ROOT / "components/detector/examples/configuration.json").read_text())
            config["cameras"][0]["view_calibration_file"] = str(ROOT / "components/detector/examples/camera.json")
            config["stale_after_seconds"] = 3
            path = directory / "configuration.json"
            path.write_text(json.dumps(config))
            processes = []
            with (directory / "services.log").open("w+") as log:
                try:
                    casper, base = start_service("components/detector/Streetflow.Casper", free_port(),
                        {"Casper__ConfigurationFile": str(path), "Casper__CalibrationDirectory": str(directory / "calibrations"),
                         "Casper__EnablePreview": "true", "CASPER_DEMO_CAMERA_TOKEN": "test-camera-token"}, log)
                    processes.append(casper)
                    session = request(base, "/api/v1/taco/session")[1]
                    route = "/api/v1/taco/sessions/" + session["id"]
                    traffic = request(base, route + "/traffic")[1]
                    self.assertEqual(traffic["status"], "unavailable")
                    self.assertIsNone(traffic["lanes"][0]["visible_vehicle_count"])
                    now = time.time()
                    frame = {
                        "schema_version": "1", "source": "camera", "time_basis": "utc",
                        "session_id": "pi-session-1", "sequence": 1, "network_revision": "network-1",
                        "detector_config_revision": "camera-1", "detector_id": "pi-01",
                        "intersection_id": "junction-1", "window": {"start": utc(now - 9), "end": utc(now - 8)},
                        "status": "ok", "lanes": [{"lane_id": "north-1", "approach_id": "northbound",
                        "visible_vehicle_count": 4, "queued_vehicle_count": 3, "crossed_vehicle_count": 1,
                        "mean_speed_mps": None, "measurement_status": "ok"}],
                    }
                    ingest = "/api/v1/cameras/pi-01/observations"
                    post = lambda f, token="test-camera-token": request(base, ingest, f, token=token)
                    self.assertEqual(post(frame, "wrong-token")[0], 401)
                    self.assertEqual(post(frame)[0], 200)
                    self.assertEqual(request(base, route + "/traffic")[1]["status"], "unavailable")
                    frame["sequence"] = 2
                    frame["window"] = {"start": utc(now - 2), "end": utc(now - 1)}
                    self.assertEqual(post(frame)[0], 200)
                    self.assertTrue(post(frame)[1]["duplicate"])
                    self.assertEqual(request(base, "/api/v1/cameras")[1][0]["measurement"]["sequence"], 2)
                    changed = copy.deepcopy(frame)
                    changed["lanes"][0]["visible_vehicle_count"] = 5
                    self.assertEqual(post(changed)[0], 409)
                    fresh = request(base, route + "/traffic")[1]
                    self.assertEqual(fresh["sequence"], 2)
                    self.assertEqual(fresh["lanes"][0]["queued_vehicle_count"], 3)
                    self.assertEqual(fresh["lanes"][0]["device_ids"], ["north-light"])
                    self.assertEqual(fresh["lanes"][0]["street_id"], "north-street")
                    self.assertEqual(post({**frame, "network_revision": "old"})[0], 409)
                    for mutate in [
                        lambda f: f.update(source="simulation"),
                        lambda f: f.update(lanes=[None]),
                        lambda f: f.update(window=None),
                        lambda f: f["lanes"][0].update(queued_vehicle_count=5),
                        lambda f: f.update(window={"start": utc(now + 50), "end": utc(now + 51)}),
                        lambda f: f["lanes"][0].update(lane_id="unregistered"),
                    ]:
                        bad = copy.deepcopy(frame)
                        mutate(bad)
                        self.assertEqual(post(bad)[0], 400)
                    self.assertEqual(request(base, "/api/v1/taco/sessions/old/traffic")[0], 409)
                    self.assertEqual(request(base, route + "/devices/north-light", {}, "PUT")[0], 501)
                    self.assertEqual(request(base, route + "/devices")[1][0]["state"], "unknown")
                    # Actual Melchior HTTP client, not a mocked proxy.
                    logistics, logistics_base = start_service("components/streetflow/Streetflow.Logistics", free_port(),
                        {"Logistics__TacoApiBaseUrl": base, "Logistics__ConnectionFile": str(directory / "connection.json")}, log)
                    processes.append(logistics)
                    through_melchior = request(logistics_base, "/api/v1/sessions/" + session["id"] + "/traffic")
                    self.assertEqual(through_melchior[0], 200)
                    self.assertEqual(through_melchior[1]["lanes"][0]["queued_vehicle_count"], 3)
                    frontend, frontend_base = start_service("components/frontend/Streetflow.Frontend", free_port(),
                        {"Frontend__CasperBaseUrl": base, "Frontend__LogisticsBaseUrl": logistics_base}, log)
                    processes.append(frontend)
                    self.assertEqual(request(frontend_base, "/casper-api/sessions/" + session["id"] + "/traffic")[1]["lanes"][0]["queued_vehicle_count"], 3)
                    self.assertEqual(request(frontend_base, "/melchior-api/sessions/" + session["id"] + "/traffic")[1]["lanes"][0]["queued_vehicle_count"], 3)
                    calibration_path = "/casper-camera-api/pi-01/calibration"
                    self.assertEqual(request(frontend_base, calibration_path)[0], 401)
                    initial = request(frontend_base, calibration_path, token="test-camera-token")
                    self.assertEqual(initial[0], 200)
                    self.assertEqual(initial[1]["lanes"][0]["lane_id"], "north-1")
                    jpeg = b"\xff\xd8preview\xff\xd9"
                    preview_request = urllib.request.Request(base + "/api/v1/cameras/pi-01/preview", jpeg,
                        {"Authorization": "Bearer test-camera-token", "Content-Type": "image/jpeg",
                         "X-Observed-At": utc(time.time()), "X-Calibration-Revision": "camera-1",
                         "X-Measurement-Status": "partial"}, method="POST")
                    with urllib.request.urlopen(preview_request) as response:
                        self.assertEqual(response.status, 200)
                    with urllib.request.urlopen(frontend_base + "/casper-camera-api/pi-01/preview") as response:
                        self.assertEqual(response.read(), jpeg)
                    self.assertEqual(request(base, "/api/v1/cameras")[1][0]["preview_status"], "partial")
                    with urllib.request.urlopen(frontend_base + "/casper/") as page:
                        content = page.read()
                        self.assertIn(b'aria-label="Map of traffic lights and cameras"', content)
                        self.assertIn(b'id="camera-view"', content)
                    # Outage is explicit null, even before the staleness deadline.
                    frame["sequence"] = 3
                    frame["window"] = {"start": utc(now - 1), "end": utc(now)}
                    frame["status"] = "unavailable"
                    frame["lanes"][0].update(visible_vehicle_count=None, queued_vehicle_count=None,
                        crossed_vehicle_count=None, measurement_status="unavailable")
                    self.assertEqual(post(frame)[0], 200)
                    self.assertIsNone(request(base, route + "/traffic")[1]["lanes"][0]["queued_vehicle_count"])
                    # New producer session, then retired session cannot replace it.
                    newer = copy.deepcopy(frame)
                    newer.update(session_id="pi-session-2", sequence=1)
                    newer["window"] = {"start": utc(now), "end": utc(now + 0.1)}
                    self.assertEqual(post(newer)[0], 200)
                    frame["sequence"] = 4
                    frame["window"] = {"start": utc(now + 0.1), "end": utc(now + 0.2)}
                    self.assertEqual(post(frame)[0], 409)
                    # Run the actual Pi demo worker through the same authenticated ingestion path.
                    worker = subprocess.Popen([
                        sys.executable, str(ROOT / "components/detector/camera/worker.py"),
                        "--config", str(ROOT / "components/detector/examples/camera.json"),
                        "--casper", base, "--demo"],
                        env={**os.environ, "CASPER_DEMO_CAMERA_TOKEN": "test-camera-token"}, stdout=log, stderr=log)
                    processes.append(worker)
                    for _ in range(70):
                        sample = request(base, route + "/traffic")[1]
                        lane = sample["lanes"][0]
                        if lane["measurement_method"] == "synthetic_demo" and lane["queued_vehicle_count"] == 2:
                            break
                        if worker.poll() is not None:
                            self.fail("Pi demo worker exited")
                        time.sleep(0.1)
                    else:
                        self.fail("Pi demo did not deliver a queued measurement")
                    simulator, simulator_base = start_service("components/simulator/Streetflow.Simulator", free_port(),
                        {"Simulator__DataRoot": str(directory / "simulator"), "Simulator__ConnectionString": ""}, log)
                    processes.append(simulator)
                    self.assertIsNone(request(simulator_base, "/api/v1/taco/session")[1]["id"])
                    self.assertTrue(request(simulator_base, "/api/v1/taco/capabilities")[1]["signal_control"])
                    revised = copy.deepcopy(initial[1])
                    revised["detector_config_revision"] = "camera-2"
                    revised["lanes"][0]["polygon"][0] = [0.1, 0.1]
                    self.assertEqual(request(frontend_base, calibration_path, revised, "PUT", "test-camera-token")[0], 200)
                    self.assertEqual(request(base, "/api/v1/cameras/pi-01/observations", frame, token="test-camera-token")[0], 409)
                    self.assertEqual(request(base, "/api/v1/cameras")[1][0]["calibration_revision"], "camera-2")
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


if __name__ == "__main__":
    unittest.main()
