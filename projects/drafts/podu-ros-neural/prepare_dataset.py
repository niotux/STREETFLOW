"""Build full-day synthetic demand for the bundled Podu Ros road network.

Run from the repository root with PYTHONPATH=components/route-trainer.
The positive rates are scenario assumptions, not measured traffic counts.
"""

import argparse
import json
import math
from pathlib import Path

from route_trainer.dataset import blank, validate
from route_trainer.network import Network


INTERVAL_SECONDS = 15 * 60
EXPECTED_NETWORK_SHA256 = "98e5fded7160e53332f7851966ee1c1aca0627e6dcf730e05287eabc275ef437"
MORNING_PEAK_HOUR = 8.0
MORNING_SIGMA_HOURS = 1.25
EVENING_PEAK_HOUR = 18.0
EVENING_SIGMA_HOURS = 1.5
DAYTIME_PEAK_HOUR = 13.0
DAYTIME_SIGMA_HOURS = 4.5

# Preserve the four existing complete paths and their local turn indices.
# Rates are (overnight floor, daytime amplitude, morning amplitude, evening
# amplitude), in vehicles/hour. The reverse route has the larger evening peak.
ROUTES = (
    ("nicolina-palat", "265855329", (0, 2, 0, 1, 0, 1, 1, 0, 0, 1, 0),
     (12.0, 48.0, 540.0, 270.0)),
    ("palat-nicolina", "-1068015153#1", (0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 3, 0),
     (1.2, 4.8, 54.0, 594.0)),
    ("socola-palat", "290635797#3", (1, 0, 0, 1, 1, 0, 0, 1, 0),
     (2.4, 9.6, 108.0, 54.0)),
    ("cantemir-palat", "303755190#2", (0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 1, 0, 0, 1, 0),
     (1.6, 6.4, 72.0, 36.0)),
)


def gaussian_average(start_hour, end_hour, peak_hour, sigma_hours):
    """Integrate a daily periodic Gaussian over one constant-rate interval.

    Adjacent-day copies keep the curve continuous across midnight. Copies two
    days away are included so truncation is negligible for these widths.
    """
    scale = sigma_hours * math.sqrt(2.0)
    integral = 0.0
    for day in range(-2, 3):
        center = peak_hour + day * 24.0
        integral += sigma_hours * math.sqrt(math.pi / 2.0) * (
            math.erf((end_hour - center) / scale)
            - math.erf((start_hour - center) / scale)
        )
    return integral / (end_hour - start_hour)


def build_dataset(network):
    if network.sha256 != EXPECTED_NETWORK_SHA256:
        raise ValueError("Network changed; review the stored paths and update EXPECTED_NETWORK_SHA256 first")
    dataset = blank(network)
    for start in range(0, 86400, INTERVAL_SECONDS):
        end = start + INTERVAL_SECONDS
        interval = (start / 3600.0, end / 3600.0)
        daytime = gaussian_average(*interval, DAYTIME_PEAK_HOUR, DAYTIME_SIGMA_HOURS)
        morning = gaussian_average(*interval, MORNING_PEAK_HOUR, MORNING_SIGMA_HOURS)
        evening = gaussian_average(*interval, EVENING_PEAK_HOUR, EVENING_SIGMA_HOURS)
        for identifier, origin, turns, amplitudes in ROUTES:
            floor, day_rate, morning_rate, evening_rate = amplitudes
            rate = floor + day_rate * daytime + morning_rate * morning + evening_rate * evening
            dataset["routes"].append({
                "id": f"{identifier}-{start // 3600:02d}{start % 3600 // 60:02d}",
                "start_seconds": start,
                "end_seconds": end,
                "start_edge": origin,
                "turns": list(turns),
                "vehicles_per_hour": round(rate, 6),
            })
    validate(dataset, network)
    return dataset


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("network", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    dataset = build_dataset(Network(args.network))
    args.output.write_text(json.dumps(dataset, indent=2) + "\n")
    daily_vehicles = sum(row["vehicles_per_hour"] * INTERVAL_SECONDS / 3600 for row in dataset["routes"])
    print(f"Wrote {len(dataset['routes'])} route records over 96 daily intervals; "
          f"expected demand {daily_vehicles:.2f} vehicles/day.")


if __name__ == "__main__":
    main()
