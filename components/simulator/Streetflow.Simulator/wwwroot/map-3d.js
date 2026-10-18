import {colors, paintFor, braking, laneKind, laneWidth, offsetLine, roadMarkings, dashes, sidewalkWidth, crossingWidth, vehicleLength, vehicleWidth} from './road-style.js?v=view-more-1';

// Three.js renderer for the 3D map mode: flat road layers and instanced cars, no buildings.
// SUMO (x east, y north) maps to three.js (x, 0, -y); a car model faces -z, SUMO heading 0.
let threeModule = null;
export async function loadThree() {
  threeModule ??= await import('./vendor/three.module.min.js');
  return threeModule;
}

const fieldOfView = 40;
const minimumTilt = 0.05, maximumTilt = 1.38;
const background = 0x07090d;

export class MapScene {
  constructor(THREE, canvas) {
    this.THREE = THREE;
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({canvas, antialias: true});
    this.renderer.setClearColor(background, 1);
    this.scene = new THREE.Scene();
    this.scene.fog = new THREE.Fog(background, 1, 2);
    this.camera = new THREE.PerspectiveCamera(fieldOfView, 1, 0.5, 10000);
    this.scene.add(new THREE.HemisphereLight(0x8fa0c8, 0x07090d, 0.9));
    const moon = new THREE.DirectionalLight(0xffffff, 1.1);
    moon.position.set(-40, 80, 30);
    this.scene.add(moon);
    this.orbit = {yaw: 0, tilt: 0.85};
    this.width = 1;
    this.height = 1;
    this.roads = null;
    this.pedestrians = null;
    this.cars = null;
    this.capacity = 0;
    this.dummy = new THREE.Object3D();
    this.color = new THREE.Color();
    this.vector = new THREE.Vector3();
    this.glowTexture = glowTexture(THREE);
  }

  resize(width, height, ratio) {
    this.width = width; this.height = height;
    this.renderer.setPixelRatio(Math.min(ratio, 2));
    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / Math.max(1, height);
  }

  distance(scale) {
    return this.height / 2 / Math.tan(fieldOfView * Math.PI / 360) / scale;
  }

