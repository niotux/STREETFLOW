import {createWorkspace} from '/ui/windows.js';

export function initializeWorkspace() {
  const workspace = createWorkspace({
    root: document.getElementById('studio'),
    layer: document.getElementById('applet-layer'),
    storageKey: 'streetflow.workspace.v1',
    launchers: document.querySelectorAll('[data-open-applet]'),
    resetButton: document.getElementById('reset-layout'),
    focusButton: document.getElementById('focus-view'),
    announcement: document.getElementById('workspace-announcement'),
    initialLayout(name, size) {
      const compact = size.width < 650;
      const width = name === 'sumo' ? 368 : 336;
      const heights = {runner: 416, loader: 608, analyzer: 576, sumo: 656};
      const left = name === 'loader' || name === 'sumo';
      return {
        rectangle: {x: left || compact ? 0 : size.width - width, y: name === 'analyzer' && !compact ? 48 : 0, width, height: heights[name]},
        open: name === 'runner' || name === 'loader' && !compact
      };
    }
  });
  for (const content of document.querySelectorAll('[data-applet]')) {
    workspace.registerApplet({id: content.dataset.applet, title: content.dataset.title, category: content.dataset.category, content});
  }
  return workspace;
}
