# City view proposal and implementation

The user authorized implementation on 2026-10-12 with visual review deferred. This is the design to inspect later:

- A narrow left panel: project selection, ZIP import, seed, start time, and speed.
- A large road canvas: cars, pan/zoom, fit button, and attribution.
- One control strip: start, pause/resume, step, stop, restart.
- A city clock and run state; clear import/run errors above the map.
- Responsive stacking on small screens; no detector or analysis panels.

Opening a project loads its roads before traffic starts. Start creates a running simulation; restart resets the same recorded inputs/seed and pauses at the beginning. Closing the page leaves the run active. The former scenario inspector remains available at /legacy-inspector.html.

Manual visual acceptance is pending in [the review checklist](../../active/plans/simulator/review-checklist.md).

Working previews: [desktop](../../../assets/images/city-view-desktop.png) and [mobile](../../../assets/images/city-view-mobile.png). Chromium smoke checks passed against the Compose service: imports, errors, all three maps, moving traffic, midnight, controls, reconnect, and resizing.

Intersection terminal follow-up: the current dark UI preserves the user’s styling and includes entry/exit markers, grouped-lane zoom, per-lane selector, and Show lane. [Whole map](../../../assets/images/podu-ros-terminals.png), [focused lane](../../../assets/images/podu-ros-terminal-lane.png), and [mobile preview](../../../assets/images/podu-ros-terminals-mobile.png).