  setNetwork(network) {
    for (const group of [this.roads, this.pedestrians]) if (group) { this.scene.remove(group); disposeGroup(group); }
    this.roads = this.pedestrians = null;
    if (!network) return;
    const THREE = this.THREE;
    this.roads = new THREE.Group();
    this.pedestrians = new THREE.Group();
    const layers = {road: [], junction: [], sidewalk: [], zebra: [], curb: [], line: [], dash: [], stop: []};
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const lane of network.lanes) {
      for (const point of lane.shape) {
        minX = Math.min(minX, point.x); minY = Math.min(minY, point.y);
        maxX = Math.max(maxX, point.x); maxY = Math.max(maxY, point.y);
      }
      const kind = laneKind(lane);
      if (kind === 'road' || kind === 'junction') ribbon(layers[kind], lane.shape, laneWidth(lane) / 2);
      else if (kind === 'sidewalk' || kind === 'walkingarea') ribbon(layers.sidewalk, lane.shape, (lane.width || sidewalkWidth) / 2);
      else if (kind === 'crossing') for (const stripe of dashes(lane.shape, 0.5, 0.6)) ribbon(layers.zebra, stripe, (lane.width || crossingWidth) / 2);
    }
    const markings = roadMarkings(network.lanes);
    for (const line of markings.curbs) ribbon(layers.curb, line, 0.15);
    for (const line of markings.centers) ribbon(layers.line, line, 0.08);
    for (const line of markings.dividers) for (const dash of dashes(line, 3, 6)) ribbon(layers.dash, dash, 0.08);
    for (const line of markings.stops) ribbon(layers.stop, line, 0.25);
    // Ground layers do not write depth; render order paints them bottom-up before the cars.
    const add = (group, positions, color, order, opacity = 1) => {
      if (!positions.length) return;
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
      const material = new THREE.MeshBasicMaterial({color, transparent: opacity < 1, opacity, side: THREE.DoubleSide, depthWrite: false});
      const mesh = new THREE.Mesh(geometry, material);
      mesh.renderOrder = order;
      group.add(mesh);
    };
    const size = Math.max(200, maxX - minX, maxY - minY) * 3;
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(size, size), new THREE.MeshBasicMaterial({color: 0x0a0d13, depthWrite: false}));
    ground.rotation.x = -Math.PI / 2;
    ground.position.set((minX + maxX) / 2, 0, -(minY + maxY) / 2);
    ground.renderOrder = 0;
    this.roads.add(ground);
    const grid = new THREE.GridHelper(size, Math.round(size / 16), 0x1a2230, 0x121822);
    grid.position.copy(ground.position);
    grid.material.transparent = true; grid.material.opacity = 0.5; grid.material.depthWrite = false;
    grid.renderOrder = 1;
    this.roads.add(grid);
    add(this.roads, layers.junction, colors.junction, 2);
    add(this.roads, layers.road, colors.asphalt, 3);
    add(this.pedestrians, layers.sidewalk, colors.sidewalk, 4);
    add(this.pedestrians, layers.zebra, colors.zebra, 8, 0.6);
    add(this.roads, layers.curb, colors.curb, 5, 0.9);
    add(this.roads, layers.line, colors.line, 6, 0.7);
    add(this.roads, layers.dash, colors.line, 6, 0.55);
    add(this.roads, layers.stop, colors.stop, 7, 0.85);
    this.scene.add(this.roads, this.pedestrians);
  }

  updateCamera(view) {
    const {yaw, tilt} = this.orbit;
    const distance = this.distance(view.scale);
    const target = this.vector.set(view.x, 0, -view.y);
    this.camera.position.set(
      target.x + distance * Math.sin(tilt) * Math.sin(yaw),
      distance * Math.cos(tilt),
      target.z + distance * Math.sin(tilt) * Math.cos(yaw));
    this.camera.lookAt(target);
    this.camera.near = Math.max(0.5, distance / 200);
    this.camera.far = distance * 12;
    this.camera.updateProjectionMatrix();
    this.camera.updateMatrixWorld();
    this.scene.fog.near = distance * 1.6;
    this.scene.fog.far = distance * 6;
  }

  // Screen position (CSS pixels) of a ground point, for the 2D overlay.
  project(x, y) {
    const point = this.vector.set(x, 0, -y).project(this.camera);
    if (point.z > 1) return {x: -1e5, y: -1e5};
    return {x: (point.x + 1) / 2 * this.width, y: (1 - point.y) / 2 * this.height};
  }

  // Ground displacement for a screen drag, so the point under the cursor follows it.
  pan(view, dx, dy) {
    const {yaw, tilt} = this.orbit;
    const right = {x: Math.cos(yaw), z: -Math.sin(yaw)}, up = {x: -Math.sin(yaw), z: -Math.cos(yaw)};
    const along = 1 / view.scale, depth = along / Math.max(0.2, Math.cos(tilt));
    const x = -right.x * dx * along + up.x * dy * depth, z = -right.z * dx * along + up.z * dy * depth;
    view.x += x; view.y -= z;
  }

  rotate(dx, dy) {
    this.orbit.yaw -= dx * 0.005;
    this.orbit.tilt = Math.max(minimumTilt, Math.min(maximumTilt, this.orbit.tilt + dy * 0.005));
  }

  ensureCapacity(count) {
    if (this.cars && count <= this.capacity) return;
    if (this.cars) { this.scene.remove(this.cars); disposeGroup(this.cars); }
    const THREE = this.THREE;
    this.capacity = Math.max(256, this.capacity * 2, count);
    const paint = new THREE.MeshStandardMaterial({color: 0xffffff, roughness: 0.45, metalness: 0.3});
    const body = new THREE.BoxGeometry(vehicleWidth, 0.75, vehicleLength).translate(0, 0.25 + 0.375, 0);
    const cabin = new THREE.BoxGeometry(vehicleWidth - 0.3, 0.6, 2.1).translate(0, 1.3, 0.2);
    const front = new THREE.BoxGeometry(vehicleWidth - 0.35, 0.16, 0.08).translate(0, 0.72, -vehicleLength / 2 - 0.02);
    const rear = new THREE.BoxGeometry(vehicleWidth - 0.35, 0.16, 0.08).translate(0, 0.76, vehicleLength / 2 + 0.02);
    const glow = (width, length, z) => new THREE.PlaneGeometry(width, length).rotateX(-Math.PI / 2).translate(0, 0.05, z);
    const additive = {map: this.glowTexture, blending: THREE.AdditiveBlending, transparent: true, depthWrite: false};
    this.cars = new THREE.Group();
    const instanced = (geometry, material, order) => {
      const mesh = new THREE.InstancedMesh(geometry, material, this.capacity);
      mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      mesh.setColorAt(0, this.color.set(0x000000));
      mesh.frustumCulled = false;
      mesh.renderOrder = order;
      this.cars.add(mesh);
      return mesh;
    };
    this.parts = {
      body: instanced(body, paint, 10),
      cabin: instanced(cabin, paint, 10),
      headlights: instanced(front, new THREE.MeshBasicMaterial({color: colors.headlight}), 10),
      taillights: instanced(rear, new THREE.MeshBasicMaterial({color: 0xffffff}), 10),
      beams: instanced(glow(4.5, 7, -vehicleLength / 2 - 3.2), new THREE.MeshBasicMaterial({...additive, opacity: 0.35}), 9),
      brakes: instanced(glow(3.6, 3, vehicleLength / 2 + 1), new THREE.MeshBasicMaterial({...additive}), 9)
    };
    this.scene.add(this.cars);
  }

  setVehicles(cars) {
    this.ensureCapacity(cars.length);
    const {dummy, color, parts} = this;
    const dark = new this.THREE.Color(0x000000), headlight = new this.THREE.Color(colors.headlight);
    const brake = new this.THREE.Color(colors.brake), dim = new this.THREE.Color(0x5a1410);
    cars.forEach((car, index) => {
      dummy.position.set(car.x, 0, -car.y);
      dummy.rotation.set(0, -car.angle * Math.PI / 180, 0);
      dummy.updateMatrix();
      for (const mesh of Object.values(parts)) mesh.setMatrixAt(index, dummy.matrix);
      color.set(paintFor(car, index));
      parts.body.setColorAt(index, color);
      parts.cabin.setColorAt(index, color.multiplyScalar(0.8));
      const stopped = braking(car);
      parts.headlights.setColorAt(index, headlight);
      parts.taillights.setColorAt(index, stopped ? brake : dim);
      parts.beams.setColorAt(index, stopped ? dark : headlight);
      parts.brakes.setColorAt(index, stopped ? brake : dark);
    });
    for (const mesh of Object.values(parts)) {
      mesh.count = cars.length;
      mesh.instanceMatrix.needsUpdate = true;
      mesh.instanceColor.needsUpdate = true;
    }
  }

  render(view, cars, showPedestrians) {
    this.updateCamera(view);
    if (this.pedestrians) this.pedestrians.visible = showPedestrians;
    this.setVehicles(cars);
    this.renderer.render(this.scene, this.camera);
  }
}

function ribbon(positions, shape, half) {
  if (shape.length < 2) return;
  const left = offsetLine(shape, half), right = offsetLine(shape, -half);
  for (let index = 1; index < shape.length; index++) {
    const a = left[index - 1], b = right[index - 1], c = left[index], d = right[index];
    positions.push(a.x, 0, -a.y, b.x, 0, -b.y, c.x, 0, -c.y, c.x, 0, -c.y, b.x, 0, -b.y, d.x, 0, -d.y);
  }
}

function glowTexture(THREE) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 64;
  const context = canvas.getContext('2d');
  const gradient = context.createRadialGradient(32, 32, 0, 32, 32, 32);
  gradient.addColorStop(0, 'rgba(255,255,255,1)');
  gradient.addColorStop(0.25, 'rgba(255,255,255,.55)');
  gradient.addColorStop(1, 'rgba(255,255,255,0)');
  context.fillStyle = gradient;
  context.fillRect(0, 0, 64, 64);
  return new THREE.CanvasTexture(canvas);
}

function disposeGroup(group) {
  group.traverse(object => {
    object.geometry?.dispose();
    if (object.material && !object.material.map) object.material.dispose();
  });
}
