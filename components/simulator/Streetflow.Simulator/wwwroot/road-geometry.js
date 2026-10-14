const connectionToleranceMeters = 0.75;
const maximumRouteVisits = 512;
const maximumCachedRoutes = 2048;
const laneChangeSamples = 12;
const positionToleranceMeters = 0.05;
const headingLookAheadMeters = 1.5;

function clamp(value, minimum, maximum) { return Math.max(minimum, Math.min(maximum, value)); }
function heading(dx, dy) { return (Math.atan2(dx, dy) * 180 / Math.PI + 360) % 360; }
function pointOnLane(lane, distance) {
  distance = clamp(distance, 0, lane.distance);
  let low = 1, high = lane.points.length - 1;
  while (low < high) {
    const middle = Math.floor((low + high) / 2);
    if (lane.points[middle].distance < distance) low = middle + 1;
    else high = middle;
  }
  const end = lane.points[low], start = lane.points[low - 1];
  const amount = (distance - start.distance) / Math.max(positionToleranceMeters, end.distance - start.distance);
  return {x: start.x + (end.x - start.x) * amount, y: start.y + (end.y - start.y) * amount, angle: heading(end.x - start.x, end.y - start.y)};
}
function projection(lane, car) {
  let best = null, error = Infinity;
  const expected = Number.isFinite(car.lane_position) && lane.length > 0 ? car.lane_position / lane.length * lane.distance : null;
  for (let index = 1; index < lane.points.length; index++) {
    const start = lane.points[index - 1], end = lane.points[index];
    const dx = end.x - start.x, dy = end.y - start.y;
    const lengthSquared = dx * dx + dy * dy;
    const amount = clamp(((car.x - start.x) * dx + (car.y - start.y) * dy) / lengthSquared, 0, 1);
    const x = start.x + dx * amount, y = start.y + dy * amount;
    const distance = start.distance + (end.distance - start.distance) * amount;
    const squaredError = (car.x - x) ** 2 + (car.y - y) ** 2;
    // Reported lane position disambiguates a lane that bends back beside itself.
    const score = squaredError + (expected === null ? 0 : ((distance - expected) * 0.01) ** 2);
    if (score < error) { error = score; best = {x, y, distance, offsetX: car.x - x, offsetY: car.y - y}; }
  }
  return best;
}
function appendPoint(points, point) {
  const previous = points[points.length - 1];
  if (previous && Math.hypot(previous.x - point.x, previous.y - point.y) < 0.0001) return;
  points.push({x: point.x, y: point.y});
}
function appendLane(points, lane, start, end) {
  appendPoint(points, pointOnLane(lane, start));
  for (const point of lane.points) {
    if (point.distance > start && point.distance < end) appendPoint(points, point);
  }
  appendPoint(points, pointOnLane(lane, end));
}
function appendLaneChange(points, from, to, startFraction, endFraction) {
  for (let index = 0; index <= laneChangeSamples; index++) {
    const amount = index / laneChangeSamples;
    const fraction = startFraction + (endFraction - startFraction) * amount;
    const start = pointOnLane(from, fraction * from.distance);
    const end = pointOnLane(to, fraction * to.distance);
    const lateral = amount * amount * (3 - 2 * amount);
    appendPoint(points, {x: start.x + (end.x - start.x) * lateral, y: start.y + (end.y - start.y) * lateral});
  }
}
function makePath(points, start, end, speedLimit) {
  let distance = 0;
  for (let index = 0; index < points.length; index++) {
    if (index) distance += Math.hypot(points[index].x - points[index - 1].x, points[index].y - points[index - 1].y);
    points[index].distance = distance;
  }
  return {points, distance, startOffset: {x: start.offsetX, y: start.offsetY}, endOffset: {x: end.offsetX, y: end.offsetY}, speedLimit};
}

export function sampleRoadPath(path, amount, pose) {
  if (path.distance <= positionToleranceMeters) {
    pose.x = path.points[0].x + path.startOffset.x * (1 - amount) + path.endOffset.x * amount;
    pose.y = path.points[0].y + path.startOffset.y * (1 - amount) + path.endOffset.y * amount;
    return;
  }
  const point = pointOnLane(path, amount * path.distance);
  pose.x = point.x + path.startOffset.x * (1 - amount) + path.endOffset.x * amount;
  pose.y = point.y + path.startOffset.y * (1 - amount) + path.endOffset.y * amount;
  const before = pointOnLane(path, amount * path.distance - headingLookAheadMeters);
  const after = pointOnLane(path, amount * path.distance + headingLookAheadMeters);
  pose.angle = heading(after.x - before.x, after.y - before.y);
}

