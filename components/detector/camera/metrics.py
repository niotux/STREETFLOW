"""Camera-local tracking and measurements. No video, image, or track IDs leave this module."""
from dataclasses import dataclass, field
import math


def inside(point, polygon):
    x, y = point
    result = False
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        if (a[1] > y) != (b[1] > y):
            if x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]:
                result = not result
    return result


def iou(a, b):
    intersection = max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(0, min(a[3], b[3]) - max(a[1], b[1]))
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - intersection
    return intersection / union if union > 0 else 0


def point(box):
    # Bottom centre is the road-contact reference, not the bounding box centre.
    return ((box[0] + box[2]) / 2, box[3])


def side(p, line):
    a, b = line
    length = math.dist(a, b)
    return ((b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])) / length


def passage(a, b, line):
    """Intersect the finite counting segment, not its infinite extension."""
    p, q = line
    rx, ry = b[0] - a[0], b[1] - a[1]
    sx, sy = q[0] - p[0], q[1] - p[1]
    denominator = rx * sy - ry * sx
    if abs(denominator) < 1e-12:
        return None
    dx, dy = p[0] - a[0], p[1] - a[1]
    t = (dx * sy - dy * sx) / denominator
    u = (dx * ry - dy * rx) / denominator
    return t if 0 <= t <= 1 and 0 <= u <= 1 else None


@dataclass
class Track:
    box: tuple
    seen: float
    anchor: tuple
    stationary_since: float
    lane_sides: dict = field(default_factory=dict)


