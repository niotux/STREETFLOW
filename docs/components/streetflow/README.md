# Melchior / Streetflow logistics

Melchior is the component responsible for traffic light control. Its separate .NET 10 server integrates with [tlcapi](../contracts/tlcapi.md) and provides manual web controls. Automatic management remains future work. See the [implementation guide](../../../components/streetflow/README.md) and [MAGI frontend guide](../../../components/frontend/README.md).

The MAGI frontend opens on a component selector: Melchior for logistics, Balthasar for the existing simulator studio, and Casper as a disabled future detector. Melchior saves a configurable provider URL, automatically discovers its active session and configuration revision, caches the world map, reads physical-device states, and forwards revision-bound commands. The backend discovers world changes even while the page is closed. No SUMO controller IDs or movement indices are exposed through the physical-device tlcapi. Balthasar owns simulation lifecycle, engine serialization, validation, and applied-state readback. Melchior does not depend on simulator implementation classes or SUMO.

## Requirements already established

- Connect without depending on SUMO or the simulator's internal graph objects.
- Interpret counts, queue estimates, speed, time windows, coverage, and missing observations correctly.
- Use the same observation meanings for simulated and real traffic.
- Keep recommendations and control decisions outside the simulator and detector.
- Allow reproducible evaluation against a baseline using the simulator's run records.

## Future decisions

A compatible Casper provider can replace the simulator at the same configurable tlcapi connection. A detector and physical controller adapter are outside this milestone. The [traffic-observation contract](../contracts/traffic-observations.md) remains available for future traffic interpretation.

Before autonomous control, define allowed phases/transitions, observations, decision cadence, conflict protection, clearance timings, stale-data handling, fallback behavior, and physical-controller integration. Manual tlcapi commands currently set lamp colors directly; the operator supplies their sequence.

Define success metrics and reproducible comparison against a baseline. Navigation and route guidance are outside the current manual-controller milestone.

Code location: [components/streetflow](../../../components/streetflow/README.md).
