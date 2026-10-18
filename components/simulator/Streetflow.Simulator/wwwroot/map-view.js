import {layoutSignalMarkers, deviceState, controlledLanes} from './signals.js?v=physical-devices-3';
import {colors, paintFor, braking, laneKind, laneWidth, roadMarkings, sidewalkWidth, crossingWidth, vehicleLength, vehicleWidth} from './road-style.js?v=view-more-1';
import {loadThree, MapScene} from './map-3d.js?v=view-more-1';

const modeKey = 'streetflow.map-mode';

// One map renderer for Balthasar and Melchior: roads, controlled-lane highlight, vehicles and traffic lights.
// Callers supply vehicles as {id?, x, y, angle (degrees, SUMO heading), speed_mps, opacity?}.
// Mode '2d' draws everything on the canvas; '3d' renders roads and cars with three.js beneath it
// and keeps the canvas as a projected overlay for signals, highlights and caller drawings.
export class MapView {
  constructor(canvas, options = {}) {
    this.canvas = canvas;
    this.context = canvas.getContext('2d');
    this.options = options;
    this.network = null;
    this.view = {x: 0, y: 0, scale: 1};
    this.width = 1;
    this.height = 1;
    this.markers = [];
    this.roadCanvas = document.createElement('canvas');
    this.pedestrianCanvas = document.createElement('canvas');
    this.roadCache = null;
    this.pedestrianCache = null;
    this.markersDirty = true;
    this.drag = null;
    this.mode = '2d';
    this.scene = null;
    this.markings = null;
    new ResizeObserver(() => this.resize()).observe(canvas);
    canvas.addEventListener('pointerdown', event => this.pointerDown(event));
    canvas.addEventListener('pointermove', event => this.pointerMove(event));
    canvas.addEventListener('pointerup', event => this.pointerUp(event));
    canvas.addEventListener('pointercancel', () => { this.drag = null; });
    canvas.addEventListener('pointerleave', event => this.options.onLeave?.(event));
    canvas.addEventListener('wheel', event => this.wheel(event), {passive: false});
    canvas.addEventListener('contextmenu', event => { if (this.mode === '3d') event.preventDefault(); });
  }

  async setMode(mode) {
    if (mode === '3d' && !this.scene) {
      const THREE = await loadThree();
      const glCanvas = document.createElement('canvas');
      glCanvas.className = 'map-3d';
      glCanvas.setAttribute('aria-hidden', 'true');
      glCanvas.style.pointerEvents = 'none';
      this.canvas.before(glCanvas);
      this.scene = new MapScene(THREE, glCanvas);
      this.scene.resize(this.width, this.height, window.devicePixelRatio || 1);
      this.scene.setNetwork(this.network);
      this.scene.network = this.network;
    }
    this.mode = mode;
    if (this.scene) this.scene.canvas.style.display = mode === '3d' ? '' : 'none';
    this.markersDirty = true;
    this.draw();
  }

  // network: {lanes: [{id, shape, render_kind?}], devices: [{id, x, y, lane_ids}]}
  setNetwork(network) {
    this.network = network;
    this.markers = [];
    this.markersDirty = true;
    this.fit();
  }

  screen(x, y) {
    if (this.mode === '3d') return this.scene.project(x, y);
    return {x: this.width / 2 + (x - this.view.x) * this.view.scale, y: this.height / 2 - (y - this.view.y) * this.view.scale};
  }

