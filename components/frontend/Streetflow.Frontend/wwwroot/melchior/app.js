import {signalAppearance, deviceState, deviceDescription, layoutSignalMarkers, controlledLanes, deviceMapStatus} from '/balthasar/signals.js?v=physical-devices-3';

const $ = id => document.getElementById(id);
const canvas = $('map');
const context = canvas.getContext('2d');
const actions = [['off', 'Off'], ['red', 'Red'], ['red-yellow', 'Red + yellow'], ['green', 'Green'], ['yellow', 'Yellow']];
let session = null, network = null, states = [], provider = '';
let cachedMap = null, cachedProvider = '';
let busy = false, polling = false, available = false, epoch = 0;
let width = 1, height = 1, view = {x: 0, y: 0, scale: 1}, markers = [], drag = null, hoveredDevice = null;

async function api(path, method = 'GET', body) {
  const response = await fetch(`/melchior-api${path}`, {
    method,
    headers: body === undefined ? {} : {'Content-Type': 'application/json'},
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.message || `Request failed (${response.status})`);
  return data;
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
  const disabled = busy || !available || !session?.id || !selectedDevice() || selectedState() === 'unknown';
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

function clearSession() {
  session = null;
  network = null;
  states = [];
  available = false;
  $('map-label').textContent = 'Waiting for an active world.';
  $('map-empty').hidden = false;
  $('command-status').textContent = '';
  $('run').replaceChildren(new Option('No active session', ''));
  hoveredDevice = null;
  $('run-note').textContent = 'No active session. In Balthasar, load a simulation in Loader. Selecting a project preview does not create a session.';
  fillDevices();
  draw();
}

function sameWorld(map, next) {
  return map && cachedProvider === provider && map.world_id === next.world_id && map.revision === next.revision;
}

async function loadSession(next, version) {
  const reuse = sameWorld(cachedMap, next);
  clearSession();
  updateControls();
  const path = `/sessions/${encodeURIComponent(next.id)}`;
  const [map, live] = await Promise.all([reuse ? Promise.resolve(cachedMap) : api(`${path}/map`), api(`${path}/devices`)]);
  if (version !== epoch) return;
  if (map.world_id !== next.world_id || map.revision !== next.revision) throw new Error('World changed while loading. Retrying discovery.');
  cachedMap = map;
  cachedProvider = provider;
  session = next;
  network = map;
  states = live;
  available = true;
  $('map-label').textContent = map.world_id;
  $('map-empty').hidden = map.lanes.length > 0 || map.devices.length > 0;
  $('run').replaceChildren(new Option(`${next.project_id || next.world_id} · ${next.state}`, next.id));
  $('run-note').textContent = 'Following the provider’s active session automatically. Map loads only when its world or configuration changes.';
  fillDevices();
  fit();
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
    const live = await api(`/sessions/${encodeURIComponent(next.id)}/devices`);
    if (version !== epoch) return;
    session = next;
    states = live;
    available = true;
    $('run').selectedOptions[0].textContent = `${next.project_id || next.world_id} · ${next.state}`;
    renderController();
    draw();
  }
  if (version !== epoch) return;
  $('error').hidden = true;
  connected(true, network.devices.length ? 'tlcapi connected · devices mapped' : 'tlcapi connected · no mapped lights');
  updateControls();
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
  const device = selectedDevice();
  const state = selectedState();
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
    detail = 'Select or hover a physical light to highlight its controlled incoming lanes.';
    if (!network.devices.length) detail = 'Roads loaded. Physical traffic lights need a project mapping before they can appear here.';
    const device = hoveredDevice || selectedDevice();
    if (device) detail = `${signalAppearance(available ? deviceState(device, states) : undefined).label} · ${deviceDescription(device)} · highlighted incoming lanes`;
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
      available = false;
      connected(false, 'tlcapi unavailable');
      showError(error);
      renderController();
      draw();
    }
  } finally {
    polling = false;
  }
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

function screen(point) {
  return {x: width / 2 + (point.x - view.x) * view.scale, y: height / 2 - (point.y - view.y) * view.scale};
}

function fit() {
  if (!network) return;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  const include = point => {
    minX = Math.min(minX, point.x); minY = Math.min(minY, point.y);
    maxX = Math.max(maxX, point.x); maxY = Math.max(maxY, point.y);
  };
  for (const lane of network.lanes) lane.shape.forEach(include);
  network.devices.forEach(include);
  if (Number.isFinite(minX)) view = {x: (minX + maxX) / 2, y: (minY + maxY) / 2, scale: Math.min(width / Math.max(1, maxX - minX), height / Math.max(1, maxY - minY)) * .8};
  draw();
}

