import {test} from 'node:test';
import assert from 'node:assert/strict';
import {snap, fitRectangle, dockRectangle, snapRectangle, restoreWindowState, resizeFromEdge} from '../../ui/web/window-geometry.js';
import {vehicleSettingsXml} from '../Streetflow.Simulator/wwwroot/sumo-settings.js';

test('drag and resize coordinates snap to a 16 pixel grid within the viewport', () => {
  const bounds = {width: 999, height: 701};
  const rectangle = fitRectangle({x: 735, y: -17, width: 325, height: 337}, bounds);
  for (const value of Object.values(rectangle)) assert.equal(value % 16, 0);
  assert.equal(rectangle.y, 0);
  assert.ok(rectangle.x + rectangle.width <= bounds.width);
  assert.ok(rectangle.y + rectangle.height <= bounds.height);
  assert.equal(snap(21), 16);
  assert.equal(snap(27), 32);
});

test('snapping to each margin preserves the chosen size and the other coordinate', () => {
  const bounds = {width: 1024, height: 640};
  const rectangle = {x: 64, y: 48, width: 320, height: 256};
  assert.deepEqual(dockRectangle('left', rectangle, bounds), {...rectangle, x: 0});
  assert.deepEqual(dockRectangle('right', rectangle, bounds), {...rectangle, x: 704});
  assert.deepEqual(dockRectangle('top', rectangle, bounds), {...rectangle, y: 0});
  assert.deepEqual(dockRectangle('bottom', rectangle, bounds), {...rectangle, y: 384});
});

test('small screens clamp window minimums so the title and resize handle stay reachable', () => {
  const rectangle = fitRectangle({x: 900, y: 700, width: 600, height: 900}, {width: 250, height: 170});
  assert.deepEqual(rectangle, {x: 0, y: 0, width: 240, height: 160});
});

test('moving near a window snaps adjacent edges without changing its size', () => {
  const bounds = {width: 1280, height: 768};
  const neighbor = {x: 512, y: 320, width: 320, height: 256};
  const beside = snapRectangle({x: 211, y: 352, width: 288, height: 192}, bounds, [neighbor]);
  assert.deepEqual(beside.rectangle, {x: 224, y: 352, width: 288, height: 192});
  assert.equal(beside.attached, true);
  const below = snapRectangle({x: 544, y: 563, width: 288, height: 192}, bounds, [neighbor]);
  assert.deepEqual(below.rectangle, {x: 544, y: 576, width: 288, height: 192});
  const above = snapRectangle({x: 544, y: 141, width: 288, height: 192}, bounds, [neighbor]);
  assert.equal(above.rectangle.y + above.rectangle.height, neighbor.y);
  const right = snapRectangle({x: 845, y: 352, width: 288, height: 192}, bounds, [neighbor]);
  assert.equal(right.rectangle.x, neighbor.x + neighbor.width);
});

test('distant windows do not attract edges across empty map space', () => {
  const result = snapRectangle({x: 211, y: 64, width: 288, height: 192}, {width: 1280, height: 768}, [{x: 512, y: 480, width: 320, height: 256}]);
  assert.equal(result.rectangle.x, 208);
  assert.equal(result.attached, false);
});

test('moving near any workspace margin preserves the manually chosen dimensions', () => {
  const bounds = {width: 1024, height: 640};
  for (const rectangle of [{x: 11, y: 64, width: 320, height: 256}, {x: 691, y: 64, width: 320, height: 256}, {x: 128, y: 11, width: 320, height: 256}, {x: 128, y: 371, width: 320, height: 256}]) {
    const result = snapRectangle(rectangle, bounds);
    assert.equal(result.attached, true);
    assert.equal(result.rectangle.width, rectangle.width);
    assert.equal(result.rectangle.height, rectangle.height);
  }
});

test('resizing snaps the changing edge to a neighbor and keeps the top-left fixed', () => {
  const result = snapRectangle({x: 64, y: 96, width: 435, height: 208}, {width: 1280, height: 768}, [{x: 512, y: 96, width: 320, height: 320}], true);
  assert.deepEqual(result.rectangle, {x: 64, y: 96, width: 448, height: 208});
  const constrained = snapRectangle({x: 704, y: 384, width: 600, height: 500}, {width: 1024, height: 640}, [], true);
  assert.deepEqual(constrained.rectangle, {x: 704, y: 384, width: 320, height: 256});
});

test('old expanded dock layouts restore the original manual window size', () => {
  const stored = {rectangle: {x: 0, y: 0, width: 320, height: 640}, floating: {x: 96, y: 64, width: 320, height: 256}, dock: 'left', open: true};
  assert.deepEqual(restoreWindowState(stored, {}, {width: 1024, height: 640}), {rectangle: {x: 0, y: 64, width: 320, height: 256}, open: true});
});

test('trainer layouts migrate to the shared window state without losing visibility or size', () => {
  const stored = {rect: {x: 32, y: 48, width: 320, height: 256}, open: false};
  assert.deepEqual(restoreWindowState(stored, {}, {width: 1024, height: 640}), {rectangle: stored.rect, open: false});
});

test('SUMO export maps driver settings to real vehicle attributes and escapes imported IDs', () => {
  const xml = vehicleSettingsXml({id: 'car&"<', vehicleClass: 'passenger', minGap: 3, headway: 1.2, speedFactor: 0.9, speedDeviation: 0.05, acceleration: 2, deceleration: 4});
  assert.match(xml, /<additional>/);
  assert.match(xml, /id="car&amp;&quot;&lt;"/);
  assert.match(xml, /vClass="passenger"/);
  assert.match(xml, /minGap="3" tau="1.2"/);
  assert.match(xml, /speedFactor="0.9" speedDev="0.05"/);
  assert.match(xml, /accel="2" decel="4"/);
});

test('each resize edge keeps the opposite edge and untouched axis fixed', () => {
  const rectangle = {x: 400, y: 128, width: 336, height: 416};
  const bounds = {width: 1408, height: 768};
  assert.deepEqual(resizeFromEdge(rectangle, bounds, [], 'left', 32, 32).rectangle, {x: 432, y: 128, width: 304, height: 416});
  assert.deepEqual(resizeFromEdge(rectangle, bounds, [], 'right', 32, 32).rectangle, {...rectangle, width: 368});
  assert.deepEqual(resizeFromEdge(rectangle, bounds, [], 'top', 32, 32).rectangle, {x: 400, y: 160, width: 336, height: 384});
  assert.deepEqual(resizeFromEdge(rectangle, bounds, [], 'bottom', 32, 32).rectangle, {...rectangle, height: 448});
  assert.deepEqual(resizeFromEdge(rectangle, bounds, [], 'top-left', -32, -32).rectangle, {x: 368, y: 96, width: 368, height: 448});
});

test('left and top resizing snap to neighbors without moving the opposite edges', () => {
  const bounds = {width: 1408, height: 768};
  const rectangle = {x: 400, y: 128, width: 336, height: 416};
  const left = resizeFromEdge(rectangle, bounds, [{x: 64, y: 128, width: 320, height: 256}], 'left', -3, 0).rectangle;
  assert.equal(left.x, 384);
  assert.equal(left.x + left.width, rectangle.x + rectangle.width);
  const top = resizeFromEdge(rectangle, bounds, [{x: 400, y: 0, width: 336, height: 96}], 'top', 0, -19).rectangle;
  assert.equal(top.y, 96);
  assert.equal(top.y + top.height, rectangle.y + rectangle.height);
});
