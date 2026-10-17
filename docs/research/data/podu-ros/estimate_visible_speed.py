"""Exploratory evening speed estimate from the authors' selected car tracks.

Usage: python estimate_visible_speed.py RAW.parquet IDS.txt CALIBRATION.json NETWORK.net.xml OUTPUT.json
Requires the dependencies and raw release documented in README.md. This is not a measured
mean approach speed or a calibration target for the morning V3 scenario.
"""

import hashlib
import json
import sys
from pathlib import Path
from xml.etree import ElementTree

import cameratransform as ct
import numpy as np
import pandas as pd
import pyarrow.parquet as pq
from pyproj import Transformer


MAXIMUM_PLAUSIBLE_SPEED_MPS = 30
SAMPLE_EVERY_DETECTIONS = 25


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main(raw_path, ids_path, calibration_path, network_path, output_path):
    ids = {int(value) for value in Path(ids_path).read_text().split()}
    columns = ["tracker_id", "kafka_timestamp", "left", "right", "top", "bottom"]
    frame = pq.read_table(raw_path, columns=columns).to_pandas()
    frame = frame[frame.tracker_id.isin(ids)].sort_values(["tracker_id", "kafka_timestamp"]).copy()
    frame = frame[frame.groupby("tracker_id").cumcount() % SAMPLE_EVERY_DETECTIONS == 0].copy()

    camera = ct.Camera(ct.RectilinearProjection(image=(1280, 720), focallength_px=710))
    camera.load(calibration_path)
    image_points = np.array([(frame.left + frame.right) * .5, frame.top * .1 + frame.bottom * .9]).T
    gps = camera.gpsFromImage(image_points)
    transformer = Transformer.from_crs("EPSG:4326", "EPSG:32635", always_xy=True)
    east, north = transformer.transform(gps[:, 1], gps[:, 0])
    network = ElementTree.parse(network_path).getroot()
    offset = np.array([float(value) for value in network.find("location").get("netOffset").split(",")])
    frame["x"] = east + offset[0]
    frame["y"] = north + offset[1]

    previous = frame.groupby("tracker_id")[["x", "y", "kafka_timestamp"]].shift(1)
    following = frame.groupby("tracker_id")[["x", "y", "kafka_timestamp"]].shift(-1)
    duration = (following.kafka_timestamp - previous.kafka_timestamp) / 1000
    speed = np.hypot(following.x - previous.x, following.y - previous.y) / duration
    boundary = [float(value) for value in network.find("location").get("convBoundary").split(",")]
    inside = frame.x.between(boundary[0], boundary[2]) & frame.y.between(boundary[1], boundary[3])
    valid = inside & duration.between(.5, 5) & np.isfinite(speed)
    rejected_high = int((valid & (speed > MAXIMUM_PLAUSIBLE_SPEED_MPS)).sum())
    valid &= speed <= MAXIMUM_PLAUSIBLE_SPEED_MPS
    points = frame.loc[valid, ["tracker_id"]].copy()
    points["speed_mps"] = speed[valid]
    per_track = points.groupby("tracker_id").speed_mps.mean()

    report = {
        "format_version": 1,
        "source_raw_sha256": sha256(raw_path),
        "source_selected_ids_sha256": sha256(ids_path),
        "selected_tracks": len(ids),
        "tracks_with_usable_speed_samples": int(per_track.size),
        "usable_centered_samples": int(points.shape[0]),
        "samples_rejected_above_30_mps": rejected_high,
        "sample_every_detections": SAMPLE_EVERY_DETECTIONS,
        "mean_camera_time_sample_speed_kmh": float(points.speed_mps.mean() * 3.6),
        "median_camera_time_sample_speed_kmh": float(points.speed_mps.median() * 3.6),
        "fraction_camera_time_samples_at_or_below_0_5_mps": float((points.speed_mps <= .5).mean()),
        "mean_of_per_track_mean_speeds_kmh": float(per_track.mean() * 3.6),
        "median_of_per_track_mean_speeds_kmh": float(per_track.median() * 3.6),
        "method": "Every 25th selected-car detection; authors' bottom-center image point and camera calibration; centered displacement over neighboring samples divided by processing timestamp difference; keep projected points within the SUMO map, 0.5–5 s intervals, and speeds <=30 m/s. One evening camera view, with tracking and projection uncertainty. Time-sample mean weights long visible/stopped tracks more than per-track mean.",
        "limitation": "Exploratory camera-derived estimate, not an author-reported ground-truth speed, morning speed, lane mean, full-intersection mean, or car-to-car gap. Released processing timestamps are not independently synchronized to filming time. Do not use to calibrate the morning V3 scenario without validation.",
    }
    Path(output_path).write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    if len(sys.argv) != 6:
        raise SystemExit("usage: estimate_visible_speed.py RAW.parquet IDS.txt CALIBRATION.json NETWORK.net.xml OUTPUT.json")
    main(*sys.argv[1:])
