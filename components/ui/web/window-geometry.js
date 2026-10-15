const grid = 16;
const snapDistance = 20;

export function snap(value) { return Math.round(value / grid) * grid; }
function gridBounds(bounds) {
  return {width: Math.max(grid, Math.floor(bounds.width / grid) * grid), height: Math.max(grid, Math.floor(bounds.height / grid) * grid)};
}
export function fitRectangle(rectangle, bounds) {
  const available = gridBounds(bounds);
  const width = Math.min(available.width, Math.max(Math.min(288, available.width), snap(rectangle.width)));
  const height = Math.min(available.height, Math.max(Math.min(192, available.height), snap(rectangle.height)));
  return {
    x: Math.max(0, Math.min(available.width - width, snap(rectangle.x))),
    y: Math.max(0, Math.min(available.height - height, snap(rectangle.y))),
    width, height
  };
}
export function dockRectangle(edge, rectangle, bounds) {
  const fitted = fitRectangle(rectangle, bounds);
  const available = gridBounds(bounds);
  if (edge === 'left') fitted.x = 0;
  if (edge === 'right') fitted.x = available.width - fitted.width;
  if (edge === 'top') fitted.y = 0;
  if (edge === 'bottom') fitted.y = available.height - fitted.height;
  return fitted;
}
function overlap(start, length, otherStart, otherLength) {
  return start <= otherStart + otherLength + snapDistance && start + length >= otherStart - snapDistance;
}
function nearest(value, targets, minimum, maximum) {
  let best = null, distance = snapDistance;
  for (const target of targets) {
    if (target < minimum || target > maximum) continue;
    const difference = Math.abs(value - target);
    if (difference < distance) { best = target; distance = difference; }
  }
  return best;
}
export function fitResizedRectangle(rectangle, bounds) {
  const available = gridBounds(bounds);
  const fitted = fitRectangle(rectangle, bounds);
  const x = Math.max(0, Math.min(available.width - grid, snap(rectangle.x)));
  const y = Math.max(0, Math.min(available.height - grid, snap(rectangle.y)));
  return {x, y, width: Math.min(fitted.width, available.width - x), height: Math.min(fitted.height, available.height - y)};
}
export function snapRectangle(rectangle, bounds, neighbors = [], resizing = false) {
  const available = gridBounds(bounds);
  // Resizing keeps the top-left corner fixed, including at a workspace margin.
  const fitted = resizing ? fitResizedRectangle(rectangle, bounds) : fitRectangle(rectangle, bounds);
  const horizontal = resizing ? [available.width] : [0, available.width - fitted.width];
  const vertical = resizing ? [available.height] : [0, available.height - fitted.height];
  for (const neighbor of neighbors) {
    if (overlap(rectangle.y, fitted.height, neighbor.y, neighbor.height)) {
      if (resizing) horizontal.push(neighbor.x, neighbor.x + neighbor.width);
      else horizontal.push(neighbor.x, neighbor.x + neighbor.width, neighbor.x - fitted.width, neighbor.x + neighbor.width - fitted.width);
    }
    if (overlap(rectangle.x, fitted.width, neighbor.x, neighbor.width)) {
      if (resizing) vertical.push(neighbor.y, neighbor.y + neighbor.height);
      else vertical.push(neighbor.y, neighbor.y + neighbor.height, neighbor.y - fitted.height, neighbor.y + neighbor.height - fitted.height);
    }
  }
  const minimumWidth = Math.min(288, available.width - fitted.x);
  const minimumHeight = Math.min(192, available.height - fitted.y);
  const x = nearest(resizing ? rectangle.x + rectangle.width : rectangle.x, horizontal, resizing ? fitted.x + minimumWidth : 0, resizing ? available.width : available.width - fitted.width);
  const y = nearest(resizing ? rectangle.y + rectangle.height : rectangle.y, vertical, resizing ? fitted.y + minimumHeight : 0, resizing ? available.height : available.height - fitted.height);
  if (x !== null) {
    if (resizing) fitted.width = x - fitted.x;
    else fitted.x = x;
  }
  if (y !== null) {
    if (resizing) fitted.height = y - fitted.y;
    else fitted.y = y;
  }
  return {rectangle: fitted, attached: x !== null || y !== null};
}
export function resizeFromEdge(rectangle, bounds, neighbors, edge, dx, dy) {
  const available = gridBounds(bounds);
  const left = edge.includes('left'), top = edge.includes('top');
  const horizontal = left || edge.includes('right');
  const vertical = top || edge.includes('bottom');
  function mirror(value) {
    return {...value, x: left ? available.width - value.x - value.width : value.x, y: top ? available.height - value.y - value.height : value.y};
  }
  const mirroredNeighbors = [];
  for (const neighbor of neighbors) mirroredNeighbors.push(mirror(neighbor));
  const proposed = mirror(rectangle);
  if (horizontal) proposed.width += left ? -dx : dx;
  if (vertical) proposed.height += top ? -dy : dy;
  const result = snapRectangle(proposed, available, mirroredNeighbors, true);
  result.rectangle = mirror(result.rectangle);
  if (!horizontal) { result.rectangle.x = rectangle.x; result.rectangle.width = rectangle.width; }
  if (!vertical) { result.rectangle.y = rectangle.y; result.rectangle.height = rectangle.height; }
  return result;
}
function validRectangle(value) {
  return value && ['x', 'y', 'width', 'height'].every(key => Number.isFinite(value[key]));
}
export function restoreWindowState(stored, fallback, bounds) {
  if (stored?.rect && !stored.rectangle) stored = {...stored, rectangle: stored.rect};
  if (!stored || !validRectangle(stored.rectangle) || typeof stored.open !== 'boolean') return fallback;
  let rectangle = stored.rectangle;
  // Restore the manually chosen size from layouts saved by the old expanding docks.
  if (stored.dock && stored.dock !== 'float' && validRectangle(stored.floating)) rectangle = dockRectangle(stored.dock, stored.floating, bounds);
  return {rectangle: fitRectangle(rectangle, bounds), open: stored.open};
}
