import {signalAppearance, deviceState, deviceDescription, controlledLanes, deviceMapStatus} from '/balthasar/signals.js?v=physical-devices-3';
import {MapView, queuedVehicles} from '/balthasar/map-view.js?v=nerv-1';
import {createWorkspace} from '/ui/windows.js?v=shared-ui-2';

const $ = id => document.getElementById(id);
const canvas = $('map');
const actions = [['off', 'Off'], ['red', 'Red'], ['red-yellow', 'Red + yellow'], ['green', 'Green'], ['yellow', 'Yellow']];
let session = null, network = null, states = [], provider = '';
let traffic = null, queues = null, laneById = new Map();
let cachedMap = null, cachedProvider = '';
let busy = false, polling = false, queuePolling = false, available = false, epoch = 0;
let hoveredDevice = null;
let signalMode = null;
let policyDirty = false;

const workspace = createWorkspace({
  root: $('melchior-workspace'),
  layer: $('applet-layer'),
  storageKey: 'streetflow.melchior.workspace.v1',
  taskbar: $('taskbar'),
  resetButton: $('reset-layout'),
  focusButton: $('focus-view'),
  announcement: $('workspace-announcement'),
  stackedMedia: '(max-width: 760px)',
  initialLayout(name, size) {
    const width = name === 'connection' ? 320 : 384;
    return {
      rectangle: {x: name === 'connection' ? 0 : size.width - width, y: 0, width, height: size.height},
      open: true
    };
  }
});
for (const content of document.querySelectorAll('[data-applet]')) {
  workspace.registerApplet({id: content.dataset.applet, title: content.dataset.title, category: content.dataset.category, icon: content.dataset.icon, content});
}

const mapView = new MapView(canvas, {
  vehicles: () => queuedCars(),
  deviceStates: () => available ? states : [],
  focusedDevice: () => hoveredDevice || selectedDevice(),
  highlightedLanes: device => queuePath(device),
  markerActive: device => device.id === $('controller').value || device.id === hoveredDevice?.id,
  onHover: event => {
    const next = mapView.signalAt(event)?.point || null;
    canvas.style.cursor = next ? 'pointer' : 'grab';
    if (hoveredDevice !== next) { hoveredDevice = next; draw(); }
    renderMapDetail();
  },
  onLeave: () => { hoveredDevice = null; renderMapDetail(); draw(); },
  onClick: event => {
    if (busy) return;
    const marker = mapView.signalAt(event);
    if (marker) { $('signal-filter').value = ''; fillDevices(marker.point.id); draw(); }
  }
});
const draw = () => mapView.draw();

