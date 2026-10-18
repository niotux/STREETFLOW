// Road and vehicle appearance shared by the flat (canvas) and 3D (three.js) map modes.
// World coordinates are SUMO metres: x east, y north. Lanes drive on the right; index 0 is the rightmost lane.

export const defaultLaneWidth = 3.2;
export const sidewalkWidth = 2;
export const crossingWidth = 3;
export const vehicleLength = 4.4;
export const vehicleWidth = 1.85;

export const colors = {
  asphalt: '#232a35',
  junction: '#1e242e',
  overview: '#8a4a1c',
  curb: '#ff7a3c',
  line: '#cfd6e4',
  stop: '#ffffff',
  sidewalk: '#2f343d',
  zebra: '#dde3ee',
  headlight: '#fff2dc',
  brake: '#ff2e22'
};

// Muted paints after the Streetflow hook render: blue-greys, a few silver and burgundy cars.
export const paints = ['#5b6b85', '#8a93a6', '#3f5770', '#7d2a33', '#a7adb8', '#4f6a8a', '#c9ccd3', '#6b6f7a'];

export function paintFor(car, fallback = 0) {
  const id = car.id ?? String(fallback);
  let hash = 0;
  for (let index = 0; index < id.length; index++) hash = (hash * 31 + id.charCodeAt(index)) | 0;
  return paints[Math.abs(hash) % paints.length];
}

export const braking = car => car.speed_mps < 0.8;

export function laneKind(lane) {
  return lane.render_kind || (lane.id.startsWith(':') ? 'junction' : 'road');
}

export const laneWidth = lane => lane.width || (laneKind(lane) === 'sidewalk' ? sidewalkWidth : defaultLaneWidth);

// Polyline shifted sideways; positive distance is to the left of the direction of travel.
export function offsetLine(shape, distance) {
  if (!distance) return shape;
  const points = [];
  for (let index = 0; index < shape.length; index++) {
    const previous = shape[Math.max(0, index - 1)], next = shape[Math.min(shape.length - 1, index + 1)];
    const dx = next.x - previous.x, dy = next.y - previous.y;
    const length = Math.hypot(dx, dy) || 1;
    points.push({x: shape[index].x - dy / length * distance, y: shape[index].y + dx / length * distance});
  }
  return points;
}

function laneIndex(lane) {
  const match = /_(\d+)$/.exec(lane.id);
  return match ? Number(match[1]) : 0;
}

// Painted lines derived from lane geometry: orange curbs on the outside, a solid line on the
// left of each carriageway (two opposing edges make a double centre line), dashed lane dividers
// and a stop line at the end of each lane.
export function roadMarkings(lanes) {
  const edges = new Map();
  for (const lane of lanes) {
    if (laneKind(lane) !== 'road' || lane.shape.length < 2) continue;
    const edge = lane.edge_id || lane.id.replace(/_\d+$/, '');
    if (!edges.has(edge)) edges.set(edge, []);
    edges.get(edge).push(lane);
  }
  const markings = {curbs: [], centers: [], dividers: [], stops: []};
  for (const group of edges.values()) {
    group.sort((a, b) => laneIndex(a) - laneIndex(b));
    group.forEach((lane, index) => {
      const half = laneWidth(lane) / 2;
      if (index === 0) markings.curbs.push(offsetLine(lane.shape, -half));
      if (index === group.length - 1) markings.centers.push(offsetLine(lane.shape, half - 0.15));
      else markings.dividers.push(offsetLine(lane.shape, half));
      const stop = stopLine(lane.shape, half);
      if (stop) markings.stops.push(stop);
    });
  }
  return markings;
}

function stopLine(shape, half) {
  const end = shape[shape.length - 1], before = shape[shape.length - 2];
  const dx = end.x - before.x, dy = end.y - before.y, length = Math.hypot(dx, dy);
  if (length < 1) return null;
  const ux = dx / length, uy = dy / length, back = 0.6;
  const x = end.x - ux * back, y = end.y - uy * back;
  return [{x: x + uy * half, y: y - ux * half}, {x: x - uy * half, y: y + ux * half}];
}

// Dash pieces along a polyline, in metres, for renderers without native dashing.
export function dashes(shape, dash, gap) {
  const pieces = [];
  let current = [], inDash = true, remaining = dash;
  for (let index = 1; index < shape.length; index++) {
    const a = shape[index - 1], b = shape[index];
    const length = Math.hypot(b.x - a.x, b.y - a.y);
    let walked = 0;
    if (inDash && !current.length) current.push(a);
    while (length - walked > remaining) {
      walked += remaining;
      const point = {x: a.x + (b.x - a.x) * walked / length, y: a.y + (b.y - a.y) * walked / length};
      if (inDash) { current.push(point); pieces.push(current); current = []; }
      else current = [point];
      inDash = !inDash;
      remaining = inDash ? dash : gap;
    }
    remaining -= length - walked;
    if (inDash) current.push(b);
  }
  if (current.length > 1) pieces.push(current);
  return pieces;
}