  fit() {
    if (!this.network?.lanes.length) { this.draw(); return; }
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const lane of this.network.lanes) for (const point of lane.shape) {
      minX = Math.min(minX, point.x); minY = Math.min(minY, point.y);
      maxX = Math.max(maxX, point.x); maxY = Math.max(maxY, point.y);
    }
    this.view = {x: (minX + maxX) / 2, y: (minY + maxY) / 2, scale: Math.min(this.width / Math.max(1, maxX - minX), this.height / Math.max(1, maxY - minY)) * 0.82};
    this.draw();
  }

  zoom(factor) {
    this.view.scale = Math.max(0.005, Math.min(30, this.view.scale * factor));
    this.draw();
  }

  focus(x, y, scale) {
    this.view.x = x;
    this.view.y = y;
    this.view.scale = Math.min(30, Math.max(this.view.scale, scale));
    this.draw();
  }

  signalAt(event) {
    const rectangle = this.canvas.getBoundingClientRect();
    const x = event.clientX - rectangle.left, y = event.clientY - rectangle.top;
    let closest = null, distance = 18;
    for (const marker of this.markers) {
      const next = Math.hypot(marker.x - x, marker.y - y);
      if (next < distance) { closest = marker; distance = next; }
    }
    return closest;
  }

  draw(now = performance.now()) {
    const context = this.context;
    context.clearRect(0, 0, this.width, this.height);
    const cars = this.network ? this.options.vehicles?.(now) || [] : [];
    if (this.mode === '3d') {
      if (this.scene.network !== this.network) { this.scene.setNetwork(this.network); this.scene.network = this.network; }
      this.scene.render(this.view, cars, !!this.options.showPedestrians?.());
      this.markersDirty = true;
    }
    if (!this.network) return;
    if (this.mode === '2d') {
      this.drawRoads();
      context.drawImage(this.roadCanvas, 0, 0, this.roadCanvas.width, this.roadCanvas.height, 0, 0, this.width, this.height);
      if (this.options.showPedestrians?.()) {
        this.drawPedestrians();
        context.drawImage(this.pedestrianCanvas, 0, 0, this.pedestrianCanvas.width, this.pedestrianCanvas.height, 0, 0, this.width, this.height);
      }
    }
    context.lineCap = 'round'; context.lineJoin = 'round';
    this.drawControlledLanes();
    if (this.mode === '2d') {
      cars.forEach((car, index) => {
        const position = this.screen(car.x, car.y);
        if (position.x < -40 || position.y < -40 || position.x > this.width + 40 || position.y > this.height + 40) return;
        drawVehicle(context, position, car, this.view.scale, index);
      });
    }
    this.options.afterVehicles?.(context);
    this.drawSignals();
  }

  drawRoads() {
    const {roadCanvas, network, view} = this;
    const cache = this.roadCache;
    if (cache && cache.network === network && cache.x === view.x && cache.y === view.y && cache.scale === view.scale
      && roadCanvas.width === this.canvas.width && roadCanvas.height === this.canvas.height) return;
    roadCanvas.width = this.canvas.width;
    roadCanvas.height = this.canvas.height;
    const context = roadCanvas.getContext('2d');
    const ratio = window.devicePixelRatio || 1;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.lineCap = 'round'; context.lineJoin = 'round';
    // Zoomed out, lanes are thinner than their markings: draw a plain network overview instead.
    const detailed = defaultPixels(view.scale) >= 4;
    for (const kind of ['junction', 'road']) {
      for (const lane of network.lanes) {
        if (laneKind(lane) !== kind) continue;
        context.strokeStyle = detailed ? colors[kind === 'junction' ? 'junction' : 'asphalt'] : colors.overview;
        context.lineWidth = Math.max(1, laneWidth(lane) * view.scale);
        this.tracePath(context, lane.shape);
        context.stroke();
      }
    }
    if (detailed) this.drawMarkings(context);
    this.roadCache = {network, x: view.x, y: view.y, scale: view.scale};
    this.markersDirty = true;
  }

  drawMarkings(context) {
    if (this.markings?.network !== this.network) this.markings = {network: this.network, ...roadMarkings(this.network.lanes)};
    const scale = this.view.scale;
    const stroke = (lines, color, width, alpha, dash = []) => {
      context.strokeStyle = color;
      context.globalAlpha = alpha;
      context.lineWidth = Math.max(1, width * scale);
      context.setLineDash(dash.map(length => length * scale));
      for (const line of lines) { this.tracePath(context, line); context.stroke(); }
    };
    context.lineCap = 'butt';
    stroke(this.markings.curbs, colors.curb, 0.3, 0.9);
    stroke(this.markings.centers, colors.line, 0.16, 0.7);
    stroke(this.markings.dividers, colors.line, 0.16, 0.55, [3, 6]);
    stroke(this.markings.stops, colors.stop, 0.5, 0.85);
    context.setLineDash([]);
    context.globalAlpha = 1;
    context.lineCap = 'round';
  }

  drawPedestrians() {
    const {pedestrianCanvas, network, view} = this;
    const cache = this.pedestrianCache;
    if (cache && cache.network === network && cache.x === view.x && cache.y === view.y && cache.scale === view.scale
      && pedestrianCanvas.width === this.canvas.width && pedestrianCanvas.height === this.canvas.height) return;
    pedestrianCanvas.width = this.canvas.width;
    pedestrianCanvas.height = this.canvas.height;
    const context = pedestrianCanvas.getContext('2d');
    const ratio = window.devicePixelRatio || 1;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.lineCap = 'round'; context.lineJoin = 'round';
    const detailed = defaultPixels(view.scale) >= 4;
    for (const lane of network.lanes) {
      const kind = lane.render_kind;
      if (kind !== 'sidewalk' && kind !== 'walkingarea' && kind !== 'crossing') continue;
      if (kind === 'crossing') {
        // Zebra: short dashes along the crossing, each as wide as the crossing.
        context.lineCap = 'butt';
        context.strokeStyle = detailed ? colors.zebra : '#ffd23a';
        context.globalAlpha = detailed ? 0.6 : 1;
        context.lineWidth = detailed ? (lane.width || crossingWidth) * view.scale : Math.max(2, Math.min(5, 2.4 * view.scale));
        context.setLineDash(detailed ? [0.5 * view.scale, 0.6 * view.scale] : [4, 4]);
      } else {
        context.lineCap = 'round';
        context.strokeStyle = colors.sidewalk;
        context.globalAlpha = 1;
        context.lineWidth = Math.max(1, (lane.width || sidewalkWidth) * view.scale);
        context.setLineDash([]);
      }
      this.tracePath(context, lane.shape);
      context.stroke();
    }
    context.setLineDash([]);
    context.globalAlpha = 1;
    this.pedestrianCache = {network, x: view.x, y: view.y, scale: view.scale};
  }

  drawControlledLanes() {
    const context = this.context;
    context.strokeStyle = '#5cff7a';
    context.lineWidth = Math.max(4, Math.min(12, this.view.scale * 4));
    const device = this.options.focusedDevice?.();
    const lanes = this.options.highlightedLanes?.(device) || controlledLanes(device, this.network.lanes);
    for (const lane of lanes) {
      this.tracePath(context, lane.shape);
      context.stroke();
    }
  }

  drawSignals() {
    const context = this.context;
    const size = Math.max(0.72, Math.min(1, this.view.scale));
    if (this.markersDirty) {
      this.markers = layoutSignalMarkers(this.network.devices || [], point => this.screen(point.x, point.y), this.width, this.height, size);
      this.markersDirty = false;
    }
    for (const marker of this.markers) {
      context.strokeStyle = '#ff7a1288';
      context.lineWidth = 1;
      context.beginPath();
      context.moveTo(marker.anchor.x, marker.anchor.y);
      context.lineTo(marker.x, marker.y);
      context.stroke();
      context.fillStyle = '#ffb54d';
      context.beginPath();
      context.arc(marker.anchor.x, marker.anchor.y, 2, 0, Math.PI * 2);
      context.fill();
    }
    const states = this.options.deviceStates?.() || [];
    for (const marker of this.markers) {
      const code = deviceState(marker.point, states);
      context.fillStyle = '#050201';
      context.strokeStyle = this.options.markerActive?.(marker.point) ? '#5cff7a' : '#ff7a12';
      context.lineWidth = 1.5;
      context.beginPath();
      context.roundRect(marker.x - 7 * size, marker.y - 16 * size, 14 * size, 32 * size, 3 * size);
      context.fill();
      context.stroke();
      const lamps = [
        {color: '#ff5148', lit: code === 'r' || code === 'u'},
        {color: '#ffcf38', lit: code === 'y' || code === 'Y' || code === 'u'},
        {color: '#44eb88', lit: code === 'g' || code === 'G' || code === 's'}
      ];
      for (let index = 0; index < lamps.length; index++) {
        context.fillStyle = lamps[index].lit ? lamps[index].color : '#2a1408';
        context.beginPath();
        context.arc(marker.x, marker.y + (index - 1) * 9 * size, 3.5 * size, 0, Math.PI * 2);
        context.fill();
      }
    }
  }

  tracePath(context, shape) {
    context.beginPath();
    shape.forEach((point, index) => {
      const position = this.screen(point.x, point.y);
      if (index) context.lineTo(position.x, position.y);
      else context.moveTo(position.x, position.y);
    });
  }

  resize() {
    const rectangle = this.canvas.getBoundingClientRect();
    this.width = rectangle.width; this.height = rectangle.height;
    const ratio = window.devicePixelRatio || 1;
    this.canvas.width = Math.round(this.width * ratio); this.canvas.height = Math.round(this.height * ratio);
    this.context.setTransform(ratio, 0, 0, ratio, 0, 0);
    this.scene?.resize(this.width, this.height, ratio);
    this.fit();
  }

  pointerDown(event) {
    const rotate = this.mode === '3d' && (event.button === 2 || event.shiftKey);
    this.drag = {x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, moved: false, rotate};
    this.canvas.setPointerCapture(event.pointerId);
    this.options.onPointerDown?.(event);
  }

  pointerMove(event) {
    if (!this.drag) { this.options.onHover?.(event); return; }
    const dx = event.clientX - this.drag.x, dy = event.clientY - this.drag.y;
    if (this.drag.rotate) this.scene.rotate(dx, dy);
    else if (this.mode === '3d') this.scene.pan(this.view, dx, dy);
    else { this.view.x -= dx / this.view.scale; this.view.y += dy / this.view.scale; }
    this.drag.moved ||= Math.hypot(event.clientX - this.drag.startX, event.clientY - this.drag.startY) > 4;
    this.drag.x = event.clientX; this.drag.y = event.clientY;
    this.draw();
  }

  pointerUp(event) {
    const clicked = this.drag && !this.drag.moved;
    this.drag = null;
    if (clicked) this.options.onClick?.(event);
  }

  wheel(event) {
    event.preventDefault();
    this.options.onWheel?.(event);
    const rectangle = this.canvas.getBoundingClientRect();
    const x = event.clientX - rectangle.left - this.width / 2, y = event.clientY - rectangle.top - this.height / 2;
    const old = this.view.scale;
    this.view.scale = Math.max(0.005, Math.min(30, old * Math.exp(-event.deltaY * 0.001)));
    // The 3D camera zooms toward the view centre; the flat map keeps the point under the cursor.
    if (this.mode === '2d') { this.view.x += x / old - x / this.view.scale; this.view.y -= y / old - y / this.view.scale; }
    this.draw();
  }
}

