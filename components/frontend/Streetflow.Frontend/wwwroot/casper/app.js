import {MapView} from '/balthasar/map-view.js?v=nerv-1';
import {createWorkspace} from '/ui/windows.js?v=shared-ui-2';

const $ = id => document.getElementById(id);
const canvas = $('map');
const requested = new URLSearchParams(location.search);
let session = null;
let network = null;
let traffic = null;
let states = [];
let cameras = [];
let cameraMarkers = [];
let selected = requested.has('camera') ? {kind: 'camera', id: requested.get('camera')}
  : requested.has('light') ? {kind: 'light', id: requested.get('light')} : null;
let hovered = null;

const workspace = createWorkspace({
  root: $('casper-workspace'),
  layer: $('applet-layer'),
  storageKey: 'streetflow.casper.workspace.v1',
  taskbar: $('taskbar'),
  launchers: document.querySelectorAll('[data-open-applet]'),
  resetButton: $('reset-layout'),
  focusButton: $('focus-view'),
  announcement: $('workspace-announcement'),
  stackedMedia: '(max-width: 760px)',
  initialLayout(name, size) {
    const width = name === 'devices' ? 300 : 370;
    return {
      rectangle: {x: name === 'devices' ? 0 : size.width - width, y: 0, width, height: Math.min(name === 'devices' ? 410 : 620, size.height)},
      open: true
    };
  }
});
for (const content of document.querySelectorAll('[data-applet]')) {
  workspace.registerApplet({id: content.dataset.applet, title: content.dataset.title, category: content.dataset.category, icon: content.dataset.icon, content});
}

const mapView = new MapView(canvas, {
  deviceStates: () => states,
  focusedDevice: () => hovered?.kind === 'light' ? network?.devices.find(light => light.id === hovered.id) : null,
  markerActive: light => selectedLight()?.id === light.id || hovered?.kind === 'light' && hovered.id === light.id,
  afterVehicles: context => drawCameras(context),
  onHover: event => {
    const next = markerAt(event);
    canvas.style.cursor = next ? 'pointer' : 'grab';
    if (next?.kind !== hovered?.kind || next?.id !== hovered?.id) {
      hovered = next;
      mapView.draw();
    }
  },
  onLeave: () => {
    hovered = null;
    canvas.style.cursor = 'grab';
    mapView.draw();
  },
  onClick: event => {
    const marker = markerAt(event);
    if (marker) {
      select(marker.kind, marker.id);
    }
  }
});

async function getJson(path) {
  const response = await fetch(path, {cache: 'no-store', signal: AbortSignal.timeout(5000)});
  const data = await response.json();
  if (!response.ok) {
    throw new Error(data.message || 'Casper is unavailable.');
  }
  return data;
}

function sharesLane(first, second) {
  for (const lane of first.lane_ids) {
    if (second.lane_ids.includes(lane)) {
      return true;
    }
  }
  return false;
}

function relatedLight(camera) {
  if (!camera || !network) {
    return null;
  }
  for (const light of network.devices) {
    if (sharesLane(light, camera)) {
      return light;
    }
  }
  return null;
}

function selectedLight() {
  if (!selected || !network) {
    return null;
  }
  if (selected.kind === 'light') {
    return network.devices.find(light => light.id === selected.id) || null;
  }
  return relatedLight(cameras.find(camera => camera.id === selected.id));
}

function camerasForLight(light) {
  const result = [];
  if (!light) {
    return result;
  }
  for (const camera of cameras) {
    if (sharesLane(light, camera)) {
      result.push(camera);
    }
  }
  return result;
}

function activeCamera() {
  if (!selected) {
    return null;
  }
  if (selected.kind === 'camera') {
    return cameras.find(camera => camera.id === selected.id) || null;
  }
  return camerasForLight(selectedLight())[0] || null;
}

function makeCameraMarkers() {
  cameraMarkers = [];
  if (!network) {
    return;
  }
  for (const camera of cameras) {
    let point = null;
    for (const detector of network.detectors || []) {
      if (sharesLane(detector, camera)) {
        point = detector;
        break;
      }
    }
    point ||= relatedLight(camera);
    if (point) {
      const nearby = cameraMarkers.filter(marker => marker.point.x === point.x && marker.point.y === point.y).length;
      cameraMarkers.push({camera, point, offset: nearby});
    }
  }
}

