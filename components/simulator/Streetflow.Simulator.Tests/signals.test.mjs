import {test} from 'node:test';
import assert from 'node:assert/strict';
import {signalAppearance, movementState, layoutSignalMarkers, deviceState, deviceDescription, controlledLanes, deviceMapStatus} from '../Streetflow.Simulator/wwwroot/signals.js';

test('joined controller states are indexed by movement, preserving mixed colors', () => {
  const signals = [{id: 'cluster_a_b', state: 'Gruy'}];
  assert.equal(movementState({signal_id: 'cluster_a_b', link_index: 0}, signals), 'G');
  assert.equal(movementState({signal_id: 'cluster_a_b', link_index: 1}, signals), 'r');
  assert.equal(movementState({signal_id: 'cluster_a_b', link_index: 2}, signals), 'u');
  assert.equal(movementState({signal_id: 'cluster_a_b', link_index: 3}, signals), 'y');
  assert.equal(movementState({signal_id: 'missing', link_index: 0}, signals), undefined);
});

test('red plus yellow lights both lamps and keeps the stop instruction', () => {
  const appearance = signalAppearance('u');
  assert.equal(appearance.colors.length, 2);
  assert.match(appearance.label, /remain stopped/);
  assert.notEqual(appearance.colors[0], appearance.colors[1]);
});

test('green priority, yielding green, off and unavailable remain distinguishable', () => {
  assert.match(signalAppearance('G').label, /priority/);
  assert.match(signalAppearance('g').label, /yield/);
  assert.match(signalAppearance('s').label, /stop, then yield/);
  assert.match(signalAppearance('o').label, /Off/);
  assert.match(signalAppearance(undefined).label, /No live/);
});

test('dense junction signals remain individually visible without overlapping housings', () => {
  const points = Array.from({length: 81}, (_, index) => ({signal_id: 'joined', link_index: index, x: 450, y: 260}));
  const markers = layoutSignalMarkers(points, point => ({x: point.x, y: point.y}), 900, 520);
  assert.equal(markers.length, points.length);
  for (let i = 0; i < markers.length; i++) {
    assert.equal(markers[i].anchor.x, 450);
    assert.equal(markers[i].anchor.y, 260);
    for (let j = 0; j < i; j++) {
      assert.ok(Math.abs(markers[i].x - markers[j].x) >= 20 || Math.abs(markers[i].y - markers[j].y) >= 38);
    }
  }
});


test('one physical device covering several lanes renders once and uses its own state', () => {
  const device = {id: 'north', label: 'North approach', x: 50, y: 50, lane_ids: ['north_0', 'north_1', 'north_2']};
  assert.equal(layoutSignalMarkers([device], point => point, 100, 100).length, 1);
  assert.equal(deviceState(device, [{id: 'north', state: 'red-yellow'}]), 'u');
  assert.equal(deviceState(device, [{id: 'north', state: 'off'}]), 'o');
  assert.equal(deviceState(device, [{id: 'north', state: 'mixed'}]), 'm');
  assert.equal(deviceState(device, []), undefined);
  assert.match(deviceDescription(device), /North approach/);
  assert.doesNotMatch(deviceDescription(device), /link|center/);
});


test('device lane highlighting follows explicit lane associations and keeps original geometry', () => {
  const lanes = [{id: 'north_0', shape: [{x: 1, y: 2}]}, {id: 'north_1', shape: []}, {id: 'south_0', shape: []}];
  const device = {id: 'north', lane_ids: ['north_0', 'north_1', 'north_0', 'missing']};
  const selected = controlledLanes(device, lanes);
  assert.deepEqual(selected, [lanes[0], lanes[1]]);
  assert.equal(selected[0], lanes[0]);
  assert.deepEqual(controlledLanes(null, lanes), []);
  assert.deepEqual(controlledLanes({lane_ids: []}, lanes), []);
});

test('empty physical maps explain migration and saved-run behavior', () => {
  assert.match(deviceMapStatus([]), /mapping/);
  assert.match(deviceMapStatus([]), /refresh/);
  assert.match(deviceMapStatus([]), /saved run keeps its original mapping/);
  assert.match(deviceMapStatus([{id: 'north'}]), /1 physical traffic light configured/);
});