async function api(path, method = 'GET', body) {
  const response = await fetch(`/melchior-api${path}`, {
    method,
    signal: method === 'GET' ? AbortSignal.timeout(5000) : undefined,
    headers: body === undefined ? {} : {'Content-Type': 'application/json'},
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || `Request failed (${response.status})`);
  return data;
}

// TACO queue frame (little-endian): "TQ01", int64 sequence, float64 time, uint8 available, uint16 devices,
// then per device a uint8 lane count and one uint16 queued count per lane (0xFFFF = unknown).
function decodeQueues(buffer) {
  const data = new DataView(buffer);
  if (buffer.byteLength < 23 || String.fromCharCode(...new Uint8Array(buffer, 0, 4)) !== 'TQ01') throw new Error('The provider returned an invalid queue frame');
  const devices = [];
  let offset = 23;
  for (let device = 0, count = data.getUint16(21, true); device < count; device++) {
    const lanes = [];
    for (let lane = 0, total = data.getUint8(offset++); lane < total; lane++, offset += 2) {
      const value = data.getUint16(offset, true);
      lanes.push(value === 0xffff ? null : value);
    }
    devices.push(lanes);
  }
  return {sequence: Number(data.getBigInt64(4, true)), available: data.getUint8(20) === 1, devices};
}

// Queue counts index the map's devices and their lane_ids in order; a frame for another map is ignored.
function queueCounts(device) {
  const index = network?.devices.indexOf(device) ?? -1;
  const counts = queues?.available ? queues.devices[index] : null;
  return counts && counts.length === device.lane_ids.length ? counts : null;
}

function queuedCars() {
  const cars = [];
  for (const device of network?.devices || []) {
    const counts = queueCounts(device);
    if (!counts) continue;
    device.lane_ids.forEach((id, index) => {
      const lane = laneById.get(id);
      if (lane && counts[index]) cars.push(...queuedVehicles(lane, counts[index], laneById));
    });
  }
  return cars;
}

// The selected light's controlled lanes, extended upstream far enough to sit under its counted cars.
function queuePath(device) {
  if (!device) return [];
  const counts = queueCounts(device) || [];
  const result = [];
  device.lane_ids.forEach((id, index) => {
    let lane = laneById.get(id), remaining = (counts[index] || 0) * 7.5;
    const seen = new Set();
    while (lane && !seen.has(lane.id)) {
      seen.add(lane.id);
      result.push(lane);
      remaining -= laneLength(lane);
      if (remaining <= 0) break;
      lane = lane.upstream_lane_id ? laneById.get(lane.upstream_lane_id) : null;
    }
  });
  return result.length ? result : controlledLanes(device, network.lanes);
}

function laneLength(lane) {
  let total = 0;
  for (let index = 1; index < lane.shape.length; index++) total += Math.hypot(lane.shape[index].x - lane.shape[index - 1].x, lane.shape[index].y - lane.shape[index - 1].y);
  return total;
}

function queueSummary(device) {
  const counts = queueCounts(device);
  if (!counts) return 'Queues unavailable';
  return `On approach ${counts.reduce((sum, count) => sum + (count ?? 0), 0)} · ` + device.lane_ids.map((id, index) => `${id}: ${counts[index] ?? '?'}`).join(', ');
}

function showError(error) {
  $('error').textContent = error.message;
  $('error').hidden = false;
}

function connected(value, label) {
  $('connection').dataset.connected = String(value);
  $('connection').textContent = label;
}

function selectedDevice() {
  return network?.devices.find(device => device.id === $('controller').value);
}

function selectedState() {
  return states.find(state => state.id === $('controller').value)?.state || 'unknown';
}

function updateControls() {
  const disabled = busy || !available || !session?.id || !selectedDevice() || selectedState() === 'unknown' || signalMode?.mode === 'automatic';
  $('automatic-signals').disabled = busy || !available || !session?.id || !signalMode?.automatic_supported;
  $('automatic-signals').checked = signalMode?.mode === 'automatic';
  $('adaptive-settings').querySelectorAll('input, button').forEach(element => {
    element.disabled = busy || !available || !session?.id || !signalMode?.adaptive_supported;
  });
  if (!policyDirty) {
    const policy = signalMode?.policy;
    $('prevent-spillback').checked = policy?.prevent_spillback ?? true;
    $('demand-weighting').checked = policy?.weight_demand ?? true;
    $('signal-multiplier').value = policy?.multiplier ?? '';
  }
  $('automatic-status').textContent = !session?.id ? 'Load a simulation to start automatic signals.'
    : !available ? 'Signal control is unavailable.'
    : !signalMode?.automatic_supported ? 'This provider has no automatic signal timing.'
    : signalMode.mode === 'automatic' ? signalMode.adaptive_supported
      ? 'Automatic · downstream capacity checks and demand weights run with simulation time.'
      : 'Automatic · lights follow the project’s timing plans. Pausing the simulation pauses the lights.'
    : 'Manual · lights hold their current state. Enable automatic cycling to resume the timing plans.';
  $('network-mode').textContent = signalMode?.mode === 'automatic' ? 'CONNECTED NETWORK / AUTOMATIC' : 'CONNECTED NETWORK / MANUAL';
  $('connection-form').querySelector('button').disabled = busy;
  $('target-host').disabled = busy;
  $('target-port').disabled = busy;
  $('refresh-runs').disabled = busy;
  $('run').disabled = true;
  $('controller').disabled = busy || !network?.devices.length;
  $('controller-enabled').disabled = disabled;
  $('switchboard').querySelectorAll('button, input').forEach(element => { element.disabled = disabled; });
  $('fit').disabled = !network;
}

async function perform(work) {
  if (busy) return;
  busy = true;
  ++epoch;
  $('error').hidden = true;
  updateControls();
  try {
    await work();
  } catch (error) {
    available = false;
    connected(false, 'Connection error');
    showError(error);
  } finally {
    busy = false;
    renderController();
    draw();
  }
}

function setNetwork(map) {
  network = map;
  laneById = new Map((map?.lanes || []).map(lane => [lane.id, lane]));
  queues = null;
  if (map) mapView.setNetwork({lanes: map.lanes, devices: map.devices});
  else { mapView.network = null; draw(); }
}

function clearSession() {
  traffic = null;
  $('traffic-summary').textContent = 'Waiting for traffic measurements.';
  session = null;
  setNetwork(null);
  states = [];
  signalMode = null;
  policyDirty = false;
  available = false;
  $('map-label').textContent = 'Waiting for an active world.';
  $('map-empty').hidden = false;
  $('command-status').textContent = '';
  $('run').replaceChildren(new Option('No active session', ''));
  hoveredDevice = null;
  $('run-note').textContent = 'No active session. In Balthasar, choose a project in Loader and click Load.';
  fillDevices();
}

function sameWorld(map, next) {
  return map && cachedProvider === provider && map.world_id === next.world_id && map.revision === next.revision;
}

async function loadSession(next, version) {
  const reuse = sameWorld(cachedMap, next);
  clearSession();
  updateControls();
  const path = `/sessions/${encodeURIComponent(next.id)}`;
  const [map, live, mode] = await Promise.all([reuse ? Promise.resolve(cachedMap) : api(`${path}/map`), api(`${path}/devices`), api(`${path}/signal-mode`)]);
  if (version !== epoch) return;
  if (map.world_id !== next.world_id || map.revision !== next.revision) throw new Error('World changed while loading. Retrying discovery.');
  cachedMap = map;
  cachedProvider = provider;
  session = next;
  states = live;
  signalMode = mode;
  available = true;
  setNetwork(map);
  $('map-label').textContent = map.world_id;
  $('map-empty').hidden = map.lanes.length > 0 || map.devices.length > 0;
  $('run').replaceChildren(new Option(`${next.project_id || next.world_id} · ${next.state}`, next.id));
  $('run-note').textContent = 'Following the provider’s active session automatically. Map loads only when its world or configuration changes.';
  fillDevices();
}

async function refreshSession(version) {
  const [next, connection] = await Promise.all([api('/session'), api('/connection')]);
  if (version !== epoch) return;
  if (provider !== connection.base_url) {
    provider = connection.base_url;
    cachedMap = null;
    clearSession();
    displayConnection(provider);
  }
  if (!next.id) {
    clearSession();
    connected(true, 'Waiting for active session');
    updateControls();
    return;
  }
  const changed = !session || session.id !== next.id || session.world_id !== next.world_id || session.revision !== next.revision;
  if (changed) {
    await loadSession(next, version);
  } else {
    const [live, mode] = await Promise.all([api(`/sessions/${encodeURIComponent(next.id)}/devices`), api(`/sessions/${encodeURIComponent(next.id)}/signal-mode`)]);
    if (version !== epoch) return;
    session = next;
    states = live;
    signalMode = mode;
    available = true;
    $('run').selectedOptions[0].textContent = `${next.project_id || next.world_id} · ${next.state}`;
    renderController();
    draw();
  }
  if (version !== epoch) return;
  $('error').hidden = true;
  try {
    const measured = await api(`/sessions/${encodeURIComponent(next.id)}/traffic`);
    if (version !== epoch) return;
    traffic = measured.session_id === next.id && measured.revision === next.revision ? measured : null;
  } catch { traffic = null; }
  if (version !== epoch) return;
  renderTraffic();
  connected(true, network.devices.length ? 'taco connected · devices mapped' : 'taco connected · no mapped lights');
  updateControls();
}

// Queue frames are small and binary, so they refresh much faster than the session and map.
async function pollQueues() {
  if (queuePolling || !session?.id || !network) return;
  queuePolling = true;
  const target = session.id, map = network;
  try {
    const response = await fetch(`/melchior-api/sessions/${encodeURIComponent(target)}/queues`, {signal: AbortSignal.timeout(2000)});
    if (!response.ok) throw new Error(`Queue request failed (${response.status})`);
    const frame = decodeQueues(await response.arrayBuffer());
    if (session?.id !== target || network !== map) return;
    if (frame.devices.length !== map.devices.length) { queues = null; return; }
    queues = frame;
  } catch {
    if (session?.id === target) queues = null;
  } finally {
    queuePolling = false;
    renderTraffic();
    renderMapDetail();
    draw();
  }
}

function fillDevices(preferred = $('controller').value) {
  const query = $('signal-filter').value.trim().toLowerCase();
  const matching = (network?.devices || []).filter(device => `${device.id} ${device.label} ${device.lane_ids.join(' ')}`.toLowerCase().includes(query));
  $('controller').replaceChildren();
  for (const device of matching) $('controller').add(new Option(device.label, device.id));
  if (matching.some(device => device.id === preferred)) $('controller').value = preferred;
  if (!matching.length) $('controller').add(new Option(network?.devices.length ? 'No matching devices' : 'No physical devices configured', ''));
  renderController();
}

function renderController() {
  renderTraffic();
  const device = selectedDevice();
  const state = selectedState();
  const adaptive = signalMode?.adaptive_devices?.find(item => item.id === device?.id);
  $('adaptive-summary').textContent = adaptive && signalMode.mode === 'automatic'
    ? `${adaptive.queue_buffer ? `Queue buffer · about ${adaptive.capacity_vehicles} cars` : 'Standard approach'} · road ahead ${Math.round(adaptive.downstream_occupancy * 100)}% occupied`
      + (adaptive.blocked_movements ? ` · ${adaptive.blocked_movements} blocked turn(s) held red` : '')
      + ` · ${adaptive.rush_hour ? 'Rush-hour' : 'Off-peak'} k=${adaptive.multiplier.toFixed(2)}` : '';
  $('adaptive-groups').textContent = adaptive && signalMode.mode === 'automatic'
    ? `A: ${adaptive.group_a || 'No vehicle group'} · serviceable demand ${adaptive.demand_a.toFixed(0)} · green ${adaptive.green_a_seconds.toFixed(1)}s. B: ${adaptive.group_b || 'No vehicle group'} · serviceable demand ${adaptive.demand_b.toFixed(0)} · green ${adaptive.green_b_seconds.toFixed(1)}s.` : '';
  $('switchboard').hidden = !device;
  $('signal-summary').textContent = network ? deviceMapStatus(network.devices) : 'Waiting for an active world. Load a simulation in Balthasar’s Loader.';
  if (network && !available) $('signal-summary').textContent += ' Live states are unavailable; controls are disabled.';
  else if (device && state === 'unknown') $('signal-summary').textContent += ' The provider has not supplied a live state for this device; controls are disabled.';
  renderMapDetail();
  $('movements').replaceChildren();
  $('controller-actions').replaceChildren();
  if (device) {
    $('controller-enabled').checked = ['red', 'red-yellow', 'green', 'yellow', 'mixed'].includes(state);
    $('controller-enabled').indeterminate = state === 'mixed';
    $('controller-state').textContent = available ? state : 'unavailable';
    const description = document.createElement('p');
    description.className = 'movement-description';
    description.textContent = deviceDescription(device);
    $('movements').append(description);
    for (const [action, label] of actions) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = label;
      button.dataset.action = action;
      button.setAttribute('aria-pressed', String(state === action));
      button.onclick = () => perform(() => command(device.id, action));
      $('controller-actions').append(button);
    }
  }
  updateControls();
}

