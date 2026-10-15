const $ = id => document.getElementById(id);
const canvas = $('map'), context = canvas.getContext('2d');
let project = null, roads = new Map(), dataset = null, path = [], editingId = null;
let view = {x: 0, y: 0, scale: 1}, drag = null, activeJob = null;
let jsonDirty = false, routeDirty = false, saving = false;

function status(id, text) { if ($(id)) $(id).textContent = text; }
function announce(name) { window.dispatchEvent(new CustomEvent(`trainer:${name}`)); }
function updateControls() {
  for (const id of ['start-edge', 'turns', 'route-id', 'start-time', 'end-time', 'rate', 'undo', 'clear-path', 'save-route', 'repeat', 'dataset-file', 'export-dataset', 'dataset-json', 'apply-json', 'revert-json', 'export-id', 'export-name', 'epochs', 'seed', 'fit']) {
    if ($(id)) $(id).disabled = !project || saving;
  }
  $('train').disabled = !project || saving || Boolean(activeJob) || (!dataset.routes.length && !jsonDirty);
  $('train').classList.toggle('primary', $('download').hidden);
  $('train').textContent = $('download').hidden ? 'Train on CPU' : 'Train again';
  $('project-file').disabled = saving || Boolean(activeJob);
  $('undo').disabled = !project || saving || !path.length || !turnListSafe().length;
  for (const button of $('next-roads').querySelectorAll('button')) button.disabled = saving;
  status('editor-status', `${editingId ? `Editing ${editingId}` : 'New route'}${routeDirty ? ' · unsaved changes' : ''}`);
  status('json-status', jsonDirty ? 'Unapplied changes · apply or discard before editing the saved dataset.' : 'JSON matches the saved dataset.');
  if ($('revert-json')) $('revert-json').disabled = !project || saving || !jsonDirty;
  for (const row of $('route-list').children) {
    const selected = row.dataset.routeId === editingId;
    row.classList.toggle('selected', selected);
    row.querySelector('button')?.setAttribute('aria-pressed', String(selected));
    for (const button of row.querySelectorAll('button')) button.disabled = saving;
  }
}
function turnListSafe() { try { return turnList(); } catch { return []; } }
function requireCleanJson() {
  if (jsonDirty) throw new Error('Apply or discard your JSON changes before changing the saved dataset.');
}
function nextRouteId() {
  let index = 1;
  while (dataset.routes.some(row => row.id === `route-${index}`)) index++;
  return `route-${index}`;
}
function newRoute() {
  editingId = null; routeDirty = false;
  $('start-edge').value = ''; $('turns').value = '';
  $('route-id').value = nextRouteId();
  $('save-route').textContent = 'Save new route';
  updatePath();
}

