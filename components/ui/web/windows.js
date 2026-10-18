import {fitRectangle, fitResizedRectangle, snapRectangle, resizeFromEdge, dockRectangle, restoreWindowState} from './window-geometry.js?v=shared-ui-1';

const directions = {ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1]};
const edges = ['top', 'bottom', 'left', 'right', 'top-left', 'top-right', 'bottom-left', 'bottom-right'];

function readLayout(key) {
  try { return JSON.parse(localStorage.getItem(key)) || {}; }
  catch { return {}; }
}

function createWindow(applet, number) {
  const element = document.createElement('section');
  element.className = 'ui-window';
  element.dataset.windowId = applet.id;
  element.id = `window-${applet.id}`;
  const heading = document.createElement('div');
  heading.className = 'ui-window-titlebar';
  heading.tabIndex = 0;
  heading.title = 'Drag to move. Drag any border to resize. Arrows move; Ctrl + arrows snap to a margin. Escape cancels a drag.';
  const index = document.createElement('span');
  index.className = 'ui-window-number';
  index.textContent = String(number).padStart(2, '0');
  const title = document.createElement('h2');
  title.id = `${element.id}-title`;
  title.textContent = applet.title;
  element.setAttribute('aria-labelledby', title.id);
  const category = document.createElement('span');
  category.className = 'ui-window-category';
  category.textContent = applet.category || '';
  const tools = document.createElement('div');
  tools.className = 'ui-window-tools';
  const close = document.createElement('button');
  close.type = 'button';
  close.textContent = '×';
  close.setAttribute('aria-label', `Close ${applet.title}`);
  close.title = `Close ${applet.title}; reopen it from the taskbar`;
  tools.append(close);
  heading.append(index, title, category, tools);
  const body = document.createElement('div');
  body.className = 'ui-window-body';
  applet.content.hidden = false;
  body.append(applet.content);
  element.append(heading, body);
  if (applet.footer) {
    applet.footer.classList.add('ui-window-footer');
    element.append(applet.footer);
  }
  const handle = document.createElement('button');
  handle.type = 'button';
  handle.className = 'ui-window-resize';
  handle.setAttribute('aria-label', `Resize ${applet.title}. Use arrow keys.`);
  element.append(handle);
  return {element, heading, close, handle};
}

function createTask(applet) {
  const task = document.createElement('div');
  task.className = 'ui-task';
  const open = document.createElement('button');
  open.type = 'button';
  open.className = 'ui-task-open';
  if (applet.icon) {
    const icon = document.createElement('span');
    icon.className = 'ui-task-icon';
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = applet.icon;
    open.append(icon);
  }
  open.append(applet.title);
  const close = document.createElement('button');
  close.type = 'button';
  close.className = 'ui-task-close';
  close.textContent = '×';
  close.setAttribute('aria-label', `Close ${applet.title}`);
  close.title = `Close ${applet.title}`;
  task.append(open, close);
  return {task, taskOpen: open, taskClose: close};
}

