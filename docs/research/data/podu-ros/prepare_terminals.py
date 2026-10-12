"""Prepare lane terminals from retained exterior road ends; no runtime dependency."""

import gzip
import json
from pathlib import Path
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[4]
PROJECT = ROOT / "experiments/drafts/podu-ros"
WEST, SOUTH, EAST, NORTH = 27.5835, 47.1480, 27.5915, 47.1535
ENTRY_POSITION_METRES = 6.0


def permits_passenger(lane):
    allowed = lane.get("allow")
    disallowed = lane.get("disallow", "").split()
    if "all" in disallowed or "passenger" in disallowed:
        return False
    return allowed is None or "all" in allowed.split() or "passenger" in allowed.split()


def main():
    network = ET.parse(PROJECT / "network.net.xml").getroot()
    with gzip.open(Path(__file__).parent / "source.osm.gz") as source_file:
        source = ET.parse(source_file).getroot()
    source_nodes = {node.get("id"): node for node in source.findall("node")}
    edges = [edge for edge in network.findall("edge") if edge.get("function") in (None, "normal")]
    terminals = []
    for junction in network.findall("junction"):
        node_id = junction.get("id")
        incoming = [edge for edge in edges if edge.get("to") == node_id]
        outgoing = [edge for edge in edges if edge.get("from") == node_id]
        neighbours = {edge.get("from") for edge in incoming} | {edge.get("to") for edge in outgoing}
        source_node = source_nodes.get(node_id)
        if len(neighbours) != 1 or source_node is None:
            continue
        longitude = float(source_node.get("lon"))
        latitude = float(source_node.get("lat"))
        if WEST < longitude < EAST and SOUTH < latitude < NORTH:
            continue
        for role, role_edges in (("entry", outgoing), ("exit", incoming)):
            for edge in role_edges:
                road_name = edge.get("name", "Local road")
                for lane in edge.findall("lane"):
                    if not permits_passenger(lane):
                        continue
                    length = float(lane.get("length"))
                    if role == "entry" and length < ENTRY_POSITION_METRES:
                        raise ValueError(f"Exterior entry lane {lane.get('id')} is too short for insertion")
                    terminals.append({
                        "id": f"{role}:{lane.get('id')}",
                        "lane_id": lane.get("id"),
                        "role": role,
                        "position_m": ENTRY_POSITION_METRES if role == "entry" else length,
                        "label": f"{road_name} · lane {int(lane.get('index')) + 1}",
                    })
    (PROJECT / "terminals.json").write_text(json.dumps(terminals, ensure_ascii=False, indent=2) + "\n")
    entries = sum(terminal["role"] == "entry" for terminal in terminals)
    print(f"Prepared {entries} entry and {len(terminals) - entries} exit lane terminals")


if __name__ == "__main__":
    main()
