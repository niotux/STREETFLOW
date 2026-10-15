import hashlib
from pathlib import Path

from defusedxml import ElementTree


def permits(lane, vehicle_class):
    allowed = lane.get("allow")
    denied = lane.get("disallow", "").split()
    return (allowed is None or "all" in allowed.split() or vehicle_class in allowed.split()) and "all" not in denied and vehicle_class not in denied


class Network:
    def __init__(self, path, vehicle_class="passenger"):
        self.path = Path(path)
        self.sha256 = hashlib.sha256(self.path.read_bytes()).hexdigest()
        self.vehicle_class = vehicle_class
        root = ElementTree.parse(path).getroot()
        if root.tag != "net":
            raise ValueError("Expected a SUMO <net> network")
        self.lanes = {}
        self.lane_ids = {}
        self.edges = {}
        self.internal_edges = set()
        for edge in root.findall("edge"):
            lanes = sorted(edge.findall("lane"), key=lambda lane: int(lane.get("index")))
            for lane in lanes:
                self.lanes[lane.get("id")] = (edge.get("id"), lane)
                self.lane_ids[(edge.get("id"), int(lane.get("index")))] = lane.get("id")
            ordinary = edge.get("function", "normal") == "normal" and not edge.get("id").startswith(":")
            if not ordinary:
                self.internal_edges.add(edge.get("id"))
            if ordinary and any(permits(lane, vehicle_class) for lane in lanes):
                self.edges[edge.get("id")] = {
                    "edge_id": edge.get("id"), "name": edge.get("name", ""),
                    "lanes": lanes, "next_edge_ids": [],
                }
        self.edge_ids = sorted(self.edges, key=ordinal)
        if not self.edge_ids:
            raise ValueError("Map has no usable roads")
        connections = root.findall("connection")
        self.connections = {}
        for connection in connections:
            self.connections.setdefault((connection.get("from"), int(connection.get("fromLane"))), []).append(connection)
        self.movements = {}
        for connection in connections:
            source, target = connection.get("from"), connection.get("to")
            if source not in self.edges or target not in self.edges:
                continue
            source_lane = int(connection.get("fromLane"))
            target_lane = int(connection.get("toLane"))
            if not permits(self.edges[source]["lanes"][source_lane], vehicle_class):
                continue
            if not permits(self.edges[target]["lanes"][target_lane], vehicle_class):
                continue
            if not self._internal_path(connection):
                continue
            self.movements.setdefault((source, target), set()).add((source_lane, target_lane))
        for source, target in self.movements:
            self.edges[source]["next_edge_ids"].append(target)
        for edge in self.edges.values():
            edge["next_edge_ids"].sort(key=ordinal)
        self.max_turns = max(1, max(len(edge["next_edge_ids"]) for edge in self.edges.values()))

    def _internal_path(self, connection):
        via = connection.get("via")
        if via is None:
            return True
        target = (connection.get("to"), int(connection.get("toLane")))
        pending, visited = [via], set()
        while pending:
            lane_id = pending.pop()
            if lane_id in visited:
                continue
            visited.add(lane_id)
            edge_id, lane = self.lanes[lane_id]
            if not permits(lane, self.vehicle_class):
                continue
            for following in self.connections.get((edge_id, int(lane.get("index"))), []):
                endpoint = (following.get("to"), int(following.get("toLane")))
                if endpoint == target and following.get("via") is None:
                    return True
                if endpoint != target and endpoint[0] not in self.internal_edges:
                    continue
                if following.get("via"):
                    pending.append(following.get("via"))
                elif endpoint[0] in self.internal_edges:
                    pending.append(self.lane_ids[endpoint])
        return False

    def initial_lanes(self, edge_id):
        return {int(lane.get("index")) for lane in self.edges[edge_id]["lanes"] if permits(lane, self.vehicle_class)}

    def advance_lanes(self, source, target, possible):
        # A conservative allowance matching the simulator's lane planner.
        expanded = set(possible)
        pending = list(possible)
        lanes = self.edges[source]["lanes"]
        while pending:
            index = pending.pop()
            if float(lanes[index].get("length")) < 20:
                continue
            for neighbor in (index - 1, index + 1):
                if 0 <= neighbor < len(lanes) and neighbor not in expanded and permits(lanes[neighbor], self.vehicle_class):
                    expanded.add(neighbor)
                    pending.append(neighbor)
        return {end for start, end in self.movements.get((source, target), []) if start in expanded}

    def replay(self, start_edge, turns):
        if start_edge not in self.edges:
            raise ValueError(f"Unknown or unusable start_edge: {start_edge}")
        edge_id, path = start_edge, [start_edge]
        possible = self.initial_lanes(edge_id)
        for step, turn in enumerate(turns):
            options = self.edges[edge_id]["next_edge_ids"]
            if type(turn) is not int or not 0 <= turn < len(options):
                raise ValueError(f"Step {step}: turn {turn} is invalid on road {edge_id}; legal indices: 0..{len(options) - 1}")
            following = options[turn]
            possible = self.advance_lanes(edge_id, following, possible)
            if not possible:
                raise ValueError(f"Step {step}: no continuous permitted lane path from {edge_id} to {following}")
            edge_id = following
            path.append(edge_id)
        return path

    def view(self):
        result = []
        for edge_id in self.edge_ids:
            edge = self.edges[edge_id]
            lane = next(lane for lane in edge["lanes"] if permits(lane, self.vehicle_class))
            shape = [[float(value) for value in point.split(",")[:2]] for point in lane.get("shape", "").split()]
            result.append({"edge_id": edge_id, "name": edge["name"], "shape": shape, "next_edge_ids": edge["next_edge_ids"]})
        return {"network_sha256": self.sha256, "vehicle_class": self.vehicle_class, "roads": result}


def ordinal(value):
    return value.encode("utf-16-be", errors="surrogatepass")
