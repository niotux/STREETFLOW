#!/usr/bin/env python3
"""Rebuild this synthetic Transformer Routes package from transparent assumptions."""

import json
import math
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parent
SIDES = ["south", "north", "east", "west"]
PERIODS = [(0, 25200), (25200, 32400), (32400, 57600), (57600, 68400), (68400, 86400)]
RATES = [
    [30, 30, 20, 20],
    [180, 60, 80, 80],
    [80, 80, 70, 70],
    [60, 180, 80, 80],
    [40, 40, 30, 30],
]

# Destination weights in SIDES order. Same-side outcomes are infeasible on this
# network, so their finite stored placeholder is masked by network connectivity.
# Every reachable row sums to 1. These are illustrative assumptions, not counts.
SHARES = [
    [[0, .60, .20, .20], [.60, 0, .20, .20], [.25, .25, 0, .50], [.25, .25, .50, 0]],
    [[0, .80, .10, .10], [.50, 0, .25, .25], [.15, .65, 0, .20], [.15, .65, .20, 0]],
    [[0, .60, .20, .20], [.60, 0, .20, .20], [.25, .25, 0, .50], [.25, .25, .50, 0]],
    [[0, .50, .25, .25], [.80, 0, .10, .10], [.65, .15, 0, .20], [.65, .15, .20, 0]],
    [[0, .60, .20, .20], [.60, 0, .20, .20], [.25, .25, 0, .50], [.25, .25, .50, 0]],
]


def write_json(name, value):
    (ROOT / name).write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")


def tensor(axes, shape, values):
    return {"dtype": "float64", "axis_order": axes, "shape": shape, "values": values}


def main():
    source_network = ROOT.parent / "tiny-graph" / "network.net.xml"
    shutil.copyfile(source_network, ROOT / "network.net.xml")
    network = ET.parse(source_network).getroot()
    connections = set()
    for connection in network.findall("connection"):
        incoming = connection.attrib["from"]
        outgoing = connection.attrib["to"]
        if not incoming.startswith(":") and not outgoing.startswith(":"):
            connections.add((incoming, outgoing))
    movements = sorted(connections)
    assert len(movements) == 12

    write_json("experiment.json", {
        "format_version": 2,
        "id": "four-way-transformer-routes-devices",
        "name": "Four-way intersection — Transformer Routes",
        "description": "Synthetic morning/evening directionality fixture with four entries, four exits, and dense probabilistic model weights.",
        "network": {"file": "network.net.xml", "attribution": "Synthetic STREETFLOW four-way intersection"},
        "traffic_lights_file": "traffic-lights.json",
        "terminals_file": "terminals.json",
        "journey_model_file": "journey-model.json",
        "defaults": {"start_time_seconds": 27000, "step_seconds": 0.5},
    })
    terminals = []
    for role, suffix, position in [("entry", "in", 6), ("exit", "out", 186)]:
        for side in SIDES:
            terminals.append({"id": f"{side}-{role}", "lane_id": f"{side}_{suffix}_0", "role": role, "position_m": position, "label": f"{side.title()} {role}"})
    write_json("terminals.json", terminals)
    write_json("journey-model.json", {
        "format_version": 2,
        "architecture": "contextual_journey_v1",
        "arrival_model": "nonhomogeneous_poisson",
        "repeat_daily": True,
        "route_time": "planned_departure",
        "weights_file": "weights.json",
        "axes": {
            "period": [{"start_seconds": start, "end_seconds": end} for start, end in PERIODS],
            "origin": [f"{side}-entry" for side in SIDES],
            "destination": [{"id": side, "terminal_ids": [f"{side}-exit"], "label": f"{side.title()} exit"} for side in SIDES],
            "category": ["default"],
            "vehicle_type": [{"id": "car", "vehicle_class": "passenger", "label": "Passenger car"}],
            "movement": [{"incoming_edge_id": incoming, "outgoing_edge_id": outgoing} for incoming, outgoing in movements],
        },
        "evidence": [{
            "tensor_names": ["arrival_rates", "journey_logits", "movement_logits"],
            "kind": "authored_assumption",
            "note": "All rates, clock windows, destination shares, and movement scores are synthetic test assumptions. Morning south-to-north and evening north-to-south imitate the requested commute direction qualitatively; they are not measured Podu Ros traffic or trained transformer parameters.",
        }],
    })
    write_json("traffic-lights.json", {'format_version': 1, 'world_id': 'four-way', 'description': 'Synthetic physical-device mapping: one shared vehicle signal device per approach. Not a surveyed real-world inventory.', 'devices': [{'id': 'north', 'label': 'North approach', 'x': 198.4, 'y': 207.2, 'lane_ids': ['north_in_0'], 'signal_id': 'center', 'link_indices': [0, 1, 2]}, {'id': 'east', 'label': 'East approach', 'x': 207.2, 'y': 201.6, 'lane_ids': ['east_in_0'], 'signal_id': 'center', 'link_indices': [3, 4, 5]}, {'id': 'south', 'label': 'South approach', 'x': 201.6, 'y': 192.8, 'lane_ids': ['south_in_0'], 'signal_id': 'center', 'link_indices': [6, 7, 8]}, {'id': 'west', 'label': 'West approach', 'x': 192.8, 'y': 198.4, 'lane_ids': ['west_in_0'], 'signal_id': 'center', 'link_indices': [9, 10, 11]}]})
    journey_logits = []
    movement_logits = []
    for period in range(len(PERIODS)):
        for origin in range(len(SIDES)):
            shares = SHARES[period][origin]
            assert math.isclose(sum(shares), 1)
            assert shares[origin] == 0
            for destination in range(len(SIDES)):
                probability = shares[destination]
                journey_logits.append(round(math.log(probability), 12) if probability else 0)
                for incoming, outgoing in movements:
                    # Base preference for this context's destination at each
                    # approach. Destination conditioning subsequently removes
                    # unsuccessful exits; no routes or vehicles are serialized.
                    movement_logits.append(1.5 if outgoing == f"{SIDES[destination]}_out" else 0)
    write_json("weights.json", {"format_version": 2, "tensors": {
        "arrival_rates": tensor(["period", "origin"], [5, 4], [rate for row in RATES for rate in row]),
        "journey_logits": tensor(["period", "origin", "destination", "category", "vehicle_type"], [5, 4, 4, 1, 1], journey_logits),
        "movement_logits": tensor(["period", "origin", "destination", "category", "vehicle_type", "movement"], [5, 4, 4, 1, 1, 12], movement_logits),
    }})
    print("Prepared four-way-transformer-routes: 20 rates, 80 journey logits, 960 movement logits.")


if __name__ == "__main__":
    main()
