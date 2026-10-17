"""Author an explicit morning scenario against the final Podu Roș network.

Run with the trainer environment: python prepare_v3_dataset.py NETWORK OUTPUT_JSON
The rates are scenario assumptions, not measured Podu Roș turning counts.
"""

import heapq
import json
import sys
from pathlib import Path

from defusedxml import ElementTree

from route_trainer.dataset import blank, validate
from route_trainer.network import Network


MORNING_START = 7 * 3600
MORNING_END = 9 * 3600
ROUTES = (
    ("morning-nicolina-palat", "265855329", "1300222779#0", 600),
    ("morning-palat-nicolina", "-1068015153#1", "256585921", 60),
    ("morning-socola-palat", "290635797#3", "1300222779#0", 120),
    ("morning-cantemir-palat", "303755190#2", "1300222779#0", 80),
)


def u_turn_only_pairs(network_path):
    root = ElementTree.parse(network_path).getroot()
    directions = {}
    for connection in root.findall("connection"):
        source, target = connection.get("from"), connection.get("to")
        if source is None or target is None or source.startswith(":") or target.startswith(":"):
            continue
        directions.setdefault((source, target), set()).add(connection.get("dir"))
    return {pair for pair, values in directions.items() if values == {"t"}}


def shortest_legal_path(network, origin, destination, forbidden):
    if origin not in network.edges or destination not in network.edges:
        raise ValueError(f"Unknown origin/destination: {origin} -> {destination}")
    start_lanes = frozenset(network.initial_lanes(origin))
    queue = [(0.0, (origin,), start_lanes)]
    best = {(origin, start_lanes): 0.0}
    while queue:
        cost, path, lanes = heapq.heappop(queue)
        edge = path[-1]
        if cost > best.get((edge, lanes), float("inf")):
            continue
        if edge == destination:
            return path
        for following in network.edges[edge]["next_edge_ids"]:
            if following in path or (edge, following) in forbidden:
                continue
            next_lanes = frozenset(network.advance_lanes(edge, following, lanes))
            if not next_lanes:
                continue
            next_length = min(float(lane.get("length")) / float(lane.get("speed")) for lane in network.edges[following]["lanes"] if int(lane.get("index")) in next_lanes)
            next_cost = cost + next_length
            key = (following, next_lanes)
            if next_cost < best.get(key, float("inf")):
                best[key] = next_cost
                heapq.heappush(queue, (next_cost, path + (following,), next_lanes))
    raise ValueError(f"No legal continuous-lane route: {origin} -> {destination}")


def main(network_path, output_path):
    network = Network(network_path)
    forbidden = u_turn_only_pairs(network_path)
    dataset = blank(network)
    for identifier, origin, destination, rate in ROUTES:
        path = shortest_legal_path(network, origin, destination, forbidden)
        turns = [network.edges[source]["next_edge_ids"].index(target) for source, target in zip(path, path[1:])]
        dataset["routes"].append({
            "id": identifier,
            "start_seconds": MORNING_START,
            "end_seconds": MORNING_END,
            "start_edge": origin,
            "turns": turns,
            "vehicles_per_hour": rate,
        })
        print(f"{identifier}: {rate}/hour, {len(path)} roads: {' > '.join(path)}")
    validate(dataset, network)
    Path(output_path).write_text(json.dumps(dataset, indent=2) + "\n")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: prepare_v3_dataset.py NETWORK OUTPUT_JSON")
    main(sys.argv[1], sys.argv[2])