class Measurements:
    def __init__(self, config):
        self.lanes = config["lanes"]
        self.window_seconds = config.get("window_seconds", 2)
        self.stop_seconds = config.get("queue_duration_seconds", 2)
        self.stop_distance = config.get("stationary_radius", 0.008)
        self.max_gap = config.get("maximum_tracking_gap_seconds", 1.5)
        self.iou_threshold = config.get("tracking_iou_threshold", 0.2)
        self.maximum_detections = config.get("maximum_detections", 200)
        self.tracks = {}
        self.next_id = 0
        self.window_start = None
        self.previous_time = None
        self.crossings = []
        self.partial = True  # Warm-up cannot establish every passage.
        ids = [lane["lane_id"] for lane in self.lanes]
        if not ids or len(ids) != len(set(ids)):
            raise ValueError("Configure unique lane IDs")
        if not (0 < self.window_seconds <= 30 and self.stop_seconds >= 0
                and self.stop_distance > 0 and self.max_gap > 0 and 0 < self.iou_threshold <= 1
                and isinstance(self.maximum_detections, int) and 1 <= self.maximum_detections <= 500):
            raise ValueError("Invalid measurement timing or tracking thresholds")
        for lane in self.lanes:
            polygon = lane["polygon"]
            line = lane.get("counting_line")
            if len(polygon) < 3 or any(len(p) != 2 or not all(math.isfinite(v) and 0 <= v <= 1 for v in p) for p in polygon):
                raise ValueError("Lane polygons use normalized [0,1] coordinates")
            if line is not None and (len(line) != 2 or any(len(p) != 2 or not all(math.isfinite(v) and 0 <= v <= 1 for v in p) for p in line) or math.dist(*line) == 0):
                raise ValueError("Counting lines need two distinct normalized points")

    def update(self, boxes, now):
        if len(boxes) > self.maximum_detections:
            boxes = boxes[:self.maximum_detections]
            self.partial = True
        if self.previous_time is not None and now <= self.previous_time:
            raise ValueError("Frame timestamps must increase")
        if self.window_start is None:
            self.window_start = now
        if self.previous_time is not None and now - self.previous_time > self.max_gap:
            self.tracks.clear()
            self.partial = True
        self.previous_time = now
        self.tracks = {key: track for key, track in self.tracks.items() if now - track.seen <= self.max_gap}
        if len(self.tracks) > self.maximum_detections * 2:
            self.tracks = dict(sorted(self.tracks.items(), key=lambda item: item[1].seen, reverse=True)[:self.maximum_detections * 2])
            self.partial = True
        pairs = sorted(((iou(track.box, box), key, index) for key, track in self.tracks.items()
                        for index, box in enumerate(boxes)), reverse=True)
        matched_tracks, matched_boxes, matches = set(), set(), {}
        for score, key, index in pairs:
            if score >= self.iou_threshold and key not in matched_tracks and index not in matched_boxes:
                matches[index] = key
                matched_tracks.add(key)
                matched_boxes.add(index)
        current = []
        for index, box in enumerate(boxes):
            p = point(box)
            if index in matches:
                track = self.tracks[matches[index]]
                if math.dist(p, track.anchor) > self.stop_distance:
                    track.anchor, track.stationary_since = p, now
                for lane in self.lanes:
                    line = lane.get("counting_line")
                    if line is None:
                        continue
                    distance = side(p, line)
                    old = track.lane_sides.get(lane["lane_id"])
                    # A dead band avoids box jitter generating duplicate crossings.
                    if abs(distance) > 0.005:
                        if old is not None and old[0] < 0 < distance:
                            fraction = passage(old[1], p, line)
                            if fraction is not None and (inside(old[1], lane["polygon"]) or inside(p, lane["polygon"])):
                                timestamp = old[2] + fraction * (now - old[2])
                                # Preserve exact half-open boundaries after interpolation.
                                if abs(timestamp - self.window_start) < 1e-8:
                                    timestamp = self.window_start
                                if abs(timestamp - now) < 1e-8:
                                    timestamp = now
                                self.crossings.append((lane["lane_id"], timestamp))
                        track.lane_sides[lane["lane_id"]] = (distance, p, now)
                track.box, track.seen = tuple(box), now
            else:
                self.next_id += 1
                track = Track(tuple(box), now, p, now)
                for lane in self.lanes:
                    if lane.get("counting_line") is not None:
                        distance = side(p, lane["counting_line"])
                        if abs(distance) > 0.005:
                            track.lane_sides[lane["lane_id"]] = (distance, p, now)
                self.tracks[self.next_id] = track
            current.append(track)
        if now - self.window_start < self.window_seconds:
            return None
        # A completed variable-length window ends at this actual sampled frame.
        # Events exactly at end remain queued for the following half-open window.
        result = []
        for lane in self.lanes:
            visible = [track for track in current if inside(point(track.box), lane["polygon"])]
            result.append({
                "approach_id": lane["approach_id"], "lane_id": lane["lane_id"],
                "visible_vehicle_count": len(visible),
                "crossed_vehicle_count": sum(name == lane["lane_id"] and self.window_start <= t < now
                                             for name, t in self.crossings) if lane.get("counting_line") else None,
                "queued_vehicle_count": sum(now - track.stationary_since >= self.stop_seconds for track in visible),
                "mean_speed_mps": None,
                "measurement_status": "partial" if self.partial else "ok",
            })
        start = self.window_start
        self.window_start = now
        self.crossings = [(name, t) for name, t in self.crossings if t >= now]
        status = "partial" if self.partial else "ok"
        self.partial = False
        return start, now, status, result

    def unavailable(self, now):
        start = self.window_start if self.window_start is not None else now - self.window_seconds
        # A long outage must still respect Casper's maximum accepted window.
        start = max(start, now - self.window_seconds)
        self.window_start = now
        self.tracks.clear()
        self.crossings.clear()
        self.previous_time = None
        self.partial = True
        return start, now, "unavailable", [{
            "approach_id": lane["approach_id"], "lane_id": lane["lane_id"],
            "visible_vehicle_count": None, "crossed_vehicle_count": None,
            "queued_vehicle_count": None, "mean_speed_mps": None,
            "measurement_status": "unavailable",
        } for lane in self.lanes]