// Top-down car at true size when zoomed in, with a minimum size so traffic stays visible.
// Front faces up before rotation; headlights shine while moving, brake lights glow when stopped.
export function drawVehicle(context, position, car, scale, index = 0) {
  const length = Math.max(7, vehicleLength * scale), width = length * vehicleWidth / vehicleLength;
  const stopped = braking(car);
  context.save();
  context.globalAlpha = car.opacity ?? 1;
  context.translate(position.x, position.y);
  context.rotate(car.angle * Math.PI / 180);
  context.globalCompositeOperation = 'lighter';
  const glow = (y, radius, color) => {
    const gradient = context.createRadialGradient(0, y, 0, 0, y, radius);
    gradient.addColorStop(0, color);
    gradient.addColorStop(1, 'transparent');
    context.fillStyle = gradient;
    context.fillRect(-radius, y - radius, radius * 2, radius * 2);
  };
  if (stopped) glow(length * 0.55, length * 0.55, '#ff2e2299');
  else glow(-length * 0.95, length * 0.6, '#fff2dc40');
  context.globalCompositeOperation = 'source-over';
  context.fillStyle = paintFor(car, index);
  context.strokeStyle = '#000000aa';
  context.lineWidth = 1;
  context.beginPath();
  context.roundRect(-width / 2, -length / 2, width, length, width * 0.3);
  context.fill();
  context.stroke();
  if (length >= 10) {
    context.fillStyle = '#0b0f16d0';
    context.beginPath();
    context.moveTo(-width * 0.4, -length * 0.2); context.lineTo(width * 0.4, -length * 0.2);
    context.lineTo(width * 0.33, -length * 0.04); context.lineTo(-width * 0.33, -length * 0.04);
    context.fill();
    context.fillRect(-width * 0.33, length * 0.22, width * 0.66, length * 0.08);
    context.fillStyle = '#ffffff22';
    context.fillRect(-width * 0.33, -length * 0.04, width * 0.66, length * 0.26);
  }
  const lamp = Math.max(1, length * 0.06);
  context.fillStyle = colors.headlight;
  context.fillRect(-width * 0.42, -length / 2, width * 0.24, lamp);
  context.fillRect(width * 0.18, -length / 2, width * 0.24, lamp);
  context.fillStyle = stopped ? colors.brake : '#7a1a12';
  context.fillRect(-width * 0.42, length / 2 - lamp, width * 0.24, lamp);
  context.fillRect(width * 0.18, length / 2 - lamp, width * 0.24, lamp);
  context.restore();
}

