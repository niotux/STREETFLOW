import {layoutSignalMarkers, deviceState, controlledLanes} from './signals.js?v=physical-devices-3';

// One map renderer for Balthasar and Melchior: roads, controlled-lane highlight, vehicles and traffic lights.
// Callers supply vehicles as {x, y, angle (degrees, SUMO heading), speed_mps, opacity?}.
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
    new ResizeObserver(() => this.resize()).observe(canvas);
    canvas.addEventListener('pointerdown', event => this.pointerDown(event));
    canvas.addEventListener('pointermove', event => this.pointerMove(event));
    canvas.addEventListener('pointerup', event => this.pointerUp(event));
    canvas.addEventListener('pointercancel', () => { this.drag = null; });
    canvas.addEventListener('pointerleave', event => this.options.onLeave?.(event));
    canvas.addEventListener('wheel', event => this.wheel(event), {passive: false});
  }

  // network: {lanes: [{id, shape, render_kind?}], devices: [{id, x, y, lane_ids}]}
  setNetwork(network) {
    this.network = network;
    this.markers = [];
    this.markersDirty = true;
    this.fit();
  }

  screen(x, y) {
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
    if (!this.network) return;
    this.drawRoads();
    context.drawImage(this.roadCanvas, 0, 0, this.roadCanvas.width, this.roadCanvas.height, 0, 0, this.width, this.height);
    if (this.options.showPedestrians?.()) {
      this.drawPedestrians();
      context.drawImage(this.pedestrianCanvas, 0, 0, this.pedestrianCanvas.width, this.pedestrianCanvas.height, 0, 0, this.width, this.height);
    }
    context.lineCap = 'round'; context.lineJoin = 'round';
    this.drawControlledLanes();
    const size = Math.max(5, Math.min(15, 4.5 * this.view.scale));
    for (const car of this.options.vehicles?.(now) || []) {
      const position = this.screen(car.x, car.y);
      if (position.x < -20 || position.y < -20 || position.x > this.width + 20 || position.y > this.height + 20) continue;
      drawVehicle(context, position, car, size);
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
    for (const lane of network.lanes) {
      const kind = laneKind(lane);
      if (kind !== 'junction' && kind !== 'road') continue;
      context.strokeStyle = kind === 'junction' ? '#5a2208' : '#b0520f';
      context.lineWidth = Math.max(1, Math.min(9, 3.2 * view.scale));
      this.tracePath(context, lane.shape);
      context.stroke();
    }
    this.roadCache = {network, x: view.x, y: view.y, scale: view.scale};
    this.markersDirty = true;
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
    for (const lane of network.lanes) {
      const kind = lane.render_kind;
      if (kind !== 'sidewalk' && kind !== 'walkingarea' && kind !== 'crossing') continue;
      context.strokeStyle = kind === 'crossing' ? '#ffd23a' : '#4a2a14';
      context.lineWidth = kind === 'crossing' ? Math.max(2, Math.min(5, 2.4 * view.scale)) : Math.max(1, Math.min(3, 1.2 * view.scale));
      context.setLineDash(kind === 'crossing' ? [4, 4] : []);
      this.tracePath(context, lane.shape);
      context.stroke();
    }
    context.setLineDash([]);
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
    this.fit();
  }

  pointerDown(event) {
    this.drag = {x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY, moved: false};
    this.canvas.setPointerCapture(event.pointerId);
    this.options.onPointerDown?.(event);
  }

  pointerMove(event) {
    if (!this.drag) { this.options.onHover?.(event); return; }
    this.view.x -= (event.clientX - this.drag.x) / this.view.scale;
    this.view.y += (event.clientY - this.drag.y) / this.view.scale;
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
    this.view.x += x / old - x / this.view.scale; this.view.y -= y / old - y / this.view.scale;
    this.draw();
  }
}

export function drawVehicle(context, position, car, size) {
  context.save();
  context.globalAlpha = car.opacity ?? 1;
  context.translate(position.x, position.y);
  context.rotate(car.angle * Math.PI / 180);
  context.fillStyle = car.speed_mps < 0.1 ? '#ff2f1f' : '#5cff7a';
  context.fillRect(-size * 0.3, -size * 0.5, size * 0.6, size);
  context.fillStyle = '#e9ffe0';
  context.fillRect(-size * 0.22, -size * 0.35, size * 0.44, Math.max(1, size * 0.13));
  context.restore();
}

function laneKind(lane) {
  return lane.render_kind || (lane.id.startsWith(':') ? 'junction' : 'road');
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