function markerPosition(marker) {
  const anchor = mapView.screen(marker.point.x, marker.point.y);
  return {x: anchor.x - 28 - marker.offset * 32, y: anchor.y - 18};
}

function drawCameras(context) {
  for (const marker of cameraMarkers) {
    const {x, y} = markerPosition(marker);
    if (x < 20 || y < 20 || x > mapView.width - 20 || y > mapView.height - 20) {
      continue;
    }
    const active = selected?.kind === 'camera' && selected.id === marker.camera.id;
    context.strokeStyle = active || hovered?.kind === 'camera' && hovered.id === marker.camera.id ? '#5cff7a' : '#73dacb';
    context.fillStyle = '#050201';
    context.lineWidth = 2;
    context.beginPath();
    context.roundRect(x - 13, y - 9, 26, 19, 3);
    context.fill();
    context.stroke();
    context.beginPath();
    context.arc(x, y, 5, 0, Math.PI * 2);
    context.stroke();
    context.fillStyle = '#73dacb';
    context.fillRect(x - 7, y - 13, 11, 4);
    const number = cameras.indexOf(marker.camera) + 1;
    context.font = '12px system-ui';
    context.fillText('Camera ' + number, x - 24, y + 26);
  }
}

function markerAt(event) {
  const bounds = canvas.getBoundingClientRect();
  const x = event.clientX - bounds.left;
  const y = event.clientY - bounds.top;
  for (const marker of cameraMarkers) {
    const position = markerPosition(marker);
    if (Math.hypot(position.x - x, position.y - y) < 18) {
      return {kind: 'camera', id: marker.camera.id};
    }
  }
  const light = mapView.signalAt(event);
  return light ? {kind: 'light', id: light.point.id} : null;
}

function select(kind, id) {
  selected = {kind, id};
  const query = kind === 'camera' ? '?camera=' : '?light=';
  history.replaceState(null, '', '/casper/' + query + encodeURIComponent(id));
  renderDevices();
  renderSelected();
  workspace.show('camera', false);
  mapView.draw();
}

function countFor(laneIds, field) {
  if (!traffic) {
    return null;
  }
  let total = 0;
  for (const id of laneIds) {
    const lane = traffic.lanes.find(item => item.lane_id === id);
    if (!lane || lane[field] === null) {
      return null;
    }
    total += lane[field];
  }
  return total;
}

function renderCounts(laneIds) {
  const visible = countFor(laneIds, 'visible_vehicle_count');
  const waiting = countFor(laneIds, 'queued_vehicle_count');
  $('device-counts').replaceChildren();
  for (const [label, value] of [['Cars in view', visible], ['May be waiting', waiting]]) {
    const item = document.createElement('div');
    const name = document.createElement('span');
    const count = document.createElement('strong');
    name.textContent = label;
    count.textContent = value ?? '—';
    item.append(name, count);
    $('device-counts').append(item);
  }
  $('device-counts').hidden = false;
}

function renderDevices() {
  $('device-list').replaceChildren();
  if (!network) {
    return;
  }
  for (const light of network.devices) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = '🚦 ' + light.label;
    button.setAttribute('aria-pressed', selected?.kind === 'light' && selected.id === light.id);
    button.onclick = () => select('light', light.id);
    $('device-list').append(button);
  }
  for (let index = 0; index < cameras.length; index++) {
    const camera = cameras[index];
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = '▣ Camera ' + (index + 1);
    button.setAttribute('aria-pressed', selected?.kind === 'camera' && selected.id === camera.id);
    button.onclick = () => select('camera', camera.id);
    $('device-list').append(button);
  }
}