// Lane width in CSS pixels at a scale; markings only read once lanes are a few pixels wide.
function defaultPixels(scale) { return laneWidth({id: '', render_kind: 'road'}) * scale; }

// A 2D/3D toggle button; the chosen mode is remembered per browser.
export function bindModeToggle(mapView, button) {
  const show = () => {
    button.textContent = mapView.mode === '3d' ? '2D' : '3D';
    button.setAttribute('aria-pressed', String(mapView.mode === '3d'));
    button.title = mapView.mode === '3d' ? 'Switch to the flat map' : 'Switch to the 3D map (right-drag or Shift-drag to orbit)';
  };
  const apply = async mode => {
    button.disabled = true;
    try {
      await mapView.setMode(mode);
      try { localStorage.setItem(modeKey, mode); } catch {}
    } catch (error) {
      console.error('3D map unavailable', error);
      await mapView.setMode('2d');
    } finally {
      button.disabled = false;
      show();
    }
  };
  button.onclick = () => apply(mapView.mode === '3d' ? '2d' : '3d');
  show();
  let saved = null;
  try { saved = localStorage.getItem(modeKey); } catch {}
  if (saved === '3d') apply('3d');
}

// Queued vehicles placed back from a lane's stop line, following upstream lanes; heading uses SUMO's convention.
export function queuedVehicles(lane, count, laneById, spacing = 7.5) {
  const cars = [];
  const visited = new Set();
  let current = lane, distance = spacing / 2;
  while (current && cars.length < count && !visited.has(current.id)) {
    visited.add(current.id);
    const shape = current.shape;
    let walked = 0;
    for (let index = shape.length - 1; index > 0 && cars.length < count; index--) {
      const a = shape[index - 1], b = shape[index];
      const length = Math.hypot(b.x - a.x, b.y - a.y);
      if (!length) continue;
      const angle = Math.atan2(b.x - a.x, b.y - a.y) * 180 / Math.PI;
      while (distance <= walked + length && cars.length < count) {
        const t = (distance - walked) / length;
        cars.push({x: b.x + (a.x - b.x) * t, y: b.y + (a.y - b.y) * t, angle, speed_mps: 0});
        distance += spacing;
      }
      walked += length;
    }
    distance -= walked;
    current = current.upstream_lane_id ? laneById.get(current.upstream_lane_id) : null;
  }
  return cars;
}