export class RoadGeometry {
  constructor(network) {
    this.lanes = new Map();
    this.edges = new Map();
    this.routes = new Map();
    const starts = new Map();
    for (const source of network?.lanes || []) {
      const points = [];
      for (const point of source.shape || []) appendPoint(points, point);
      if (points.length < 2) continue;
      let distance = 0;
      for (let index = 0; index < points.length; index++) {
        if (index) distance += Math.hypot(points[index].x - points[index - 1].x, points[index].y - points[index - 1].y);
        points[index].distance = distance;
      }
      if (distance <= positionToleranceMeters) continue;
      const lane = {id: source.id, edgeId: source.edge_id || source.id, length: source.length || distance, speed: source.speed || 0, points, distance, next: []};
      this.lanes.set(lane.id, lane);
      if (!this.edges.has(lane.edgeId)) this.edges.set(lane.edgeId, []);
      this.edges.get(lane.edgeId).push(lane);
      const first = points[0];
      const key = `${Math.floor(first.x)},${Math.floor(first.y)}`;
      if (!starts.has(key)) starts.set(key, []);
      starts.get(key).push(lane);
    }
    // Internal SUMO lanes supply the curved paths through junctions. Only join
    // directed lane endpoints that physically meet, never the nearest road.
    for (const lane of this.lanes.values()) {
      const last = lane.points[lane.points.length - 1];
      const x = Math.floor(last.x), y = Math.floor(last.y);
      for (let dx = -1; dx <= 1; dx++) {
        for (let dy = -1; dy <= 1; dy++) {
          for (const next of starts.get(`${x + dx},${y + dy}`) || []) {
            const first = next.points[0];
            if (Math.hypot(last.x - first.x, last.y - first.y) <= connectionToleranceMeters) lane.next.push(next);
          }
        }
      }
    }
  }

  route(from, to) {
    const key = `${from.id}\n${to.id}`;
    if (this.routes.has(key)) return this.routes.get(key);
    const pending = [];
    const visited = new Map();
    for (const next of from.next) pending.push({lane: next, cost: 0, previous: null});
    let result = null;
    for (let count = 0; pending.length && count < maximumRouteVisits; count++) {
      let best = 0;
      for (let index = 1; index < pending.length; index++) {
        if (pending[index].cost < pending[best].cost) best = index;
      }
      const current = pending.splice(best, 1)[0];
      if (visited.has(current.lane.id)) continue;
      visited.set(current.lane.id, current);
      if (current.lane === to) {
        result = [];
        for (let item = current; item; item = item.previous) result.push(item.lane);
        result.reverse();
        break;
      }
      for (const next of current.lane.next) {
        if (!visited.has(next.id)) pending.push({lane: next, cost: current.cost + current.lane.distance, previous: current});
      }
    }
    if (this.routes.size >= maximumCachedRoutes) this.routes.clear();
    this.routes.set(key, result);
    return result;
  }

  path(startCar, endCar, simulationDelta) {
    const startLane = this.lanes.get(startCar.lane_id), endLane = this.lanes.get(endCar.lane_id);
    if (!startLane || !endLane) return null;
    const start = projection(startLane, startCar), end = projection(endLane, endCar);
    if (!start || !end) return null;
    let points = [], speedLimit = Math.max(startLane.speed, endLane.speed);
    const fractionTolerance = positionToleranceMeters / Math.max(startLane.distance, endLane.distance);
    if (startLane.edgeId === endLane.edgeId && end.distance / endLane.distance >= start.distance / startLane.distance - fractionTolerance) {
      if (startLane === endLane) appendLane(points, startLane, start.distance, end.distance);
      else appendLaneChange(points, startLane, endLane, start.distance / startLane.distance, end.distance / endLane.distance);
    } else {
      let selected = null, cost = Infinity;
      for (const from of this.edges.get(startLane.edgeId)) {
        for (const to of this.edges.get(endLane.edgeId)) {
          const route = this.route(from, to);
          if (!route) continue;
          let length = from.distance * (1 - start.distance / startLane.distance) + to.distance * end.distance / endLane.distance;
          for (let index = 0; index < route.length - 1; index++) length += route[index].distance;
          // Favor the reported lanes; inferred lane changes are a fallback.
          const score = length + (from === startLane ? 0 : 10) + (to === endLane ? 0 : 10);
          if (score < cost) { selected = {from, to, route}; cost = score; }
        }
      }
      if (!selected) return null;
      if (selected.from === startLane) appendLane(points, startLane, start.distance, startLane.distance);
      else appendLaneChange(points, startLane, selected.from, start.distance / startLane.distance, 1);
      for (let index = 0; index < selected.route.length - 1; index++) {
        const lane = selected.route[index];
        appendLane(points, lane, 0, lane.distance);
        speedLimit = Math.max(speedLimit, lane.speed);
      }
      if (selected.to === endLane) appendLane(points, endLane, 0, end.distance);
      else appendLaneChange(points, selected.to, endLane, 0, end.distance / endLane.distance);
    }
    if (!points.length) return null;
    const path = makePath(points, start, end, speedLimit);
    // A clock-scaled travel bound replaces the old fixed distance cutoff.
    // A real engine teleport should not be rendered as an invented road journey.
    const maximumTravel = Math.max(startCar.speed_mps, endCar.speed_mps, speedLimit) * simulationDelta * 1.75 + 10;
    return path.distance <= maximumTravel ? path : null;
  }
}
