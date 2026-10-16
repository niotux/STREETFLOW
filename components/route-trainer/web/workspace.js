import {createWorkspace} from '/ui/windows.js?v=shared-ui-1';

const workspace = createWorkspace({
  root: document.getElementById('studio'),
  layer: document.getElementById('window-layer'),
  storageKey: 'streetflow.trainer.workspace.v1',
  launchers: document.querySelectorAll('[data-open-applet]'),
  resetButton: document.getElementById('reset-layout'),
  focusButton: document.getElementById('focus-map'),
  announcement: document.getElementById('workspace-announcement'),
  stackedMedia: '(max-width: 800px)',
  onLayoutChange: () => window.dispatchEvent(new Event('trainer:fit-map')),
  initialLayout(name, size) {
    const width = size.width < 1300 ? 320 : 352;
    const margin = 16, gap = 16;
    const availableHeight = Math.max(192, size.height - margin * 2 - 32);
    const topHeight = Math.max(192, Math.floor((availableHeight - gap) * .45 / 16) * 16);
    return {
      open: true,
      rectangle: {x: name === 'route' ? margin : size.width - width - margin, y: name === 'training' ? margin + topHeight + gap : margin, width, height: name === 'route' ? availableHeight : name === 'dataset' ? topHeight : availableHeight - topHeight - gap}
    };
  }
});
for (const content of document.querySelectorAll('[data-applet]')) {
  workspace.registerApplet({id: content.dataset.applet, title: content.dataset.title, category: content.dataset.category, content, footer: document.querySelector(`[data-applet-footer="${content.dataset.applet}"]`)});
}
window.addEventListener('trainer:project-opened', () => workspace.show('route', false));
window.addEventListener('trainer:edit-route', () => workspace.show('route'));
window.addEventListener('trainer:training-started', () => workspace.show('training', false));
