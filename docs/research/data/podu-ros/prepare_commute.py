"""Prepare an explicitly estimated commute variant; preserve the camera-only project."""

import csv
import json
import math
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[4]
SOURCE = ROOT / "experiments/drafts/podu-ros"
DESTINATION = ROOT / "experiments/drafts/podu-ros-commute"
CAMERA_START = 68940
CAMERA_END = 78938.454
NORTHBOUND_PEAK = 2.4
SOUTHBOUND_PEAK = 0.35


def current_weight(intervals, time):
    for interval in intervals:
        if interval["start_seconds"] <= time < interval["end_seconds"]:
            return interval["weight"]
    raise ValueError(f"No interval at {time}")


def commute_factor(time, heading):
    hour = time / 3600
    # User-observed direction; these hours and strengths are preparation estimates.
    north, south = 1.0, 1.0
    if 7 <= hour < 9:
        north, south = NORTHBOUND_PEAK, SOUTHBOUND_PEAK
    elif 6 <= hour < 7 or 9 <= hour < 10:
        north, south = 1.4, 0.7
    elif 16 <= hour < 19:
        north, south = SOUTHBOUND_PEAK, NORTHBOUND_PEAK
    elif 15 <= hour < 16 or 19 <= hour < 20:
        north, south = 0.7, 1.4
    preference = north if heading >= 0 else south
    return 1 + abs(heading) * (preference - 1)


def headings():
    result = {}
    for edge in ET.parse(SOURCE / "network.net.xml").getroot().findall("edge"):
        if edge.get("function") not in (None, "normal"):
            continue
        lane = edge.find("lane")
        points = [tuple(map(float, point.split(","))) for point in lane.get("shape").split()]
        length = sum(math.dist(start, end) for start, end in zip(points, points[1:]))
        result[edge.get("id")] = (points[-1][1] - points[0][1]) / length if length else 0
    return result


def main():
    DESTINATION.mkdir(parents=True, exist_ok=True)
    for name in ("network.net.xml", "terminals.json", "detectors.json"):
        shutil.copyfile(SOURCE / name, DESTINATION / name)
    manifest = json.loads((SOURCE / "experiment.json").read_text())
    manifest["id"] = "podu-ros-commute"
    manifest["name"] = "Podu Roș · commute estimate"
    manifest["description"] = "Estimated commute: toward Palas 07:00–09:00, toward southern approaches 16:00–19:00. Direction and peak strengths are assumptions; camera trajectories and the citywide daily curve are not measured road densities."
    manifest["defaults"]["start_time_seconds"] = 27000
    (DESTINATION / "experiment.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    demand = json.loads((SOURCE / "demand.json").read_text())
    with (ROOT / "docs/research/data/iasi-pmud-2023/profiles.csv").open() as file:
        daily = [{"start_seconds": float(row["start_seconds"]), "end_seconds": float(row["end_seconds"]), "weight": float(row["relative_weight"])} for row in csv.DictReader(file)]
    direction = headings()
    boundaries = {0.0, 86400.0}
    for interval in daily:
        boundaries.add(interval["start_seconds"])
        boundaries.add(interval["end_seconds"])
    for profile in demand["profiles"]:
        for interval in profile["intervals"]:
            boundaries.add(interval["start_seconds"])
            boundaries.add(interval["end_seconds"])
    boundaries = sorted(boundaries)
    for profile in demand["profiles"]:
        original = profile["intervals"]
        baseline = original[0]["weight"]
        intervals = []
        for start, end in zip(boundaries, boundaries[1:]):
            if CAMERA_START <= start < CAMERA_END:
                weight = current_weight(original, start)
            else:
                weight = baseline * current_weight(daily, start)
            weight *= commute_factor(start, direction[profile["edge"]])
            intervals.append({"start_seconds": start, "end_seconds": end, "weight": round(weight, 6)})
        profile["intervals"] = intervals
    (DESTINATION / "demand.json").write_text(json.dumps(demand, ensure_ascii=False, indent=2) + "\n")
    print(f"Prepared {len(demand['profiles'])} edge profiles in {DESTINATION}")


if __name__ == "__main__":
    main()
