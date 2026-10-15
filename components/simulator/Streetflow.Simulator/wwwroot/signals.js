// Each character controls a connection's linkIndex, not an entire junction.
export function signalAppearance(code) {
  switch (code) {
    case 'r': return {label: 'Red · stop', colors: ['#f36c65']};
    case 'y': case 'Y': return {label: 'Yellow · clearing', colors: ['#ffd166']};
    case 'u': return {label: 'Red + yellow · prepare, remain stopped', colors: ['#f36c65', '#ffd166']};
    case 'G': return {label: 'Green · priority', colors: ['#63d8b1']};
    case 'g': return {label: 'Green · yield to conflicting traffic', colors: ['#63d8b1']};
    case 's': return {label: 'Green arrow · stop, then yield', colors: ['#63d8b1']};
    case 'o': return {label: 'Off · blinking, yield', colors: ['#b6a880']};
    case 'O': return {label: 'Off · priority', colors: ['#a19b93']};
    case 'm': return {label: 'Mixed device state', colors: ['#777777']};
    default: return {label: 'No live signal state', colors: ['#777777']};
  }
}

export function movementState(point, signals) {
  const signal = signals.find(item => item.id === point.signal_id);
  return signal?.state[point.link_index];
}

export function movementDescription(point) {
  const directions = {s: 'straight', l: 'left', r: 'right', L: 'partial left', R: 'partial right', t: 'U-turn'};
  return `${point.lane_id} → ${point.to_lane_id} · ${directions[point.direction] || 'movement'} · link ${point.link_index} · controller ${point.signal_id}`;
}

// Keep every visible signal readable at map scale; leaders retain the exact stop-line location.
export function layoutSignalMarkers(points, project, width, height, size = 1) {
  const markers = [];
  const halfWidth = 10 * size, halfHeight = 19 * size, stepX = 22 * size, stepY = 40 * size;
  for (const point of points) {
    const anchor = project(point);
    if (anchor.x < 0 || anchor.y < 0 || anchor.x > width || anchor.y > height) continue;
    let placed = null;
    for (let ring = 0; ring <= 12 && !placed; ring++) {
      for (let row = -ring; row <= ring && !placed; row++) {
        for (let column = -ring; column <= ring; column++) {
          if (ring > 0 && Math.max(Math.abs(row), Math.abs(column)) !== ring) continue;
          const x = Math.max(halfWidth, Math.min(width - halfWidth, anchor.x + 13 * size + column * stepX));
          const y = Math.max(halfHeight, Math.min(height - halfHeight, anchor.y - 18 * size + row * stepY));
          if (markers.some(marker => Math.abs(marker.x - x) < halfWidth * 2 && Math.abs(marker.y - y) < halfHeight * 2)) continue;
          placed = {x, y, point, anchor};
          break;
        }
      }
    }
    if (placed) markers.push(placed);
  }
  return markers;
}

// Physical-device states are provider-neutral; SUMO signal indices are not part of their map.
export function deviceState(device, states) {
  const state = states.find(item => item.id === device.id)?.state;
  return {red: 'r', 'red-yellow': 'u', yellow: 'y', green: 'G', off: 'o', mixed: 'm'}[state];
}

export function deviceDescription(device) {
  return `${device.label} · ${device.lane_ids.join(', ')}`;
}

// Lane IDs are explicit device associations; never derive devices from SUMO movements.
export function controlledLanes(device, lanes) {
  const result = [];
  if (!device) return result;
  const ids = new Set(device.lane_ids);
  for (const lane of lanes) {
    if (ids.has(lane.id)) result.push(lane);
  }
  return result;
}

export function deviceMapStatus(devices) {
  if (!devices.length) return 'No physical traffic lights are configured for this map. Add a physical-device mapping to the project folder, refresh it from disk, and load a new simulation. Resetting a saved run keeps its original mapping.';
  return `${devices.length} physical traffic ${devices.length === 1 ? 'light' : 'lights'} configured. Select or hover a light to highlight its controlled incoming lanes.`;
}
