import {archiveFolder} from './folder-archive.js';
const $ = id => document.getElementById(id);
const canvas = $('map');
const context = canvas.getContext('2d');
let projects = [], selectedProject = null, run = null, network = null, snapshot = null;
let busy = false, generation = 0, initialized = false;
let width = 1, height = 1, view = {x: 0, y: 0, scale: 1}, drag = null;
let terminalGroups = [], selectedTerminal = null;

async function api(path, method = 'GET', body) {
  const response = await fetch(`/api/v1${path}`, {
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
function clearError() { $('error').hidden = true; }
function active() { return run && ['starting', 'running', 'paused'].includes(run.state); }
function timeText(seconds) {
  if (seconds == null) return '—';
  const total = Math.floor(seconds);
  return [Math.floor(total / 3600), Math.floor(total / 60) % 60, total % 60].map(value => String(value).padStart(2, '0')).join(':');
}
function pacing() {
  const value = $('speed').value;
  return {mode: value === 'unpaced' ? 'unpaced' : 'realtime', multiplier: value === 'unpaced' ? 1 : Number(value)};
}
function controls() {
  $('project').disabled = busy || active();
  $('zip').disabled = busy || active();
  $('local-folder').disabled = busy || active();
  $('import').disabled = busy || active();
  $('import').firstChild.textContent = busy ? 'Working… ' : 'Import ';
  $('fit').disabled = !network;
  $('zoom-in').disabled = !network;
  $('zoom-out').disabled = !network;
  $('folder').disabled = busy || active();
  $('folder-form').querySelector('button').disabled = busy || active();
  $('seed').disabled = busy || active();
  $('start-time').disabled = busy || active();
  $('start').disabled = busy || active() || !selectedProject;
  $('speed').disabled = busy;
  syncSpeed();
  $('toggle').disabled = busy || !active();
  $('toggle').textContent = run?.state === 'paused' ? 'Resume' : 'Pause';
  $('step').disabled = busy || run?.state !== 'paused';
  $('stop').disabled = busy || !active();
  $('restart').disabled = busy || !run;
  $('state').textContent = run ? `${run.state[0].toUpperCase()}${run.state.slice(1)}${run.backlog_seconds > 5 ? ' · traffic waiting' : ''}` : selectedProject ? 'Map ready · traffic stopped' : 'No project loaded';
  $('state').dataset.state = run?.state || 'ready';
  $('vehicle-count').textContent = snapshot?.available ? snapshot.total_active_count.toLocaleString() : '—';
  $('lane-count').textContent = network ? network.lanes.length.toLocaleString() : '—';
  $('city-clock').textContent = timeText(run?.city_time_seconds ?? selectedProject?.manifest.defaults.start_time_seconds);
  $('city-day').textContent = run ? `CITY TIME · DAY ${run.day + 1}` : 'CITY TIME';
  $('run-note').textContent = active() ? 'Closing this page leaves traffic running.' : 'Use the same seed to replay the same traffic.';
}
async function action(work) {
  if (busy) return;
  busy = true;
  ++generation;
  clearError();
  controls();
  try { await work(); }
  catch (error) { showError(error); }
  finally { busy = false; controls(); }
}
async function refreshProjects() {
  projects = await api('/projects');
  const value = $('project').value;
  $('project').replaceChildren();
  if (!projects.length) {
    const option = new Option('Import a project to begin', '');
    $('project').add(option);
  }
  for (const project of projects) $('project').add(new Option(project.manifest.name, project.manifest.id));
  if (projects.some(project => project.manifest.id === value)) $('project').value = value;
}
async function openProject(id) {
  const token = ++generation;
  const info = projects.find(project => project.manifest.id === id);
  if (!info) return;
  selectedProject = info;
  run = null; snapshot = null; network = null;
  $('map-label').textContent = 'Loading streets…';
  const map = {lanes: [], junctions: [], terminals: []};
  for (let offset = 0; ; offset += 10000) {
    const page = await api(`/projects/${encodeURIComponent(id)}/map?offset=${offset}&limit=10000`);
    if (token !== generation) return;
    map.lanes.push(...page.lanes);
    map.junctions.push(...page.junctions);
    map.attribution = page.attribution;
    if (offset === 0) map.terminals = page.terminals || [];
    if (offset + 10000 >= Math.max(page.total_lanes, page.total_junctions)) break;
  }
  if (token !== generation) return;
  showMap(map, info.manifest.name);
  $('start-time').value = timeText(info.manifest.defaults.start_time_seconds);
  $('project-note').textContent = info.manifest.description || `${info.usable_edge_count} directed roads · daily traffic profiles`;
  $('snapshot-note').textContent = 'Map ready. Start traffic when you’re ready.';
  controls();
}
function showMap(map, name) {
  network = map;
  selectedTerminal = null;
  terminalGroups = [];
  $('terminal-tooltip').hidden = true;
  const terminals = map.terminals || [];
  $('terminal-inspector').hidden = terminals.length === 0;
  $('entry-legend').hidden = !terminals.some(terminal => terminal.role === 'entry');
  $('exit-legend').hidden = !terminals.some(terminal => terminal.role === 'exit');
  $('terminal-choice').replaceChildren(new Option('Choose a lane terminal', ''));
  for (const terminal of terminals) $('terminal-choice').add(new Option(terminalDescription(terminal), terminal.id));
  $('terminal-label').textContent = `Lane terminals · ${terminals.length}`;
  $('terminal-focus').disabled = true;
  $('terminal-detail').textContent = 'Hover a terminal to inspect it. Click a numbered group to zoom.';
  $('map-label').textContent = name;
  $('attribution').textContent = map.attribution || '';
  $('empty').hidden = true;
  fit();
}
async function openRun(status) {
  const token = ++generation;
  const map = await api(`/city-runs/${status.id}/map`);
  if (token !== generation) return;
  run = status;
  snapshot = null;
  selectedProject = projects.find(project => project.manifest.id === status.configuration.project_id) || null;
  $('project').value = status.configuration.project_id;
  $('seed').value = status.configuration.seed;
  $('start-time').value = timeText(status.configuration.start_time_seconds);
  $('speed').value = status.pacing.mode === 'unpaced' ? 'unpaced' : String(status.pacing.multiplier);
  showMap(map, selectedProject?.manifest.name || status.configuration.project_id);
  await refresh(token);
  controls();
}
async function refresh(token) {
  if (!run) return;
  const id = run.id;
  const [status, nextSnapshot] = await Promise.all([api(`/city-runs/${id}`), api(`/city-runs/${id}/snapshot`)]);
  if (token !== generation || busy) return;
  run = status;
  snapshot = nextSnapshot;
  controls();
  $('snapshot-note').textContent = status.state === 'failed' ? 'Simulation failed. Check the message above.' : !nextSnapshot.available ? 'Traffic stopped. Restart to replay this seed.' : `${nextSnapshot.total_active_count} cars on the map · orange cars are stopped`;
  if (status.gridlock_recoveries > 0) $('snapshot-note').textContent += ` · Gridlock recovery moved ${status.gridlock_recoveries} vehicles past blocked sections`;
  if (status.warning && !status.failure) $('snapshot-note').textContent += ` · ${status.warning}`;
  if (status.failure) showError(new Error(status.failure));
  draw();
}
async function importResult(info) {
  await refreshProjects();
  $('project').value = info.manifest.id;
  await openProject(info.manifest.id);
}
$('project').onchange = () => action(() => openProject($('project').value));
async function uploadProject(archive) {
  const response = await fetch('/api/v1/projects/import/zip', {method: 'POST', headers: {'Content-Type': 'application/zip'}, body: archive});
  const info = await response.json();
  if (!response.ok) throw new Error(info.message || `Import failed (${response.status})`);
  await importResult(info);
}
$('import').onclick = () => $('import-dialog').showModal();
$('close-import').onclick = () => $('import-dialog').close();
$('choose-zip').onclick = () => { $('import-dialog').close(); $('zip').click(); };
$('choose-folder').onclick = () => { $('import-dialog').close(); $('local-folder').click(); };
$('zip').onchange = () => {
  const file = $('zip').files[0];
  if (!file) return;
  action(() => uploadProject(file)).finally(() => { $('zip').value = ''; });
};
$('local-folder').onchange = () => {
  const files = Array.from($('local-folder').files);
  if (!files.length) return;
  action(async () => uploadProject(await archiveFolder(files))).finally(() => { $('local-folder').value = ''; });
};
$('folder-form').onsubmit = event => {
  event.preventDefault();
  action(async () => importResult(await api('/projects/import/folder', 'POST', {path: $('folder').value})));
};
$('start-form').onsubmit = event => {
  event.preventDefault();
  action(async () => {
    const parts = $('start-time').value.split(':').map(Number);
    const start = parts[0] * 3600 + parts[1] * 60 + (parts[2] || 0);
    const status = await api(`/projects/${encodeURIComponent(selectedProject.manifest.id)}/runs`, 'POST', {seed: Number($('seed').value), start_time_seconds: start, start_paused: false, pacing: pacing()});
    await openRun(status);
    if (status.failure) throw new Error(status.failure);
  });
};
for (const command of ['toggle', 'step', 'stop', 'restart']) $(command).onclick = () => action(async () => {
  const name = command === 'toggle' ? run.state === 'paused' ? 'resume' : 'pause' : command;
  const result = await api(`/city-runs/${run.id}/${name}`, 'POST', {});
  if (name === 'restart') await openRun(result);
  else {
    run = result;
    snapshot = await api(`/city-runs/${run.id}/snapshot`);
    controls(); draw();
  }
  if (result.failure) throw new Error(result.failure);
});
$('speed').onchange = () => {
  syncSpeed();
  if (active()) action(async () => { run = await api(`/city-runs/${run.id}/pacing`, 'POST', {pacing: pacing()}); });
};
const speedValues = ['0.5', '1', '2', '5', '10', 'unpaced'];
const speedDial = $('speed-dial');
function syncSpeed() {
  const value = $('speed').value;
  const index = speedValues.indexOf(value);
  const label = value === 'unpaced' ? 'MAX' : `${value}×`;
  speedDial.style.setProperty('--angle', `${-135 + index * 54}deg`);
  speedDial.setAttribute('aria-valuenow', index);
  speedDial.setAttribute('aria-valuetext', value === 'unpaced' ? 'As fast as possible' : `${value} times speed`);
  speedDial.setAttribute('aria-disabled', String(busy));
  $('dial-value').textContent = label;
  $('playback-value').textContent = label;
  $('dial-description').textContent = value === 'unpaced' ? 'Maximum speed' : value === '1' ? 'Real time' : 'Playback speed';
  for (const button of document.querySelectorAll('[data-speed]')) {
    button.setAttribute('aria-pressed', String(button.dataset.speed === value));
    button.disabled = busy;
  }
}
function setSpeed(index, commit = true) {
  if (busy) return;
  $('speed').value = speedValues[Math.max(0, Math.min(5, index))];
  syncSpeed();
  if (commit) $('speed').dispatchEvent(new Event('change'));
}
for (const button of document.querySelectorAll('[data-speed]')) {
  button.onclick = () => setSpeed(speedValues.indexOf(button.dataset.speed));
}
function turnDial(event) {
  const box = speedDial.getBoundingClientRect();
  const x = event.clientX - box.left - box.width / 2;
  const y = event.clientY - box.top - box.height / 2;
  const angle = Math.atan2(x, -y) * 180 / Math.PI;
  setSpeed(Math.round((Math.max(-135, Math.min(135, angle)) + 135) / 54), false);
}
let turningDial = false;
speedDial.onpointerdown = event => {
  if (busy) return;
  turningDial = true;
  speedDial.focus();
  speedDial.setPointerCapture(event.pointerId);
  turnDial(event);
};
speedDial.onpointermove = event => { if (turningDial) turnDial(event); };
speedDial.onpointerup = speedDial.onpointercancel = () => {
  if (!turningDial) return;
  turningDial = false;
  $('speed').dispatchEvent(new Event('change'));
};
speedDial.onkeydown = event => {
  const index = speedValues.indexOf($('speed').value);
  const increments = {ArrowUp: 1, ArrowRight: 1, ArrowDown: -1, ArrowLeft: -1};
  if (event.key in increments || event.key === 'Home' || event.key === 'End') {
    event.preventDefault();
    setSpeed(event.key === 'Home' ? 0 : event.key === 'End' ? 5 : index + increments[event.key]);
  }
};
$('focus-view').onclick = () => {
  const focused = document.body.classList.toggle('focus-view');
  $('focus-view').setAttribute('aria-pressed', String(focused));
  $('focus-view').textContent = focused ? 'Exit focus' : 'Focus view';
};
document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && document.body.classList.contains('focus-view')) $('focus-view').click();
});
$('zoom-in').onclick = () => { view.scale = Math.min(30, view.scale * 1.35); draw(); };
$('zoom-out').onclick = () => { view.scale = Math.max(0.005, view.scale / 1.35); draw(); };
function screen(x, y) { return {x: width / 2 + (x - view.x) * view.scale, y: height / 2 - (y - view.y) * view.scale}; }
function terminalDescription(terminal) {
  return `${terminal.role === 'entry' ? 'Entry' : 'Exit'} · ${terminal.lane_id}${terminal.label ? ` · ${terminal.label}` : ''}`;
}
function selectTerminal(terminal) {
  selectedTerminal = terminal;
  $('terminal-choice').value = terminal?.id || '';
  $('terminal-focus').disabled = !terminal;
  $('terminal-detail').textContent = terminal ? terminalDescription(terminal) : 'Hover a terminal to inspect it. Click a numbered group to zoom.';
  draw();
}
function focusTerminal(terminal) {
  view.x = terminal.x;
  view.y = terminal.y;
  view.scale = Math.max(view.scale, 5);
  selectTerminal(terminal);
}
$('terminal-choice').onchange = () => selectTerminal((network?.terminals || []).find(terminal => terminal.id === $('terminal-choice').value) || null);
$('terminal-focus').onclick = () => { if (selectedTerminal) focusTerminal(selectedTerminal); };
function drawTerminals() {
  terminalGroups = [];
  for (const terminal of network.terminals || []) {
    const position = screen(terminal.x, terminal.y);
    if (position.x < -20 || position.y < -20 || position.x > width + 20 || position.y > height + 20) continue;
    const group = terminalGroups.find(item => item.role === terminal.role && Math.hypot(item.x - position.x, item.y - position.y) < 14);
    if (group) {
      const count = group.terminals.length;
      group.x = (group.x * count + position.x) / (count + 1);
      group.y = (group.y * count + position.y) / (count + 1);
      group.terminals.push(terminal);
    } else terminalGroups.push({x: position.x, y: position.y, role: terminal.role, terminals: [terminal]});
  }
  for (const group of terminalGroups) {
    const grouped = group.terminals.length > 1;
    const radius = grouped ? 9 : 5;
    const selected = group.terminals.includes(selectedTerminal);
    context.fillStyle = group.role === 'entry' ? '#63d8b1' : '#ed91ce';
    context.strokeStyle = '#181716';
    context.lineWidth = 2;
    context.beginPath();
    if (group.role === 'entry' || grouped) context.arc(group.x, group.y, radius, 0, Math.PI * 2);
    else {
      context.moveTo(group.x, group.y - radius - 1);
      context.lineTo(group.x + radius + 1, group.y);
      context.lineTo(group.x, group.y + radius + 1);
      context.lineTo(group.x - radius - 1, group.y);
      context.closePath();
    }
    context.fill(); context.stroke();
    if (grouped) {
      context.fillStyle = '#181716'; context.font = 'bold 10px system-ui';
      context.textAlign = 'center'; context.textBaseline = 'middle';
      context.fillText(String(group.terminals.length), group.x, group.y);
    }
    if (selected) {
      context.strokeStyle = '#fff5df'; context.lineWidth = 2;
      context.beginPath(); context.arc(group.x, group.y, radius + 4, 0, Math.PI * 2); context.stroke();
    }
  }
}
function terminalAt(event) {
  const rectangle = canvas.getBoundingClientRect();
  const x = event.clientX - rectangle.left, y = event.clientY - rectangle.top;
  let closest = null, distance = 15;
  for (const group of terminalGroups) {
    const next = Math.hypot(group.x - x, group.y - y);
    if (next < distance) { closest = group; distance = next; }
  }
  return closest;
}
function hoverTerminal(event) {
  const group = terminalAt(event);
  const tooltip = $('terminal-tooltip');
  tooltip.hidden = !group;
  canvas.style.cursor = group ? 'pointer' : 'grab';
  if (!group) return;
  tooltip.textContent = group.terminals.length === 1 ? terminalDescription(group.terminals[0]) : `${group.role === 'entry' ? 'Entry' : 'Exit'} · ${group.terminals.length} lanes · click to zoom`;
  tooltip.style.left = `${Math.max(8, Math.min(width - Math.min(280, width - 16) - 8, group.x + 14))}px`;
  tooltip.style.top = `${Math.max(8, Math.min(height - 64, group.y + 14))}px`;
}
function fit() {
  if (!network?.lanes.length) return;
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const lane of network.lanes) for (const point of lane.shape) {
    minX = Math.min(minX, point.x); minY = Math.min(minY, point.y);
    maxX = Math.max(maxX, point.x); maxY = Math.max(maxY, point.y);
  }
  view = {x: (minX + maxX) / 2, y: (minY + maxY) / 2, scale: Math.min(width / Math.max(1, maxX - minX), height / Math.max(1, maxY - minY)) * 0.82};
  draw();
}
function draw() {
  context.clearRect(0, 0, width, height);
  if (!network) return;
  context.lineCap = 'round'; context.lineJoin = 'round';
  for (const lane of network.lanes) {
    context.strokeStyle = lane.id.startsWith(':') ? '#4d4842' : '#82776a';
    context.lineWidth = Math.max(1, Math.min(9, 3.2 * view.scale));
    context.beginPath();
    lane.shape.forEach((point, index) => {
      const position = screen(point.x, point.y);
      if (index) context.lineTo(position.x, position.y);
      else context.moveTo(position.x, position.y);
    });
    context.stroke();
  }
  for (const car of snapshot?.vehicles || []) {
    const position = screen(car.x, car.y);
    if (position.x < -20 || position.y < -20 || position.x > width + 20 || position.y > height + 20) continue;
    const size = Math.max(5, Math.min(15, 4.5 * view.scale));
    context.save();
    context.translate(position.x, position.y);
    context.rotate(car.angle * Math.PI / 180);
    context.fillStyle = car.speed_mps < 0.1 ? '#efa957' : '#5b94ff';
    context.fillRect(-size * 0.3, -size * 0.5, size * 0.6, size);
    context.restore();
  }
  drawTerminals();
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
canvas.onpointerdown = event => { drag = {x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, moved: false}; $('terminal-tooltip').hidden = true; canvas.setPointerCapture(event.pointerId); };
canvas.onpointermove = event => {
  if (!drag) { hoverTerminal(event); return; }
  view.x -= (event.clientX - drag.x) / view.scale;
  view.y += (event.clientY - drag.y) / view.scale;
  drag.moved ||= Math.hypot(event.clientX - drag.startX, event.clientY - drag.startY) > 4;
  drag.x = event.clientX; drag.y = event.clientY; draw();
};
canvas.onpointerup = event => {
  const clicked = drag && !drag.moved;
  drag = null;
  if (!clicked) return;
  const group = terminalAt(event);
  if (!group) return;
  if (group.terminals.length === 1) selectTerminal(group.terminals[0]);
  else {
    view.x = group.terminals.reduce((sum, terminal) => sum + terminal.x, 0) / group.terminals.length;
    view.y = group.terminals.reduce((sum, terminal) => sum + terminal.y, 0) / group.terminals.length;
    view.scale = Math.min(30, Math.max(view.scale * 2, 5));
    draw();
  }
};
canvas.onpointercancel = () => { drag = null; };
canvas.onpointerleave = () => { $('terminal-tooltip').hidden = true; };
canvas.addEventListener('wheel', event => {
  event.preventDefault();
  $('terminal-tooltip').hidden = true;
  const rectangle = canvas.getBoundingClientRect();
  const x = event.clientX - rectangle.left - width / 2, y = event.clientY - rectangle.top - height / 2;
  const old = view.scale;
  view.scale = Math.max(0.005, Math.min(30, old * Math.exp(-event.deltaY * 0.001)));
  view.x += x / old - x / view.scale; view.y -= y / old - y / view.scale;
  draw();
}, {passive: false});
async function initialize() {
  await refreshProjects();
  const runs = await api('/city-runs');
  const existing = runs.find(status => ['running', 'paused'].includes(status.state));
  if (existing) await openRun(existing);
  else if (projects.length) {
    const requested = new URLSearchParams(window.location.search).get('project');
    if (projects.some(project => project.manifest.id === requested)) $('project').value = requested;
    await openProject($('project').value);
  }
  initialized = true;
  controls();
}
async function poll() {
  try {
    if (!busy) {
      if (!initialized) await initialize();
      else await refresh(generation);
      $('connection').textContent = 'Connected';
      $('connection').dataset.connected = 'true';
    }
  } catch (error) {
    $('connection').textContent = 'Connection interrupted';
    $('connection').dataset.connected = 'false';
    showError(error);
  } finally { setTimeout(poll, document.hidden ? 1000 : 250); }
}
controls();
poll();