function renderMapDetail() {
  let detail = 'Waiting for an active simulation.';
  if (network) {
    detail = 'Vehicles on each light’s approach, as counted by taco. Select or hover a light to highlight its lanes and vehicles.';
    if (!network.devices.length) detail = 'Roads loaded. Physical traffic lights need a project mapping before they can appear here.';
    const device = hoveredDevice || selectedDevice();
    if (device) detail = `${signalAppearance(available ? deviceState(device, states) : undefined).label} · ${device.label} · ${queueSummary(device)}`;
  }
  $('map-detail').textContent = detail;
}

async function command(deviceId, action) {
  const target = session;
  const body = {action, world_id: target.world_id, revision: target.revision};
  $('command-status').textContent = 'Applying…';
  const applied = await api(`/sessions/${encodeURIComponent(target.id)}/devices/${encodeURIComponent(deviceId)}`, 'PUT', body);
  states = states.map(state => state.id === applied.id ? applied : state);
  available = true;
  renderController();
  $('command-status').textContent = `Applied ${action} to ${selectedDevice()?.label || deviceId} · ${applied.state}`;
}

async function poll() {
  if (busy || polling) return;
  polling = true;
  const version = epoch;
  try {
    await refreshSession(version);
  } catch (error) {
    if (version === epoch) {
      traffic = null;
      available = false;
      connected(false, 'taco unavailable');
      showError(error);
      renderController();
      draw();
    }
  } finally {
    polling = false;
  }
}

