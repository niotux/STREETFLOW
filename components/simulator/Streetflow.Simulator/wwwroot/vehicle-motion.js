import {RoadGeometry, sampleRoadPath} from './road-geometry.js?v=motion-2';

const minimumBufferMilliseconds = 350;
const maximumHistoryFrames = 32;
const defaultSnapshotIntervalMilliseconds = 250;
const clockCorrectionSeconds = 1;
const maximumFrameSeconds = 0.1;
const clockEpsilonSeconds = 0.0001;

function clamp(value, minimum, maximum) { return Math.max(minimum, Math.min(maximum, value)); }
function copyPose(pose, source, opacity = 1) {
  Object.assign(pose, source);
  pose.opacity = opacity;
  return pose;
}
function distanceProgress(amount, startSpeed, endSpeed, seconds, distance, result) {
  if (distance < 0.001) { result.fraction = amount; result.speed = 0; return; }
  let start = Math.max(0, startSpeed * seconds / distance);
  let end = Math.max(0, endSpeed * seconds / distance);
  // Monotone cubic integration: endpoint speeds shape acceleration without
  // overshooting, reversing, or leaving the observed distance interval.
  if (start + end > 3) {
    const scale = 3 / (start + end);
    start *= scale; end *= scale;
  }
  const squared = amount * amount, cubed = squared * amount;
  result.fraction = clamp(start * (cubed - 2 * squared + amount) + (-2 * cubed + 3 * squared) + end * (cubed - squared), 0, 1);
  const derivative = start * (3 * squared - 4 * amount + 1) + (-6 * squared + 6 * amount) + end * (3 * squared - 2 * amount);
  result.speed = Math.max(0, derivative * distance / seconds);
}
export function distanceFraction(amount, startSpeed, endSpeed, seconds, distance) {
  const result = {};
  distanceProgress(amount, startSpeed, endSpeed, seconds, distance, result);
  return result.fraction;
}
function sampleSegment(segment, amount, seconds) {
  const {start, target, path, pose} = segment;
  if (!start) return copyPose(pose, target, amount);
  if (!target) return copyPose(pose, start, 1 - amount);
  if (amount >= 1) return copyPose(pose, target);
  if (amount <= 0) return copyPose(pose, start);
  if (!path) {
    // Missing paths and true SUMO relocations fade at their observed endpoints;
    // they never drive across unrelated streets to fill in unavailable history.
    return amount < 0.5 ? copyPose(pose, start, 1 - amount * 2) : copyPose(pose, target, (amount - 0.5) * 2);
  }
  distanceProgress(amount, start.speed_mps, target.speed_mps, seconds, path.distance, segment.motion);
  copyPose(pose, target);
  sampleRoadPath(path, segment.motion.fraction, pose);
  pose.speed_mps = segment.motion.speed;
  if (path.distance < 0.25) pose.angle = start.angle + (((target.angle - start.angle + 540) % 360) - 180) * amount;
  return pose;
}
function makeFrame(snapshot, now, previous, geometry) {
  const cars = new Map();
  const exactPoses = [];
  const segments = new Map();
  const seconds = previous ? snapshot.simulation_time - previous.time : 0;
  for (const target of snapshot.vehicles || []) {
    cars.set(target.id, target);
    exactPoses.push({...target, opacity: 1});
    const start = previous?.cars.get(target.id) || null;
    segments.set(target.id, {start, target, path: start ? geometry.path(start, target, seconds) : null, pose: {...target}, motion: {fraction: 0, speed: 0}});
  }
  for (const [id, start] of previous?.cars || []) {
    if (!cars.has(id)) segments.set(id, {start, target: null, path: null, pose: {...start}});
  }
  return {time: snapshot.simulation_time, receivedAt: now, cars, exactPoses, segments};
}

export class VehicleMotion {
  constructor() {
    this.geometry = new RoadGeometry(null);
    this.clear();
  }

  clear() {
    this.frames = [];
    this.poses = [];
    this.runId = null;
    this.running = false;
    this.renderTime = null;
    this.sampledAt = null;
    this.bufferStart = null;
    this.bufferReady = false;
    this.interval = defaultSnapshotIntervalMilliseconds;
    this.jitter = 0;
    this.observedRate = null;
    this.rate = 1;
    this.stepSeconds = 0;
    this.pacing = null;
  }

  setNetwork(network) {
    this.geometry = new RoadGeometry(network);
    this.clear();
  }

