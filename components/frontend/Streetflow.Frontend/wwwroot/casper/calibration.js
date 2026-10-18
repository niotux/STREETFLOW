const $ = id => document.getElementById(id);
const canvas = $('calibration-canvas');
const context = canvas.getContext('2d');
const requested = new URLSearchParams(location.search);
let calibration = null;
let picture = null;
let dragging = false;
let dragStart = null;
let dragEnd = null;

function cameraPath() {
  return '/casper-camera-api/' + encodeURIComponent($('camera').value);
}

function message(value) {
  $('feedback').textContent = value;
}

function currentLane() {
  return calibration?.lanes?.[0];
}

function draw() {
  context.clearRect(0, 0, canvas.width, canvas.height);
  if (picture) {
    context.drawImage(picture, 0, 0, canvas.width, canvas.height);
  }
  const area = currentLane()?.polygon;
  if (area?.length) {
    trace(area, true, '#73dacb');
  }
  if (dragStart && dragEnd) {
    trace(boxCorners(dragStart, dragEnd), true, '#fff');
  }
}

function trace(vertices, closed, color) {
  context.beginPath();
  for (let index = 0; index < vertices.length; index++) {
    const [x, y] = vertices[index];
    if (index === 0) {
      context.moveTo(x * canvas.width, y * canvas.height);
    } else {
      context.lineTo(x * canvas.width, y * canvas.height);
    }
  }
  if (closed) {
    context.closePath();
    context.fillStyle = '#73dacb33';
    context.fill();
  }
  context.strokeStyle = color;
  context.lineWidth = 4;
  context.stroke();
}

function boxCorners(first, second) {
  const left = Math.min(first[0], second[0]);
  const right = Math.max(first[0], second[0]);
  const top = Math.min(first[1], second[1]);
  const bottom = Math.max(first[1], second[1]);
  return [[left, top], [right, top], [right, bottom], [left, bottom]];
}

function pointFromEvent(event) {
  const bounds = canvas.getBoundingClientRect();
  const x = Math.max(0, Math.min(1, (event.clientX - bounds.left) / bounds.width));
  const y = Math.max(0, Math.min(1, (event.clientY - bounds.top) / bounds.height));
  return [x, y];
}

async function loadPicture() {
  const response = await fetch(cameraPath() + '/preview?t=' + Date.now(), {cache: 'no-store'});
  if (!response.ok) {
    message('Waiting for a picture. Start the sample demo, then try Refresh picture.');
    return;
  }
  picture = await createImageBitmap(await response.blob());
  canvas.width = picture.width;
  canvas.height = picture.height;
  draw();
  message('Ready. Drag a box over the road to change the counting area.');
}

async function loadCamera() {
  dragging = false;
  dragStart = null;
  dragEnd = null;
  calibration = null;
  picture = null;
  draw();
  try {
    const response = await fetch(cameraPath() + '/calibration', {cache: 'no-store'});
    const value = await response.json();
    if (!response.ok) {
      throw new Error(value.message || 'Could not load this camera.');
    }
    calibration = value;
    draw();
    await loadPicture();
  } catch (error) {
    message(error.message + ' Run the sample demo and reload this page.');
  }
}

async function start() {
  try {
    const response = await fetch('/casper-camera-api', {cache: 'no-store'});
    if (!response.ok) {
      throw new Error('Cannot connect to Casper.');
    }
    const cameras = await response.json();
    $('camera').replaceChildren(...cameras.map((camera, index) => new Option('Camera ' + (index + 1), camera.id)));
    if (cameras.some(camera => camera.id === requested.get('camera'))) {
      $('camera').value = requested.get('camera');
    }
    if (!cameras.length) {
      message('No cameras are set up yet. Start the sample demo.');
      return;
    }
    await loadCamera();
  } catch (error) {
    message(error.message + ' Start the sample demo and reload this page.');
  }
}

$('camera').onchange = loadCamera;
$('refresh-picture').onclick = async () => {
  try {
    await loadPicture();
  } catch (error) {
    message(error.message);
  }
};
$('start-over').onclick = loadCamera;
canvas.onpointerdown = event => {
  if (!currentLane()) {
    message('Wait for the camera to load first.');
    return;
  }
  dragging = true;
  dragStart = pointFromEvent(event);
  dragEnd = dragStart;
  canvas.setPointerCapture(event.pointerId);
};
canvas.onpointermove = event => {
  if (!dragging) {
    return;
  }
  dragEnd = pointFromEvent(event);
  draw();
};
canvas.onpointerup = event => {
  if (!dragging) {
    return;
  }
  dragging = false;
  dragEnd = pointFromEvent(event);
  const width = Math.abs(dragEnd[0] - dragStart[0]);
  const height = Math.abs(dragEnd[1] - dragStart[1]);
  if (width < 0.02 || height < 0.02) {
    message('Drag a larger box over the road.');
  } else {
    currentLane().polygon = boxCorners(dragStart, dragEnd);
    message('Box changed. Press Save changes to use it.');
  }
  dragStart = null;
  dragEnd = null;
  draw();
};
canvas.onpointercancel = () => {
  dragging = false;
  dragStart = null;
  dragEnd = null;
  draw();
};
$('save-camera').onclick = async () => {
  if (!calibration || dragging) {
    message('Wait for the camera picture to be ready.');
    return;
  }
  const updated = structuredClone(calibration);
  updated.detector_config_revision = 'camera-' + Date.now();
  $('save-camera').disabled = true;
  try {
    const response = await fetch(cameraPath() + '/calibration', {
      method: 'PUT',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify(updated)
    });
    const saved = await response.json();
    if (!response.ok) {
      throw new Error(saved.message || 'Could not save changes.');
    }
    calibration = saved;
    message('Saved. New camera counts will use this area in a few seconds.');
  } catch (error) {
    message(error.message);
  } finally {
    $('save-camera').disabled = false;
  }
};

if (requested.has('light')) {
  $('back-map').href = '/casper/?light=' + encodeURIComponent(requested.get('light'));
} else if (requested.has('camera')) {
  $('back-map').href = '/casper/?camera=' + encodeURIComponent(requested.get('camera'));
}
start();