function draw() {
  context.clearRect(0, 0, width, height);
  markers = [];
  if (!network) return;
  context.lineCap = 'round';
  context.lineJoin = 'round';
  context.strokeStyle = '#68899d';
  context.lineWidth = Math.max(1, Math.min(7, view.scale * 3));
  for (const lane of network.lanes) {
    context.beginPath();
    lane.shape.forEach((point, index) => {
      const position = screen(point);
      if (index) context.lineTo(position.x, position.y);
      else context.moveTo(position.x, position.y);
    });
    context.stroke();
  }
  context.strokeStyle = '#a8e76a';
  context.lineWidth = Math.max(4, Math.min(10, view.scale * 4));
  for (const lane of controlledLanes(hoveredDevice || selectedDevice(), network.lanes)) {
    context.beginPath();
    lane.shape.forEach((point, index) => {
      const position = screen(point);
      if (index) context.lineTo(position.x, position.y);
      else context.moveTo(position.x, position.y);
    });
    context.stroke();
  }
  markers = layoutSignalMarkers(network.devices, screen, width, height, .85);
  for (const marker of markers) {
    const code = available ? deviceState(marker.point, states) : undefined;
    context.strokeStyle = marker.point.id === $('controller').value || marker.point.id === hoveredDevice?.id ? '#a8e76a' : '#7f9ba9';
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(marker.anchor.x, marker.anchor.y);
    context.lineTo(marker.x, marker.y);
    context.stroke();
    context.fillStyle = '#111214';
    context.beginPath();
    context.roundRect(marker.x - 6, marker.y - 14, 12, 28, 3);
    context.fill();
    context.stroke();
    const lamps = [['r', '#ff5148'], ['y', '#ffcf38'], ['G', '#44eb88']];
    lamps.forEach(([lamp, color], index) => {
      context.fillStyle = code === lamp || code === 'u' && index < 2 ? color : '#343638';
      context.beginPath();
      context.arc(marker.x, marker.y + (index - 1) * 8, 3, 0, Math.PI * 2);
      context.fill();
    });
  }
}

function markerAt(event) {
  const rectangle = canvas.getBoundingClientRect();
  return markers.find(marker => Math.abs(marker.x - (event.clientX - rectangle.left)) < 12 && Math.abs(marker.y - (event.clientY - rectangle.top)) < 19);
}

new ResizeObserver(() => {
  const rectangle = canvas.getBoundingClientRect();
  width = rectangle.width; height = rectangle.height;
  const ratio = window.devicePixelRatio || 1;
  canvas.width = Math.round(width * ratio); canvas.height = Math.round(height * ratio);
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  fit();
}).observe(canvas);
$('fit').onclick = fit;
canvas.onpointerdown = event => { drag = {x: event.clientX, y: event.clientY, moved: false}; canvas.setPointerCapture(event.pointerId); };
canvas.onpointermove = event => {
  if (!drag) {
    const marker = markerAt(event);
    const nextHover = marker?.point || null;
    if (hoveredDevice !== nextHover) { hoveredDevice = nextHover; draw(); }
    renderMapDetail();
    return;
  }
  const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
  drag.moved ||= Math.hypot(dx, dy) > 3;
  view.x -= dx / view.scale; view.y += dy / view.scale;
  drag.x = event.clientX; drag.y = event.clientY;
  draw();
};
canvas.onpointerup = event => {
  const clicked = drag && !drag.moved;
  drag = null;
  if (!clicked || busy) return;
  const marker = markerAt(event);
  if (marker) { $('signal-filter').value = ''; fillDevices(marker.point.id); draw(); }
};
canvas.onpointercancel = () => { drag = null; };
canvas.onpointerleave = () => { hoveredDevice = null; renderMapDetail(); draw(); };
canvas.addEventListener('wheel', event => {
  event.preventDefault();
  const rectangle = canvas.getBoundingClientRect();
  const x = event.clientX - rectangle.left - width / 2, y = event.clientY - rectangle.top - height / 2;
  const old = view.scale;
  view.scale = Math.max(.005, Math.min(30, old * Math.exp(-event.deltaY * .001)));
  view.x += x / old - x / view.scale; view.y -= y / old - y / view.scale;
  draw();
}, {passive: false});

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