function renderTraffic() {
  const device = selectedDevice();
  const lanes = traffic?.lanes.filter(lane => !device || lane.device_ids.includes(device.id));
  if (!lanes?.length) { $('traffic-summary').textContent = 'Traffic measurements unavailable.'; return; }
  const total = field => lanes.every(lane => lane[field] !== null) ? lanes.reduce((sum, lane) => sum + lane[field], 0) : 'Unavailable';
  const demo = lanes.some(lane => lane.measurement_method === 'synthetic_demo');
  $('traffic-summary').textContent = `${demo ? 'SYNTHETIC DEMO · ' : ''}Visible: ${total('visible_vehicle_count')} · Estimated queue: ${total('queued_vehicle_count')} · ${traffic.status}`
    + (device ? ` · ${queueSummary(device)}` : '');
}

$('connection-form').onsubmit = event => {
  event.preventDefault();
  perform(async () => {
    const target = new URL($('target-host').value);
    if (!['http:', 'https:'].includes(target.protocol) || target.pathname !== '/' || target.search || target.hash || target.username || target.password) throw new Error('Enter an HTTP or HTTPS server address without a path, query or credentials.');
    target.port = $('target-port').value;
    clearSession();
    cachedMap = null;
    const saved = await api('/connection', 'PUT', {base_url: target.origin});
    provider = saved.base_url;
    await refreshSession(epoch);
  });
};
$('refresh-runs').onclick = () => perform(() => refreshSession(epoch));
$('controller').onchange = () => { $('command-status').textContent = ''; renderController(); draw(); };
$('signal-filter').oninput = () => { fillDevices(); draw(); };
$('controller-enabled').onchange = event => {
  const device = selectedDevice();
  if (device) perform(() => command(device.id, event.target.checked ? 'on' : 'off'));
};
$('automatic-signals').onchange = event => {
  const automatic = event.target.checked;
  perform(async () => {
    const target = session;
    if (!target?.id) throw new Error('Load a simulation before changing signal control.');
    signalMode = await api(`/sessions/${encodeURIComponent(target.id)}/signal-mode`, 'PUT', {
      mode: automatic ? 'automatic' : 'manual', world_id: target.world_id, revision: target.revision, policy: signalMode?.policy
    });
    states = await api(`/sessions/${encodeURIComponent(target.id)}/devices`);
  });
};
$('adaptive-settings').oninput = () => { policyDirty = true; };
$('adaptive-settings').onsubmit = event => {
  event.preventDefault();
  perform(async () => {
    const target = session;
    if (!target?.id) throw new Error('Load a simulation before changing traffic rules.');
    const value = $('signal-multiplier').value;
    const policy = {
      prevent_spillback: $('prevent-spillback').checked,
      weight_demand: $('demand-weighting').checked,
      multiplier: value === '' ? null : Number(value)
    };
    signalMode = await api(`/sessions/${encodeURIComponent(target.id)}/signal-mode`, 'PUT', {
      mode: signalMode.mode, world_id: target.world_id, revision: target.revision, policy
    });
    policyDirty = false;
    states = await api(`/sessions/${encodeURIComponent(target.id)}/devices`);
  });
};
$('fit').onclick = () => mapView.fit();

function displayConnection(address) {
  const target = new URL(address);
  $('target-port').value = target.port || (target.protocol === 'https:' ? '443' : '80');
  target.port = '';
  $('target-host').value = target.origin;
}

await perform(async () => {
  const saved = await api('/connection');
  provider = saved.base_url;
  displayConnection(provider);
  await refreshSession(epoch);
});
setInterval(poll, 1500);
setInterval(pollQueues, 250);