function message(text, error = false) {
  $('message').textContent = text;
  $('message').classList.toggle('error', error);
}
async function api(url, method = 'GET', body) {
  const response = await fetch(url, {method, headers: body instanceof FormData ? {} : {'Content-Type': 'application/json'}, body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body)});
  const result = await response.json();
  if (!response.ok) throw new Error(typeof result.detail === 'string' ? result.detail : JSON.stringify(result.detail));
  return result;
}
function safely(action) { return async (...args) => {try {await action(...args);} catch (error) {message(error.message, true);}}; }
function requireProject() { if (!project) throw new Error('Open a project first.'); }
function clock(seconds) {return `${String(Math.floor(seconds / 3600)).padStart(2, '0')}:${String(Math.floor(seconds % 3600 / 60)).padStart(2, '0')}`;}
function seconds(text) {
  if (!/^(?:[01]\d|2[0-3]):[0-5]\d$/.test(text) && text !== '24:00') throw new Error('Use HH:MM from 00:00 through 24:00.');
  const [hour, minute] = text.split(':').map(Number);
  return hour * 3600 + minute * 60;
}
function turnList() {
  const text = $('turns').value.trim();
  if (!text) return [];
  return text.split(/[\s,\-]+/).map(value => {if (!/^\d+$/.test(value)) throw new Error('Use nonnegative turn indices separated by commas, spaces, or hyphens.'); return Number(value);});
}
function replay(start, turns) {
  if (!roads.has(start)) throw new Error(`Unknown starting road: ${start}`);
  const result = [start];
  for (const turn of turns) {
    const next = roads.get(result.at(-1)).next_edge_ids[turn];
    if (next === undefined) throw new Error(`Turn ${turn} is unavailable on ${result.at(-1)}.`);
    result.push(next);
  }
  return result;
}
function updatePath() {
  requireProject();
  try {
    path = $('start-edge').value.trim() ? replay($('start-edge').value.trim(), turnList()) : [];
  } catch (error) {
    path = [];
    $('route-path').textContent = error.message;
    $('next-roads').replaceChildren();
    delete $('next-roads').dataset.current;
    status('path-status', 'Fix the starting road or turn indices to continue.');
    draw(); updateControls();
    throw error;
  }
  $('route-path').textContent = path.length ? path.join(' → ') : 'No path selected';
  const current = path.at(-1) || '';
  if ($('next-roads').dataset.current !== current) {
    $('next-roads').dataset.current = current;
    $('next-roads').replaceChildren();
    for (const [turn, edge] of (roads.get(current)?.next_edge_ids || []).entries()) {
      const button = document.createElement('button');
      button.textContent = `${turn} → ${edge}`;
      button.onclick = safely(() => appendTurn(turn));
      $('next-roads').append(button);
    }
  }
  const choices = roads.get(current)?.next_edge_ids.length || 0;
  status('path-status', path.length ? `${path.length} road${path.length === 1 ? '' : 's'} · ${choices ? `${choices} legal next choices` : 'No outgoing roads'} · save to finish here` : 'Click any road to choose a starting point.');
  draw(); updateControls();
}
function appendTurn(turn) {
  if (saving) throw new Error('Wait for the dataset to finish saving before changing the route.');
  routeDirty = true;
  $('turns').value = [...turnList(), turn].join(', ');
  updatePath();
}
function syncDataset() {
  $('dataset-json').value = JSON.stringify(dataset, null, 2);
  jsonDirty = false;
  $('repeat').checked = dataset.repeat_daily;
  $('route-list').replaceChildren();
  for (const row of dataset.routes) {
    const container = document.createElement('div');
    container.className = 'route-row';
    container.dataset.routeId = row.id;
    const select = document.createElement('button');
    select.append(document.createTextNode(row.id));
    const detail = document.createElement('small');
    detail.textContent = `${clock(row.start_seconds)}–${clock(row.end_seconds)} · ${row.vehicles_per_hour}/h · ${row.start_edge}`;
    select.append(detail);
    select.onclick = safely(() => {
      editingId = row.id;
      routeDirty = false;
      $('route-id').value = row.id; $('start-edge').value = row.start_edge;
      $('start-time').value = clock(row.start_seconds); $('end-time').value = clock(row.end_seconds);
      $('rate').value = row.vehicles_per_hour; $('turns').value = row.turns.join(', ');
      $('save-route').textContent = 'Save route changes';
      updatePath();
      announce('edit-route');
    });
    const remove = document.createElement('button');
    remove.className = 'remove'; remove.textContent = 'Remove';
    remove.setAttribute('aria-label', `Remove ${row.id}`);
    remove.onclick = safely(async () => {requireCleanJson(); const candidate = structuredClone(dataset); candidate.routes = candidate.routes.filter(item => item.id !== row.id); await persist(candidate); if (editingId === row.id) newRoute(); message(`Removed ${row.id}.`);});
    container.append(select, remove); $('route-list').append(container);
  }
  if (!dataset.routes.length) {
    const empty = document.createElement('p');
    empty.className = 'hint'; empty.textContent = 'No routes yet. Draw a path on the map or import a dataset JSON.';
    $('route-list').append(empty);
  }
  let expected = 0;
  for (const row of dataset.routes) expected += row.vehicles_per_hour * (row.end_seconds - row.start_seconds) / 3600;
  status('dataset-summary', `${dataset.routes.length} route${dataset.routes.length === 1 ? '' : 's'} · ${expected.toLocaleString(undefined, {maximumFractionDigits: 1})} expected vehicles per ${dataset.repeat_daily ? 'day' : 'schedule'}`);
  updateControls();
}
async function persist(candidate = dataset) {
  requireProject();
  if (saving) throw new Error('The dataset is being saved. Try again in a moment.');
  saving = true; updateControls();
  try {
    const result = await api(`/api/projects/${project.id}/dataset`, 'PUT', candidate);
    dataset = candidate;
    syncDataset();
    return result;
  } finally { saving = false; updateControls(); }
}
function downloadJson(value, name) {
  const url = URL.createObjectURL(new Blob([JSON.stringify(value, null, 2)], {type: 'application/json'}));
  const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function openProject(value) {
  project = value;
  localStorage.setItem('streetflow-trainer-project', project.id);
  roads = new Map(project.network.roads.map(road => [road.edge_id, road]));
  dataset = project.dataset; path = []; editingId = null;
  jsonDirty = false; routeDirty = false;
  $('roads').replaceChildren(...[...roads.keys()].map(id => new Option(id, id)));
  $('start-edge').value = ''; $('turns').value = '';
  $('map-title').textContent = `${project.manifest.name} · ${roads.size} roads`;
  status('project-name', project.manifest.name);
  $('export-id').value = `${project.manifest.id}-neural`;
  $('export-name').value = `${project.manifest.name} · Neural routes`;
  $('download').hidden = true; $('report-panel').hidden = true;
  $('training-status').textContent = 'Save at least one route, then train a snapshot of your dataset.';
  status('job-state', 'Ready');
  syncDataset(); newRoute(); fit();
  announce('project-opened');
  message('Map ready. Create routes manually or import a dataset for this network.');
}
$('project-file').onchange = safely(async event => {
  if (activeJob) throw new Error('Wait for the current training job before opening another project.');
  const file = event.target.files[0]; if (!file) return;
  const form = new FormData(); form.append('file', file);
  message('Opening map…');
  openProject(await api('/api/projects', 'POST', form));
  event.target.value = '';
});
$('start-edge').onchange = safely(updatePath);
$('turns').onchange = safely(updatePath);
for (const id of ['route-id', 'start-edge', 'start-time', 'end-time', 'rate', 'turns']) {
  $(id).addEventListener('input', () => {routeDirty = true; updateControls();});
}
$('undo').onclick = safely(() => {routeDirty = true; $('turns').value = turnList().slice(0, -1).join(', '); updatePath();});
$('clear-path').onclick = safely(() => {
  requireProject(); newRoute();
  message('New route. Time span and traffic volume are kept for faster authoring.');
});
$('save-route').onclick = safely(async () => {
  requireProject(); requireCleanJson(); updatePath();
  if (!path.length) throw new Error('Choose a starting road.');
  const row = {id: $('route-id').value.trim(), start_seconds: seconds($('start-time').value), end_seconds: seconds($('end-time').value), start_edge: path[0], turns: turnList(), vehicles_per_hour: Number($('rate').value)};
  if (!row.id) throw new Error('Give this route a name before saving.');
  if (row.end_seconds <= row.start_seconds) throw new Error('The end time must follow the start time. Split overnight traffic into two routes.');
  if (!Number.isFinite(row.vehicles_per_hour) || row.vehicles_per_hour <= 0) throw new Error('Vehicles per hour must be greater than zero.');
  const candidate = structuredClone(dataset);
  if (candidate.routes.some(item => item.id === row.id && item.id !== editingId)) throw new Error('Use a unique route ID.');
  const index = candidate.routes.findIndex(item => item.id === editingId);
  if (index >= 0) candidate.routes[index] = row; else candidate.routes.push(row);
  candidate.repeat_daily = $('repeat').checked;
  await persist(candidate); editingId = row.id; routeDirty = false;
  $('save-route').textContent = 'Save route changes'; updateControls();
  message(`Saved ${row.id}. Server validated the continuous lane path.`);
});
$('dataset-json').addEventListener('input', () => {jsonDirty = $('dataset-json').value !== JSON.stringify(dataset, null, 2); updateControls();});
if ($('revert-json')) $('revert-json').onclick = () => {syncDataset(); message('JSON editor restored to the saved dataset.');};
$('apply-json').onclick = safely(async () => {await persist(JSON.parse($('dataset-json').value)); newRoute(); message('Dataset validated and saved.');});
$('dataset-file').onchange = safely(async event => {
  const file = event.target.files[0]; if (!file) return;
  try {requireCleanJson(); await persist(JSON.parse(await file.text())); newRoute(); message('Dataset imported and validated.');}
  finally {event.target.value = '';}
});
$('export-dataset').onclick = safely(() => {requireProject(); downloadJson(JSON.parse($('dataset-json').value), 'route-dataset.json');});
$('repeat').onchange = safely(async () => {
  if (!dataset) return;
  try {requireCleanJson(); const candidate = structuredClone(dataset); candidate.repeat_daily = $('repeat').checked; await persist(candidate);}
  catch (error) {$('repeat').checked = dataset.repeat_daily; throw error;}
});
$('train').onclick = safely(async () => {
  requireProject();
  if (activeJob) throw new Error('A training job is already running.');
  if (routeDirty) throw new Error('Save your route changes before training, or choose New route to discard them.');
  await persist(JSON.parse($('dataset-json').value));
  const result = await api(`/api/projects/${project.id}/train`, 'POST', {epochs: Number($('epochs').value), seed: Number($('seed').value), project_id: $('export-id').value.trim(), project_name: $('export-name').value.trim()});
  activeJob = result.job_id; $('download').hidden = true; $('report-panel').hidden = true;
  status('job-state', 'Training'); updateControls(); announce('training-started');
  message('Training a snapshot of the saved dataset. Editing it now will not change this job.');
  poll();
});
async function poll() {
  try {
    const job = await api(`/api/jobs/${activeJob}`);
    $('training-status').textContent = job.epoch ? `Epoch ${job.epoch}/${job.epochs} · training loss ${job.training_loss.toFixed(4)}` : job.state;
    if (job.state === 'complete') {
      $('download').href = job.download; $('download').hidden = false;
      $('report').textContent = JSON.stringify(job.report, null, 2); $('report-panel').hidden = false;
      $('training-status').textContent = `${job.report.parameter_count.toLocaleString()} parameters · ${job.report.routes_per_second.toFixed(0)} sampled routes/s on this CPU · ${job.report.failed_routes} sample failures`;
      message('Training complete. Download the v3 ZIP and import it into the simulator.');
      status('job-state', 'Complete');
    } else if (job.state === 'failed') {throw new Error(job.error);} else {setTimeout(poll, 1000); return;}
  } catch (error) {message(error.message, true); $('training-status').textContent = 'Training or status request failed.'; status('job-state', 'Failed');}
  activeJob = null; updateControls();
}

function visibleMapArea() {
  const rect = canvas.getBoundingClientRect();
  const area = {left: 24, right: canvas.clientWidth - 24, top: 88, bottom: canvas.clientHeight - 60};
  for (const element of document.querySelectorAll('.ui-window')) {
    if (element.hidden || getComputedStyle(element).visibility === 'hidden') continue;
    const panel = element.getBoundingClientRect();
    if (panel.top >= rect.bottom || panel.bottom <= rect.top) continue;
    const left = panel.left - rect.left, right = panel.right - rect.left;
    if (left < rect.width * .25 && right < rect.width * .5) area.left = Math.max(area.left, right + 24);
    if (left > rect.width * .5 && right > rect.width * .75) area.right = Math.min(area.right, left - 24);
  }
  if (area.right - area.left < 160) {area.left = 24; area.right = canvas.clientWidth - 24;}
  return area;
}
function fit() {
  const points = [...roads.values()].flatMap(road => road.shape);
  if (!points.length) return;
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
  for (const [x, y] of points) {minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y);}
  const area = visibleMapArea();
  view.scale = Math.min(Math.max(1, area.right - area.left) / Math.max(1, maxX - minX), Math.max(1, area.bottom - area.top) / Math.max(1, maxY - minY));
  view.x = (area.left + area.right) / 2 - (minX + maxX) / 2 * view.scale;
  view.y = (area.top + area.bottom) / 2 + (minY + maxY) / 2 * view.scale; draw();
}
function screen(point) {return [point[0] * view.scale + view.x, -point[1] * view.scale + view.y];}
function draw() {
  const ratio = devicePixelRatio || 1;
  canvas.width = Math.round(canvas.clientWidth * ratio); canvas.height = Math.round(canvas.clientHeight * ratio);
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
  const selected = new Set(path), current = path.at(-1), next = new Set(roads.get(current)?.next_edge_ids || []);
  for (const road of roads.values()) {
    if (road.shape.length < 2) continue;
    context.strokeStyle = road.edge_id === current ? '#f4bf82' : selected.has(road.edge_id) ? '#b9a0ff' : next.has(road.edge_id) ? '#e394dc' : '#625a77';
    context.lineWidth = selected.has(road.edge_id) || next.has(road.edge_id) ? 5 : 2;
    context.beginPath();
    road.shape.forEach((point, index) => {const [x, y] = screen(point); if (index === 0) context.moveTo(x, y); else context.lineTo(x, y);});
    context.stroke();
    const middle = Math.floor(road.shape.length / 2), [x, y] = screen(road.shape[middle]);
    const before = screen(road.shape[Math.max(0, middle - 1)]);
    const angle = Math.atan2(y - before[1], x - before[0]);
    context.beginPath(); context.moveTo(x, y); context.lineTo(x - 8 * Math.cos(angle - .5), y - 8 * Math.sin(angle - .5)); context.moveTo(x, y); context.lineTo(x - 8 * Math.cos(angle + .5), y - 8 * Math.sin(angle + .5)); context.stroke();
    if (selected.has(road.edge_id) || next.has(road.edge_id) || roads.size < 25) {context.fillStyle = '#e8dff5'; context.font = '11px system-ui'; context.fillText(road.edge_id, x + 7, y - 7);}
  }
}
function nearest(x, y) {
  let nearestRoad = null, distance = 14;
  for (const road of roads.values()) {
    for (let i = 1; i < road.shape.length; i++) {
      const [ax, ay] = screen(road.shape[i - 1]), [bx, by] = screen(road.shape[i]);
      const dx = bx - ax, dy = by - ay;
      const t = Math.max(0, Math.min(1, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy || 1)));
      const value = Math.hypot(x - ax - t * dx, y - ay - t * dy);
      if (value < distance) {distance = value; nearestRoad = road;}
    }
  }
  return nearestRoad;
}
canvas.onpointerdown = event => {drag = {x: event.clientX, y: event.clientY, originX: view.x, originY: view.y, moved: false}; canvas.setPointerCapture(event.pointerId);};
canvas.onpointermove = event => {if (!drag) return; const dx = event.clientX - drag.x, dy = event.clientY - drag.y; drag.moved ||= Math.hypot(dx, dy) > 4; if (drag.moved) {view.x = drag.originX + dx; view.y = drag.originY + dy; draw();}};
canvas.onpointerup = safely(event => {
  if (!drag) return;
  const click = !drag.moved; drag = null;
  if (!click || saving) return;
  const rect = canvas.getBoundingClientRect(), road = nearest(event.clientX - rect.left, event.clientY - rect.top);
  if (!road) return;
  if (!path.length) {routeDirty = true; $('start-edge').value = road.edge_id; updatePath(); return;}
  const turn = roads.get(path.at(-1)).next_edge_ids.indexOf(road.edge_id);
  if (turn < 0) throw new Error('Choose a highlighted next road, or start a new route.');
  appendTurn(turn);
  announce('edit-route');
});
canvas.onpointercancel = () => {drag = null;};
canvas.onwheel = event => {event.preventDefault(); const rect = canvas.getBoundingClientRect(), x = event.clientX - rect.left, y = event.clientY - rect.top; const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15; view.x = x - (x - view.x) * factor; view.y = y - (y - view.y) * factor; view.scale *= factor; draw();};
$('fit').onclick = fit;
window.addEventListener('trainer:fit-map', fit);
new ResizeObserver(() => {if (roads.size) fit(); else draw();}).observe(canvas);
updateControls();
const lastProject = localStorage.getItem('streetflow-trainer-project');
if (lastProject) {
  api(`/api/projects/${encodeURIComponent(lastProject)}`).then(openProject).catch(() => localStorage.removeItem('streetflow-trainer-project'));
}