// Apps supply content and initial placement. This module owns window chrome, behavior and the taskbar:
// every registered applet gets a task in `taskbar` that opens or focuses its window and has × to close it.
// `launchers` are extra in-page buttons with data-open-applet that only open a window.
export function createWorkspace({root, layer, storageKey, initialLayout, taskbar, launchers = [], resetButton, focusButton, announcement, stackedMedia, onLayoutChange = () => {}}) {
  const windows = new Map();
  const saved = readLayout(storageKey);
  const media = stackedMedia ? window.matchMedia(stackedMedia) : null;
  const isStacked = () => Boolean(media?.matches);
  const bounds = () => ({width: layer.clientWidth, height: layer.clientHeight});
  const preview = document.createElement('div');
  preview.className = 'ui-snap-preview';
  preview.hidden = true;
  preview.setAttribute('aria-hidden', 'true');
  layer.append(preview);
  layer.classList.add('ui-window-layer');
  let order = 10, gesture = null, focused = false, destroyed = false, front = null;
  const listeners = [];

  function listen(target, name, handler) {
    target.addEventListener(name, handler);
    listeners.push(() => target.removeEventListener(name, handler));
  }
  function announce(text) { if (announcement) announcement.textContent = text; }
  function defaults(id) {
    const state = initialLayout(id, bounds());
    return {rectangle: fitRectangle(state.rectangle, bounds()), open: state.open};
  }
  function applyRectangle(element, rectangle) {
    Object.assign(element.style, {left: `${rectangle.x}px`, top: `${rectangle.y}px`, width: `${rectangle.width}px`, height: `${rectangle.height}px`});
  }
  function render(item) {
    if (!isStacked()) {
      item.state.rectangle = fitRectangle(item.state.rectangle, bounds());
      applyRectangle(item.element, item.state.rectangle);
    }
    item.element.hidden = !item.state.open;
    const visible = item.state.open && !focused;
    item.task.dataset.open = String(visible);
    item.task.dataset.front = String(visible && front === item);
    item.taskOpen.setAttribute('aria-pressed', String(visible));
    item.taskOpen.title = visible ? `Focus ${item.title}` : `Open ${item.title}`;
    item.taskClose.hidden = !visible;
    for (const launcher of launchers) {
      if (launcher.dataset.openApplet === item.id) {
        launcher.setAttribute('aria-pressed', String(item.state.open && !focused));
        launcher.setAttribute('aria-controls', item.element.id);
      }
    }
  }
  function save() {
    const layout = {};
    for (const [id, item] of windows) layout[id] = item.state;
    try { localStorage.setItem(storageKey, JSON.stringify(layout)); }
    catch { /* Window controls remain usable without browser storage. */ }
  }
  function foreground(item) {
    front = item;
    for (const other of windows.values()) {
      other.element.dataset.front = String(other === item);
      other.task.dataset.front = String(other === item && other.state.open && !focused);
    }
    item.element.style.zIndex = ++order;
  }
  function focusMap(value) {
    const changed = focused !== value;
    focused = value;
    root.dataset.focusMap = String(value);
    if (focusButton) {
      focusButton.setAttribute('aria-pressed', String(value));
      focusButton.textContent = value ? 'Show windows' : 'Focus map';
    }
    for (const item of windows.values()) render(item);
    if (changed) onLayoutChange();
  }
  function show(id, focus = true) {
    const item = windows.get(id);
    if (!item) return;
    focusMap(false);
    item.state.open = true;
    render(item);
    foreground(item);
    if (focus) {
      item.heading.focus({preventScroll: !isStacked()});
      if (isStacked()) item.element.scrollIntoView({block: 'start'});
    }
    save();
  }
  function hide(id) {
    const item = windows.get(id);
    if (!item) return;
    item.state.open = false;
    if (front === item) front = null;
    render(item);
    save();
    item.taskOpen.focus({preventScroll: true});
    announce(`${item.title} closed. Reopen it from the taskbar.`);
  }
  function neighbors(item) {
    const result = [];
    for (const other of windows.values()) {
      if (other !== item && other.state.open) result.push(other.state.rectangle);
    }
    return result;
  }
  function begin(event, item, edge) {
    if (isStacked() || event.button !== 0 || gesture || !edge && event.target.closest('button')) return;
    event.preventDefault();
    foreground(item);
    const target = edge ? item.handle : item.heading;
    target.focus({preventScroll: true});
    target.setPointerCapture(event.pointerId);
    gesture = {item, edge, target, pointerId: event.pointerId, x: event.clientX, y: event.clientY, initial: structuredClone(item.state), moved: false};
    root.dataset.arranging = 'true';
  }
  function move(event) {
    if (!gesture || gesture.pointerId !== event.pointerId) return;
    const current = gesture;
    const dx = event.clientX - current.x, dy = event.clientY - current.y;
    if (!current.moved && Math.hypot(dx, dy) < 4) return;
    current.moved = true;
    const rectangle = current.initial.rectangle;
    const result = current.edge
      ? resizeFromEdge(rectangle, bounds(), neighbors(current.item), current.edge, dx, dy)
      : snapRectangle({...rectangle, x: rectangle.x + dx, y: rectangle.y + dy}, bounds(), neighbors(current.item));
    current.item.state.rectangle = result.rectangle;
    render(current.item);
    preview.hidden = !result.attached;
    if (result.attached) applyRectangle(preview, result.rectangle);
  }
  function finish(cancelled = false) {
    if (!gesture) return;
    const current = gesture;
    gesture = null;
    if (current.target.hasPointerCapture(current.pointerId)) current.target.releasePointerCapture(current.pointerId);
    preview.hidden = true;
    delete root.dataset.arranging;
    if (cancelled) {current.item.state = current.initial; render(current.item);}
    else if (current.moved) announce(`${current.item.title} ${current.edge ? 'resized' : 'moved'}.`);
    save();
  }
  function keyboard(event, item, resizing) {
    if (isStacked() || event.target !== (resizing ? item.handle : item.heading)) return;
    const direction = directions[event.key];
    if (!direction) return;
    event.preventDefault();
    const rectangle = {...item.state.rectangle};
    const amount = 16 * (event.shiftKey ? 4 : 1);
    if (event.ctrlKey && !resizing) {
      item.state.rectangle = dockRectangle({ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'top', ArrowDown: 'bottom'}[event.key], rectangle, bounds());
    } else if (resizing) {
      item.state.rectangle = fitResizedRectangle({...rectangle, width: rectangle.width + direction[0] * amount, height: rectangle.height + direction[1] * amount}, bounds());
    } else {
      item.state.rectangle = fitRectangle({...rectangle, x: rectangle.x + direction[0] * amount, y: rectangle.y + direction[1] * amount}, bounds());
    }
    foreground(item); render(item); save();
  }
  function registerApplet(applet) {
    if (destroyed || windows.has(applet.id)) throw new Error(`Cannot register applet ${applet.id}`);
    const chrome = createWindow(applet, windows.size + 1);
    const item = {...applet, ...chrome, ...createTask(applet), state: restoreWindowState(saved[applet.id], defaults(applet.id), bounds())};
    item.taskOpen.setAttribute('aria-controls', item.element.id);
    windows.set(applet.id, item);
    layer.append(item.element);
    taskbar?.append(item.task);
    listen(item.taskOpen, 'click', () => show(item.id));
    listen(item.taskClose, 'click', () => hide(item.id));
    listen(item.element, 'pointerdown', () => foreground(item));
    listen(item.element, 'focusin', () => foreground(item));
    listen(item.close, 'click', () => hide(item.id));
    listen(item.heading, 'pointerdown', event => begin(event, item, null));
    listen(item.handle, 'pointerdown', event => begin(event, item, 'bottom-right'));
    listen(item.heading, 'keydown', event => keyboard(event, item, false));
    listen(item.handle, 'keydown', event => keyboard(event, item, true));
    for (const edge of edges) {
      const grip = document.createElement('div');
      grip.className = `ui-window-edge ui-edge-${edge}`;
      grip.setAttribute('aria-hidden', 'true');
      grip.title = `Drag to resize ${item.title}`;
      listen(grip, 'pointerdown', event => begin(event, item, edge));
      item.element.append(grip);
    }
    render(item);
    return {show: focus => show(item.id, focus), hide: () => hide(item.id), element: item.element};
  }
  function reset() {
    finish(true); focusMap(false);
    for (const item of windows.values()) {item.state = defaults(item.id); render(item);}
    save(); announce('Default window layout restored.'); onLayoutChange();
  }
  for (const launcher of launchers) listen(launcher, 'click', () => show(launcher.dataset.openApplet));
  if (resetButton) listen(resetButton, 'click', reset);
  if (focusButton) listen(focusButton, 'click', () => focusMap(!focused));
  listen(document, 'pointermove', move);
  listen(document, 'pointerup', event => {if (gesture?.pointerId === event.pointerId) finish();});
  listen(document, 'pointercancel', event => {if (gesture?.pointerId === event.pointerId) finish(true);});
  listen(window, 'blur', () => finish(true));
  listen(document, 'keydown', event => {
    if (event.key !== 'Escape') return;
    if (gesture) {event.preventDefault(); finish(true);}
    else if (focused) focusMap(false);
  });
  let wasStacked = isStacked();
  function refresh() {
    finish(true);
    layer.dataset.stacked = String(isStacked());
    for (const item of windows.values()) {
      if (wasStacked && !isStacked() && !saved[item.id]) item.state.rectangle = defaults(item.id).rectangle;
      render(item);
    }
    wasStacked = isStacked();
  }
  if (media) listen(media, 'change', refresh);
  const observer = new ResizeObserver(refresh);
  observer.observe(layer);
  layer.dataset.stacked = String(isStacked());
  return {
    registerApplet, show, hide, reset, focusMap,
    destroy() {
      finish(true); destroyed = true; observer.disconnect();
      for (const remove of listeners) remove();
      for (const item of windows.values()) {item.content.remove(); item.footer?.remove(); item.element.remove(); item.task.remove();}
      preview.remove(); windows.clear();
    }
  };
}
