# Streetflow UI

Shared look and window behavior for the MAGI components (Casper, Melchior, Balthasar).
`Streetflow.UI.props` links `web/` into each app as `/ui/`.

| File | Owns |
| --- | --- |
| `web/streetflow-ui.css` | Theme tokens, controls, the taskbar, window chrome |
| `web/windows.js` | `createWorkspace`: window move/resize/snap, layout storage, taskbar tasks |
| `web/window-geometry.js` | Pure rectangle math used by `windows.js` |
| `web/streetflow-logo.png` | The Streetflow logo shown at the left of the taskbar |

## Taskbar

Every component page starts with the same top bar. From left to right:

1. **Logo** — the Streetflow image only, linking to the MAGI page (`/`).
2. **Tasks** — one per window. `windows.js` creates them; the page leaves the `<nav>` empty.
3. **Reset layout** (`⊞`) — restores default window positions.
4. **Status lamp** — `.ui-connection`. Blinks red until the page sets `data-connected="true"`, then turns green. The page writes the text (for example "Connected").
5. **Component name** — `Casper`, `Melchior` or `Balthasar`.

```html
<header class="ui-taskbar">
  <a class="ui-taskbar-logo" href="/" title="MAGI component selection"><img src="/ui/streetflow-logo.png" alt="Streetflow"></a>
  <nav id="taskbar" class="ui-taskbar-tasks" aria-label="Windows"></nav>
  <div class="ui-taskbar-end">
    <button id="reset-layout" type="button" title="Restore default window positions" aria-label="Reset window layout">⊞</button>
    <span id="connection" class="ui-connection" role="status" data-connected="false">Connecting…</span>
    <span class="ui-taskbar-component">Casper</span>
  </div>
</header>
```

Pages without windows (the MAGI page, Casper's calibration page) keep only the logo and component name.
Size the area under the bar with `calc(100dvh - var(--ui-taskbar-height))`; the variable grows when the bar wraps on narrow screens.

### Task behavior

- Click a closed task: the window opens and comes to the front.
- Click an open task: the window comes to the front and takes keyboard focus.
- `×` on an open task closes the window, the same as `×` on the window itself.
- The front window's task is filled with the accent color; other open tasks are outlined; closed tasks are dimmed and have no `×`.
- In "Focus map" mode all windows are hidden, so no task shows as open.

## Windows

Every applet is a window. Mark its content and register it:

```html
<div id="applet-layer">
  <section data-applet="devices" data-icon="◎" data-title="Devices" data-category="CASPER">…</section>
</div>
```

```js
import {createWorkspace} from '/ui/windows.js?v=shared-ui-2';

const workspace = createWorkspace({
  root, layer: $('applet-layer'), storageKey: 'streetflow.<component>.workspace.v1',
  taskbar: $('taskbar'),
  resetButton: $('reset-layout'), focusButton: $('focus-view'),
  announcement: $('workspace-announcement'), stackedMedia: '(max-width: 760px)',
  initialLayout(id, size) { return {rectangle: {x, y, width, height}, open: true}; }
});
for (const content of document.querySelectorAll('[data-applet]')) {
  workspace.registerApplet({id: content.dataset.applet, title: content.dataset.title,
    category: content.dataset.category, icon: content.dataset.icon, content});
}
```

`launchers` is optional: extra in-page buttons with `data-open-applet="<id>"` (such as "Open Loader ↗") that open a window.
Do not put launchers in the taskbar; the taskbar builds its own tasks.

`.ui-app-header` and `.ui-launchers` are the older header and remain only for route-trainer.
