import {test} from 'node:test';
import assert from 'node:assert/strict';
import {VehicleMotion, distanceFraction} from '../Streetflow.Simulator/wwwroot/vehicle-motion.js';
import {RoadGeometry, sampleRoadPath} from '../Streetflow.Simulator/wwwroot/road-geometry.js';

function lane(id, edge, shape, speed = 20) {
  return {id, edge_id: edge, speed, shape: shape.map(([x, y]) => ({x, y}))};
}
const straight = {lanes: [lane('road_0', 'road', [[0, 0], [20000, 0]])]};
const junction = {lanes: [
  lane('in_0', 'in', [[0, 0], [100, 0]]),
  lane(':turn_0', ':turn', [[100, 0], [110, 0], [110, 10], [100, 10]]),
  lane('out_0', 'out', [[100, 10], [0, 10]])
]};
function car(x, speed = 10, id = 'car-1', laneId = 'road_0', y = 0) {
  return {id, x, y, angle: 90, speed_mps: speed, lane_id: laneId};
}
function snapshot(time, vehicles, runId = 'run-1') {
  return {run_id: runId, simulation_time: time, available: true, vehicles};
}
function createMotion(network = straight) {
  const motion = new VehicleMotion();
  motion.setNetwork(network);
  return motion;
}
function pair(motion, start, end, seconds = 1) {
  motion.update(snapshot(0, [start]), true, 0);
  motion.update(snapshot(seconds, [end]), true, 250);
}

test('road distance uses endpoint speed to integrate acceleration without overshoot', () => {
  assert.equal(distanceFraction(0.5, 10, 10, 1, 10), 0.5);
  assert.equal(distanceFraction(0.5, 0, 20, 1, 10), 0.25);
  for (const [start, end] of [[0, 0], [500, 0], [0, 500], [500, 500]]) {
    let previous = 0;
    for (let index = 0; index <= 100; index++) {
      const distance = distanceFraction(index / 100, start, end, 1, 10);
      assert.ok(distance >= previous - 1e-12 && distance <= 1);
      previous = distance;
    }
    assert.equal(previous, 1);
  }
});

test('junction movement follows connector geometry instead of a chord between roads', () => {
  const motion = createMotion(junction);
  pair(motion, car(90, 20, 'car-1', 'in_0'), car(90, 20, 'car-1', 'out_0', 10), 2);
  const pose = motion.sampleAt(1)[0];
  assert.ok(Math.abs(pose.x - 110) < 1e-8);
  assert.ok(Math.abs(pose.y - 5) < 1e-8);
  assert.equal(pose.angle, 0);
  assert.notEqual(pose.x, 90);
});

test('same-lane curves retain every bend rather than sliding across the inside', () => {
  const geometry = new RoadGeometry({lanes: [lane('curve', 'curve', [[0, 0], [10, 0], [10, 10]])]});
  const path = geometry.path(car(0, 10, 'a', 'curve'), car(10, 10, 'a', 'curve', 10), 2);
  const pose = {};
  sampleRoadPath(path, 0.5, pose);
  assert.equal(pose.x, 10);
  assert.equal(pose.y, 0);
  assert.equal(pose.angle, 45);
});

test('lane changes cross smoothly between parallel lanes on the same edge', () => {
  const network = {lanes: [lane('road_0', 'road', [[0, 0], [100, 0]]), lane('road_1', 'road', [[0, 3.2], [100, 3.2]])]};
  const motion = createMotion(network);
  pair(motion, car(20, 20), car(80, 20, 'car-1', 'road_1', 3.2), 3);
  const pose = motion.sampleAt(1.5)[0];
  assert.ok(Math.abs(pose.x - 50) < 0.01);
  assert.ok(Math.abs(pose.y - 1.6) < 0.01);
});

test('high-speed playback is not rejected by an arbitrary world-distance cutoff', () => {
  const motion = createMotion();
  pair(motion, car(0), car(500), 50);
  assert.equal(motion.sampleAt(25)[0].x, 250);
  assert.equal(motion.sampleAt(25)[0].opacity, 1);
});

test('disconnected roads and impossible teleports fade without crossing unrelated streets', () => {
  const motion = createMotion({lanes: [...straight.lanes, lane('other', 'other', [[0, 100], [20000, 100]])]});
  pair(motion, car(0), car(10, 10, 'car-1', 'other', 100), 2);
  const leaving = motion.sampleAt(0.5)[0];
  assert.equal(leaving.x, 0);
  assert.equal(leaving.y, 0);
  assert.equal(leaving.opacity, 0.5);
  const arriving = motion.sampleAt(1.5)[0];
  assert.equal(arriving.y, 100);
  assert.equal(arriving.opacity, 0.5);
  const teleport = createMotion();
  pair(teleport, car(0), car(500), 0.5);
  assert.equal(teleport.sampleAt(0.125)[0].x, 0);
  assert.equal(teleport.sampleAt(0.375)[0].x, 500);
});

test('unknown lane IDs do not get reassigned to an arbitrary nearby road', () => {
  const geometry = new RoadGeometry(straight);
  assert.equal(geometry.path(car(0), car(10, 10, 'car-1', 'unknown'), 1), null);
});

test('new snapshots do not retarget a trajectory that is already being displayed', () => {
  const motion = createMotion();
  pair(motion, car(0), car(10));
  const before = {...motion.sampleAt(0.5)[0]};
  motion.update(snapshot(2, [car(20)]), true, 500);
  const after = motion.sampleAt(0.5)[0];
  assert.equal(before.x, after.x);
  assert.equal(before.y, after.y);
});