  setPacing(pacing) {
    this.pacing = pacing || null;
    const measured = this.observedRate ?? pacing?.multiplier ?? 1;
    this.rate = pacing?.mode === 'realtime' ? Math.min(measured, pacing.multiplier) : measured;
    this.rate = Math.max(0.001, this.rate);
  }

  bufferMilliseconds() {
    const engineInterval = this.stepSeconds / this.rate * 1000;
    return Math.max(minimumBufferMilliseconds, this.interval * 1.75 + this.jitter * 2, engineInterval * 1.25);
  }

  update(snapshot, running, now, pacing, stepSeconds = 0) {
    if (!snapshot?.available) { this.clear(); return; }
    const previous = this.frames[this.frames.length - 1];
    const time = snapshot.simulation_time;
    const sameRun = snapshot.run_id === this.runId;
    if (!running || !sameRun || !previous || !Number.isFinite(time) || time < previous.time) {
      this.clear();
      this.runId = snapshot.run_id;
      this.running = running;
      this.stepSeconds = stepSeconds;
      this.setPacing(pacing);
      this.frames.push(makeFrame({...snapshot, simulation_time: Number.isFinite(time) ? time : 0}, now, null, this.geometry));
      this.renderTime = this.frames[0].time;
      this.bufferStart = this.renderTime;
      this.sampledAt = now;
      return;
    }
    this.running = true;
    this.stepSeconds = stepSeconds;
    this.setPacing(pacing);
    // A duplicate engine tick is not a new motion interval, even if lights change.
    if (time === previous.time) return;
    const elapsed = Math.max(1, now - previous.receivedAt);
    this.jitter = this.jitter * 0.8 + Math.abs(elapsed - this.interval) * 0.2;
    this.interval = this.interval * 0.65 + elapsed * 0.35;
    const measured = (time - previous.time) / elapsed * 1000;
    const majorChange = this.observedRate === null || measured > this.observedRate * 2 || measured < this.observedRate * 0.5;
    this.observedRate = majorChange ? measured : this.observedRate * 0.65 + measured * 0.35;
    this.setPacing(pacing);
    this.frames.push(makeFrame(snapshot, now, previous, this.geometry));
    if (!this.bufferReady && time - this.bufferStart >= this.bufferMilliseconds() / 1000 * this.rate) {
      this.bufferReady = true;
      this.sampledAt = now;
    }
    // Work stays bounded if the tab cannot render as fast as snapshots arrive.
    while (this.frames.length > maximumHistoryFrames) {
      this.frames.shift();
      if (this.renderTime < this.frames[0].time) this.renderTime = this.frames[0].time;
    }
  }

  advance(now) {
    const latest = this.frames[this.frames.length - 1];
    const elapsed = this.sampledAt === null ? 0 : clamp((now - this.sampledAt) / 1000, 0, maximumFrameSeconds);
    this.sampledAt = now;
    if (!this.running || !this.bufferReady || !latest) return;
    const estimatedNow = latest.time + Math.max(0, now - latest.receivedAt) / 1000 * this.rate;
    const desiredTime = estimatedNow - this.bufferMilliseconds() / 1000 * this.rate;
    const error = desiredTime - this.renderTime;
    const correction = clamp(1 + error / (this.rate * clockCorrectionSeconds), 0.25, 2);
    this.renderTime = Math.min(latest.time, this.renderTime + elapsed * this.rate * correction);
    if (latest.time - this.renderTime < clockEpsilonSeconds) {
      this.renderTime = latest.time;
      this.bufferReady = false;
      this.bufferStart = latest.time;
    }
    while (this.frames.length > 2 && this.frames[1].time <= this.renderTime) this.frames.shift();
  }

  sample(now) {
    this.advance(now);
    return this.sampleAt(this.renderTime);
  }

  sampleAt(time) {
    if (!this.frames.length) return [];
    const first = this.frames[0], latest = this.frames[this.frames.length - 1];
    if (time <= first.time) return first.exactPoses;
    if (time >= latest.time) return latest.exactPoses;
    let index = 1;
    while (this.frames[index].time < time) index++;
    const before = this.frames[index - 1], after = this.frames[index];
    const seconds = after.time - before.time;
    const amount = clamp((time - before.time) / seconds, 0, 1);
    this.poses.length = 0;
    for (const segment of after.segments.values()) {
      const pose = sampleSegment(segment, amount, seconds);
      if (pose.opacity > 0.001) this.poses.push(pose);
    }
    return this.poses;
  }

  isAnimating() { return this.running && this.bufferReady && this.frames.length > 1; }
}
