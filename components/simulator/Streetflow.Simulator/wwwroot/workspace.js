const grid = 16;
const snapDistance = 20;
const storageKey = 'streetflow.workspace.v1';
const arrowDirections = {ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1]};

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
function fitResizedRectangle(rectangle, bounds) {
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
export function restoreAppletState(stored, fallback, bounds) {
  if (!stored || !validRectangle(stored.rectangle) || typeof stored.open !== 'boolean') return fallback;
  let rectangle = stored.rectangle;
  // Restore the manually chosen size from layouts saved by the old expanding docks.
  if (stored.dock && stored.dock !== 'float' && validRectangle(stored.floating)) rectangle = dockRectangle(stored.dock, stored.floating, bounds);
  return {rectangle: fitRectangle(rectangle, bounds), open: stored.open};
}
function readLayout() {
  try { return JSON.parse(localStorage.getItem(storageKey)) || {}; }
  catch { return {}; }
}

export function initializeWorkspace() {
  const studio = document.getElementById('studio');
  const layer = document.getElementById('applet-layer');
  const preview = document.getElementById('snap-preview');
  const announcement = document.getElementById('workspace-announcement');
  const applets = new Map();
  let order = 10, gesture = null;
  const bounds = () => ({width: layer.clientWidth, height: layer.clientHeight});
  const saved = readLayout();

  function defaults(name) {
    const size = bounds();
    const compact = size.width < 650;
    const width = name === 'sumo' ? 368 : 336;
    const heights = {runner: 416, loader: 608, analyzer: 576, sumo: 656};
    const left = name === 'loader' || name === 'sumo';
    const rectangle = fitRectangle({x: left || compact ? 0 : size.width - width, y: name === 'analyzer' && !compact ? 48 : 0, width, height: heights[name]}, size);
    return {rectangle, open: name === 'runner' || name === 'loader' && !compact};
  }
  function applyRectangle(element, rectangle) {
    Object.assign(element.style, {left: `${rectangle.x}px`, top: `${rectangle.y}px`, width: `${rectangle.width}px`, height: `${rectangle.height}px`});
  }
  function render(applet) {
    applet.state.rectangle = fitRectangle(applet.state.rectangle, bounds());
    applyRectangle(applet.element, applet.state.rectangle);
    applet.element.hidden = !applet.state.open;
    for (const launcher of document.querySelectorAll(`[data-open-applet="${applet.name}"]`)) launcher.setAttribute('aria-pressed', String(applet.state.open));
  }
  function save() {
    const layout = {};
    for (const [name, applet] of applets) layout[name] = applet.state;
    try { localStorage.setItem(storageKey, JSON.stringify(layout)); }
    catch { /* Window controls also work when browser storage is unavailable. */ }
  }
  function bringForward(applet) {
    for (const item of applets.values()) item.element.dataset.front = String(item === applet);
    applet.element.style.zIndex = ++order;
  }
  function neighbors(applet) {
    const rectangles = [];
    for (const item of applets.values()) {
      if (item !== applet && item.state.open) rectangles.push(item.state.rectangle);
    }
    return rectangles;
  }
  function exitFocus() {
    document.body.classList.remove('focus-view');
    document.getElementById('focus-view').setAttribute('aria-pressed', 'false');
    document.getElementById('focus-view').textContent = 'Focus map';
  }
  function show(name, focus = true) {
    const applet = applets.get(name);
    if (!applet) return;
    exitFocus();
    applet.state.open = true;
    render(applet);
    bringForward(applet);
    if (focus) applet.heading.focus({preventScroll: true});
    save();
  }
  function hide(applet) {
    applet.state.open = false;
    render(applet);
    save();
    document.querySelector(`nav [data-open-applet="${applet.name}"]`).focus({preventScroll: true});
  }
  function begin(event, applet, resizing) {
    if (event.button !== 0 || gesture) return;
    if (!resizing && event.target.closest('button')) return;
    event.preventDefault();
    bringForward(applet);
    const target = resizing ? applet.handle : applet.heading;
    target.focus({preventScroll: true});
    target.setPointerCapture(event.pointerId);
    gesture = {applet, resizing, target, pointerId: event.pointerId, x: event.clientX, y: event.clientY, initial: structuredClone(applet.state), moved: false};
    studio.dataset.arranging = 'true';
  }
  function move(event) {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const current = gesture;
    const dx = event.clientX - current.x, dy = event.clientY - current.y;
    if (!current.moved && Math.hypot(dx, dy) < 4) return;
    current.moved = true;
    const initial = current.initial.rectangle;
    const result = current.resizing
      ? resizeFromEdge(initial, bounds(), neighbors(current.applet), current.resizing, dx, dy)
      : snapRectangle({...initial, x: initial.x + dx, y: initial.y + dy}, bounds(), neighbors(current.applet));
    current.applet.state.rectangle = result.rectangle;
    render(current.applet);
    preview.hidden = !result.attached;
    if (result.attached) applyRectangle(preview, {...result.rectangle, x: result.rectangle.x + layer.offsetLeft, y: result.rectangle.y + layer.offsetTop});
  }
  function finish(cancelled = false) {
    if (!gesture) return;
    const current = gesture;
    gesture = null;
    if (current.target.hasPointerCapture(current.pointerId)) current.target.releasePointerCapture(current.pointerId);
    preview.hidden = true;
    delete studio.dataset.arranging;
    if (cancelled) { current.applet.state = current.initial; render(current.applet); }
    else if (current.moved) announcement.textContent = `${current.applet.title} ${current.resizing ? 'resized' : 'moved'}.`;
    save();
  }
  for (const element of document.querySelectorAll('[data-applet]')) {
    const name = element.dataset.applet;
    const title = element.querySelector('h2').textContent;
    const heading = element.querySelector('.applet-heading');
    heading.title = 'Drag the title to move; drag any border or corner to resize. Nearby windows and margins snap together. Arrow keys move; Ctrl + arrows snap to a margin.';
    const tools = document.createElement('div');
    tools.className = 'applet-tools';
    const close = document.createElement('button');
    close.type = 'button';
    close.textContent = '×';
    close.setAttribute('aria-label', `Close ${title}`);
    close.title = `Close ${title}; reopen it from the title bar`;
    tools.append(close);
    heading.append(tools);
    const handle = document.createElement('button');
    handle.type = 'button';
    handle.className = 'resize-handle';
    handle.setAttribute('aria-label', `Resize ${title}. Use arrow keys.`);
    element.append(handle);
    const state = restoreAppletState(saved[name], defaults(name), bounds());
    const applet = {name, title, element, heading, handle, state};
    applets.set(name, applet);
    render(applet);
    element.addEventListener('pointerdown', () => bringForward(applet));
    element.addEventListener('focusin', () => bringForward(applet));
    close.onclick = () => hide(applet);
    heading.addEventListener('pointerdown', event => begin(event, applet, false));
    handle.addEventListener('pointerdown', event => begin(event, applet, 'bottom-right'));
    for (const side of ['top', 'bottom', 'left', 'right', 'top-left', 'top-right', 'bottom-left', 'bottom-right']) {
      const edge = document.createElement('div');
      edge.className = `applet-resize-edge edge-${side}`;
      edge.setAttribute('aria-hidden', 'true');
      edge.title = `Drag to resize ${title}`;
      edge.addEventListener('pointerdown', event => begin(event, applet, side));
      element.append(edge);
    }
    function keyboard(event, resizing) {
      if (event.target !== (resizing ? handle : heading)) return;
      const direction = arrowDirections[event.key];
      if (!direction) return;
      event.preventDefault();
      const rectangle = {...applet.state.rectangle};
      const amount = grid * (event.shiftKey ? 4 : 1);
      if (event.ctrlKey && !resizing) {
        applet.state.rectangle = dockRectangle({ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'top', ArrowDown: 'bottom'}[event.key], rectangle, bounds());
      } else {
        if (resizing) { rectangle.width += direction[0] * amount; rectangle.height += direction[1] * amount; }
        else { rectangle.x += direction[0] * amount; rectangle.y += direction[1] * amount; }
        // Arrow keys always advance one grid cell so an edge snap can be broken deliberately.
        applet.state.rectangle = resizing ? fitResizedRectangle(rectangle, bounds()) : fitRectangle(rectangle, bounds());
      }
      render(applet);
      save();
    }
    heading.addEventListener('keydown', event => keyboard(event, false));
    handle.addEventListener('keydown', event => keyboard(event, true));
  }
  for (const launcher of document.querySelectorAll('[data-open-applet]')) launcher.onclick = () => show(launcher.dataset.openApplet);
  document.addEventListener('pointermove', move);
  document.addEventListener('pointerup', event => { if (gesture?.pointerId === event.pointerId) finish(); });
  document.addEventListener('pointercancel', event => { if (gesture?.pointerId === event.pointerId) finish(true); });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && gesture) { event.preventDefault(); finish(true); }
  });
  document.getElementById('reset-layout').onclick = () => {
    finish(true);
    exitFocus();
    for (const applet of applets.values()) { applet.state = defaults(applet.name); render(applet); }
    save();
    announcement.textContent = 'Default window layout restored.';
  };
  new ResizeObserver(() => {
    finish(true);
    for (const applet of applets.values()) render(applet);
  }).observe(layer);
  return {show};
}
