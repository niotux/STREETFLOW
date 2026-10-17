"""Sample one paused Balthasar run for reproducible model-output diagnostics.

The reported speed and bumper gap are simulation snapshots, not field measurements.
"""

import argparse
import json
from pathlib import Path
from urllib.request import Request, urlopen


PROJECT_ID = "podu-ros-neural"
SEED = 42
START_TIME_SECONDS = 27000
STEP_SECONDS = 0.5
STEPS_PER_SAMPLE = 200
ASSUMED_CAR_LENGTH_M = 5.0
STOPPED_SPEED_MPS = 0.5


def request(base_url, path, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    headers = {} if data is None else {"Content-Type": "application/json"}
    with urlopen(Request(base_url + path, data=data, headers=headers), timeout=120) as response:
        return json.load(response)


def sample(snapshot):
    speeds = []
    positions = {}
    for vehicle in snapshot["vehicles"]:
        if vehicle["lane_id"].startswith(":"):
            continue
        speeds.append(vehicle["speed_mps"])
        positions.setdefault(vehicle["lane_id"], []).append(vehicle["lane_position"])
    gaps = []
    for lane_positions in positions.values():
        lane_positions.sort()
        for follower, leader in zip(lane_positions, lane_positions[1:]):
            gaps.append(max(0.0, leader - follower - ASSUMED_CAR_LENGTH_M))
    return {
        "simulation_time_seconds": snapshot["simulation_time"],
        "cars_on_ordinary_lanes": len(speeds),
        "mean_speed_kmh": 3.6 * sum(speeds) / len(speeds) if speeds else None,
        "stopped_cars": sum(speed <= STOPPED_SPEED_MPS for speed in speeds),
        "adjacent_car_pairs": len(gaps),
        "mean_bumper_gap_m": sum(gaps) / len(gaps) if gaps else None,
        "speed_sum_mps": sum(speeds),
        "gap_sum_m": sum(gaps),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--url", default="http://localhost:8080")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seconds", type=int, default=1000)
    args = parser.parse_args()
    if args.seconds <= 0 or args.seconds % (STEPS_PER_SAMPLE * STEP_SECONDS) != 0:
        raise ValueError("seconds must be a positive multiple of 100")
    base_url = args.url.rstrip("/")
    status = request(base_url, f"/api/v1/projects/{PROJECT_ID}/runs", {"seed": SEED, "start_time_seconds": START_TIME_SECONDS, "step_seconds": STEP_SECONDS, "start_paused": True})
    if status["state"] != "paused":
        raise RuntimeError(status.get("failure") or "Run did not start paused")
    run_id = status["id"]
    samples = []
    try:
        for _ in range(int(args.seconds / (STEPS_PER_SAMPLE * STEP_SECONDS))):
            status = request(base_url, f"/api/v1/city-runs/{run_id}/step", {"steps": STEPS_PER_SAMPLE})
            if status["state"] != "paused" or status.get("failure"):
                raise RuntimeError(status.get("failure") or f"Run state: {status['state']}")
            snapshot = request(base_url, f"/api/v1/city-runs/{run_id}/snapshot")
            samples.append(sample(snapshot))
    finally:
        request(base_url, f"/api/v1/city-runs/{run_id}/stop", {"steps": 1})
    cars = sum(item["cars_on_ordinary_lanes"] for item in samples)
    pairs = sum(item["adjacent_car_pairs"] for item in samples)
    report = {
        "project_id": PROJECT_ID,
        "project_revision": status["configuration"]["project_revision"],
        "run_id": run_id,
        "seed": SEED,
        "start_time_seconds": START_TIME_SECONDS,
        "step_seconds": STEP_SECONDS,
        "duration_seconds": args.seconds,
        "sumo_version": status["engine_version"],
        "inserted_vehicles": status["inserted_vehicles"],
        "arrived_vehicles": status["arrived_vehicles"],
        "gridlock_recoveries": status["gridlock_recoveries"],
        "pending_entries": status["pending_entries"],
        "backlog_seconds": status["backlog_seconds"],
        "mean_sampled_speed_kmh": 3.6 * sum(item["speed_sum_mps"] for item in samples) / cars if cars else None,
        "mean_sampled_bumper_gap_m": sum(item["gap_sum_m"] for item in samples) / pairs if pairs else None,
        "samples": [{key: value for key, value in item.items() if not key.endswith("_sum_mps") and key != "gap_sum_m"} for item in samples],
        "method": "Every 100 simulation seconds, ordinary-lane car snapshot speeds and same-lane adjacent front-position differences minus assumed 5 m car length; simulation output, not measured street speed or spacing. Pedestrians are not in this API.",
    }
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps({key: value for key, value in report.items() if key != "samples"}, indent=2))


if __name__ == "__main__":
    main()
