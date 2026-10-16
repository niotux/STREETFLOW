# Melchior / Streetflow logistics

Melchior is the component responsible for traffic light control. Its separate .NET 10 server integrates with [taco](../contracts/taco.md) and provides manual web controls. Automatic management remains future work. See the [implementation guide](../../../components/streetflow/README.md) and [MAGI frontend guide](../../../components/frontend/README.md).

The MAGI frontend opens Melchior, Balthasar, and Casper. Melchior saves a configurable TACO provider URL, discovers its active session/world, caches map geometry, reads traffic and physical-device states, and forwards revision-bound commands. Casper provides camera traffic but unknown lamp states; its controls are disabled. Balthasar provides simulation state and signal control. Melchior does not depend on SUMO implementation classes.

## Requirements already established

- Connect without depending on SUMO or the simulator's internal graph objects.
- Interpret counts, queue estimates, speed, time windows, coverage, and missing observations correctly.
- Use the same observation meanings for simulated and real traffic.
- Keep recommendations and control decisions outside the simulator and detector.
- Allow reproducible evaluation against a baseline using the simulator's run records.

## Future decisions

Casper can replace Balthasar at the configured TACO connection. The selected stoplight displays visible counts and image-motion queue estimates, or unavailable when unsupported/stale. A physical controller adapter and automated traffic decisions remain future work. See [Casper](../detector/README.md).

Before autonomous control, define allowed phases/transitions, observations, decision cadence, conflict protection, clearance timings, stale-data handling, fallback behavior, and physical-controller integration. Manual taco commands currently set lamp colors directly; the operator supplies their sequence.

Define success metrics and reproducible comparison against a baseline. Navigation and route guidance are outside the current manual-controller milestone.

Code location: [components/streetflow](../../../components/streetflow/README.md).