function renderCameraChoices(light) {
  const choices = $('camera-choices');
  choices.replaceChildren();
  if (selected?.kind !== 'light') {
    choices.hidden = true;
    return;
  }
  for (const camera of camerasForLight(light)) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = 'Open Camera ' + (cameras.indexOf(camera) + 1);
    button.onclick = () => select('camera', camera.id);
    choices.append(button);
  }
  choices.hidden = choices.childElementCount === 0;
}

function renderSelected() {
  const light = selectedLight();
  const camera = activeCamera();
  if (!selected || !network || !light && !camera) {
    $('device-kind').textContent = 'NO DEVICE SELECTED';
    $('device-heading').textContent = 'Choose a device';
    $('device-info').textContent = 'Traffic-light and camera icons appear on the map.';
    $('device-counts').hidden = true;
    $('camera-choices').hidden = true;
    $('camera-view').hidden = true;
    return;
  }
  const isCamera = selected.kind === 'camera';
  $('device-kind').textContent = isCamera ? 'CAMERA DETECTOR' : 'TRAFFIC LIGHT';
  $('device-heading').textContent = isCamera ? 'Camera ' + (cameras.indexOf(camera) + 1) : light.label;
  const lightState = light ? states.find(state => state.id === light.id)?.state : null;
  const lightStatus = lightState && lightState !== 'unknown' ? ' Light is ' + lightState + '.' : ' Light color is not reported by this demo.';
  $('device-info').textContent = isCamera
    ? light ? 'This camera watches the road at ' + light.label + '.' : 'This camera watches the selected road area.'
    : 'Traffic light with ' + camerasForLight(light).length + ' connected camera' + (camerasForLight(light).length === 1 ? '.' : 's.') + lightStatus;
  renderCounts(isCamera ? camera.lane_ids : light.lane_ids);
  renderCameraChoices(light);
  $('camera-view').hidden = !camera;
  if (!camera) {
    return;
  }
  $('adjust-camera').href = '/casper/calibration.html?camera=' + encodeURIComponent(camera.id)
    + (light ? '&light=' + encodeURIComponent(light.id) : '');
  if (camera.preview_status === 'unavailable') {
    $('preview-image').hidden = true;
    $('preview-status').textContent = 'Waiting for a picture from this camera.';
    return;
  }
  $('preview-status').textContent = 'Camera picture · ' + camera.preview_age_seconds.toFixed(1) + ' seconds old';
  $('preview-image').src = '/casper-camera-api/' + encodeURIComponent(camera.id) + '/preview?t=' + Date.now();
  $('preview-image').hidden = false;
}

async function poll() {
  try {
    const next = await getJson('/casper-api/session');
    const changed = !session || next.id !== session.id || next.revision !== session.revision;
    session = next;
    const path = '/casper-api/sessions/' + encodeURIComponent(next.id);
    const tasks = [getJson(path + '/traffic'), getJson('/casper-camera-api')];
    if (changed) {
      tasks.push(getJson(path + '/map'), getJson(path + '/devices'));
    }
    const result = await Promise.all(tasks);
    traffic = result[0];
    cameras = result[1];
    if (changed) {
      network = result[2];
      states = result[3];
      mapView.setNetwork({lanes: network.lanes, devices: network.devices});
      $('map-title').textContent = network.world_id === 'hackathon-intersection' ? 'Sample intersection' : network.world_id.replaceAll('-', ' ');
    }
    makeCameraMarkers();
    if (!selected && network.devices.length) {
      selected = {kind: 'light', id: network.devices[0].id};
    }
    renderDevices();
    renderSelected();
    mapView.draw();
    $('connection').dataset.connected = 'true';
    $('connection').textContent = network.devices.length + ' traffic light' + (network.devices.length === 1 ? '' : 's')
      + ' · ' + cameras.length + ' camera' + (cameras.length === 1 ? '' : 's');
    $('map-hint').textContent = 'Click a traffic light or camera to see its picture and car counts.';
    $('error').hidden = true;
  } catch (error) {
    $('connection').dataset.connected = 'false';
    $('connection').textContent = 'Casper unavailable';
    $('error').textContent = error.message;
    $('error').hidden = false;
  } finally {
    setTimeout(poll, 2000);
  }
}

$('fit-map').onclick = () => mapView.fit();
poll();
