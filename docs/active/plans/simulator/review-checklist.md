# Deferred simulator review

Tasks 3–8 are implemented. Automated checks passed: 108 tests (including PostgreSQL and terminal follow-up), browser smoke, and Docker persistence. Manual review remains pending.

Open the current review instance at [localhost:8084](http://localhost:8084), or follow [normal startup](../../../components/simulator/README.md). Check these when convenient:

- [ ] **Import:** open Tiny, Iași, and Podu Roș; roads appear before starting traffic.
- [ ] **Errors:** try a broken ZIP; see a clear error and no partial project.
- [ ] **Traffic:** cars follow roads and junctions; the pattern looks plausible.
- [ ] **Clock:** change start time; daily traffic changes and midnight wraps correctly.
- [ ] **Replay:** restart with the same seed; the same traffic choices return.
- [ ] **Controls:** pause freezes cars; step advances; resume, speed, stop, and restart work.
- [ ] **UI:** import and controls are easy to find; map zoom/pan and phone layout work.
- [ ] **Docker:** restart Compose; imported projects remain available.

Traffic weights include estimates; plausible movement does not establish measured road densities.

Intersection terminals:

- [ ] **Points:** Podu Roș shows 22 entry circles and 27 exit diamonds; numbered groups expand when clicked.
- [ ] **Lanes:** hover or select a terminal and use Show lane to inspect its lane and approach.
- [ ] **Journeys:** cars enter on marked lanes and leave at exits; no roundabout origins/destinations.

The original imported project remains unchanged. The updated review copy is named [Podu Roș · lane terminals](http://localhost:8080/?project=podu-ros-terminals). Terminal-only routes fix endpoint selection; the original camera-derived traffic scale can still congest the inferred junction model.