test('duplicate engine ticks do not distort rate estimates or restart motion', () => {
  const motion = createMotion();
  pair(motion, car(0), car(10));
  const count = motion.frames.length, rate = motion.rate, interval = motion.interval;
  motion.update({...snapshot(1, [car(10)]), sequence: 99}, true, 450);
  assert.equal(motion.frames.length, count);
  assert.equal(motion.rate, rate);
  assert.equal(motion.interval, interval);
});

test('Pause, Step, reset, and unavailable data show exact engine state', () => {
  const motion = createMotion();
  pair(motion, car(0), car(10));
  motion.update(snapshot(1, [car(10)]), false, 300);
  assert.equal(motion.sample(500)[0].x, 10);
  assert.equal(motion.isAnimating(), false);
  motion.update(snapshot(2, [car(20)]), false, 600);
  assert.equal(motion.sample(600)[0].x, 20);
  motion.update(snapshot(0, [car(0)], 'new-run'), true, 700);
  assert.equal(motion.sample(700)[0].x, 0);
  motion.update({available: false}, false, 800);
  assert.deepEqual(motion.sample(800), []);
});

test('vehicle births and departures fade at their own positions without ID mixing', () => {
  const motion = createMotion();
  pair(motion, car(0), car(10, 10, 'new-car'));
  const poses = motion.sampleAt(0.5);
  assert.equal(poses.length, 2);
  assert.equal(poses.find(pose => pose.id === 'car-1').x, 0);
  assert.equal(poses.find(pose => pose.id === 'new-car').x, 10);
  assert.equal(poses[0].opacity, 0.5);
});

function clockTrace(multiplier, unpaced = false) {
  const motion = createMotion();
  const pacing = {mode: unpaced ? 'unpaced' : 'realtime', multiplier};
  const positions = [];
  let nextPoll = 0;
  for (let now = 0; now <= 8000; now += 16) {
    if (now >= nextPoll) {
      const seconds = Math.floor(now / 1000 * multiplier / 0.5) * 0.5;
      motion.update(snapshot(seconds, [car(seconds * 10)]), true, now, pacing, 0.5);
      nextPoll += 250;
    }
    const pose = motion.sample(now)[0];
    if (now >= 3000) positions.push(pose.x);
    const latest = motion.frames.at(-1);
    assert.ok(motion.renderTime <= latest.time);
  }
  return {motion, positions};
}
for (const multiplier of [1, 5, 10, 37]) {
  test(`buffered clock stays continuous at ${multiplier === 37 ? 'MAX (37 measured simulation seconds per second)' : `${multiplier}x`}`, () => {
    const {motion, positions} = clockTrace(multiplier, multiplier === 37);
    for (let index = 1; index < positions.length; index++) {
      const distance = positions[index] - positions[index - 1];
      assert.ok(distance > 0, `stalled or reversed at frame ${index}`);
      assert.ok(distance < multiplier * 10 * 0.016 * 2.1, `jump at frame ${index}: ${distance}`);
    }
    assert.ok(motion.frames.length <= 32);
    assert.ok(Math.abs(motion.rate - multiplier) < multiplier * 0.05);
  });
}

test('playback changes preserve the render clock instead of resetting car positions', () => {
  const motion = createMotion();
  let simulationTime = 0, previousPoll = 0, nextPoll = 0, previousX = null;
  for (let now = 0; now <= 10000; now += 16) {
    const multiplier = now < 3000 ? 1 : now < 5000 ? 10 : now < 7000 ? 5 : 50;
    const pacing = {mode: now < 7000 ? 'realtime' : 'unpaced', multiplier: now < 7000 ? multiplier : 1};
    if (now >= nextPoll) {
      simulationTime += (now - previousPoll) / 1000 * multiplier;
      motion.update(snapshot(simulationTime, [car(simulationTime * 10)]), true, now, pacing, 0.5);
      nextPoll += 250; previousPoll = now;
    }
    const pose = motion.sample(now)[0];
    if (previousX !== null) assert.ok(pose.x >= previousX, 'playback change reversed the display clock');
    previousX = pose.x;
  }
});

test('a stalled engine drains its buffer and never extrapolates onto an unknown future road', () => {
  const {motion} = clockTrace(10);
  const latest = motion.frames.at(-1);
  for (let now = 8016; now < 12000; now += 16) motion.sample(now);
  assert.equal(motion.renderTime, latest.time);
  assert.equal(motion.sample(12000)[0].x, latest.exactPoses[0].x);
  assert.equal(motion.isAnimating(), false);
});

test('adaptive buffering tolerates irregular snapshot delivery without alternating holds and jumps', () => {
  const motion = createMotion();
  const intervals = [250, 325, 185, 290, 200];
  let nextPoll = 0, poll = 0, previous = null;
  for (let now = 0; now <= 8000; now += 16) {
    if (now >= nextPoll) {
      const time = Math.floor(now / 1000 * 10 / 0.5) * 0.5;
      motion.update(snapshot(time, [car(time * 10)]), true, now, {mode: 'realtime', multiplier: 10}, 0.5);
      nextPoll += intervals[poll++ % intervals.length];
    }
    const pose = motion.sample(now)[0];
    if (now >= 3000 && previous !== null) {
      assert.ok(pose.x > previous, 'delivery jitter froze the rendered position');
      assert.ok(pose.x - previous < 4, 'delivery jitter caused a catch-up jump');
    }
    previous = pose.x;
  }
});
