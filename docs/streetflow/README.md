# Streetflow app

Streetflow is the decision-making product. It runs in a separate process and consumes the [traffic-observation contract](../contracts/traffic-observations.md), supplied by the simulator during tests or the detector in real-world operation.

Its product direction remains open: navigation/routing guidance, traffic-signal control, or both. This boundary document does not choose an optimizer, AI model, user interface, language, or deployment model.

## Requirements already established

- Connect without depending on SUMO or the simulator's internal graph objects.
- Interpret counts, queue estimates, speed, time windows, coverage, and missing observations correctly.
- Use the same observation meanings for simulated and real traffic.
- Keep recommendations and control decisions outside the simulator and detector.
- Allow reproducible evaluation against a baseline using the simulator's run records.

## Decisions before a product specification

For navigation, define the user, route request, travel-time estimation, advice delivery, and driver compliance. Sparse junction observations do not directly reveal every road's travel time.

For signal control, define the allowed commands, observations, decision cadence, fallback behavior, and the interface to actual controllers. The simulator can enforce permitted signal transitions, but does not establish access to the city's signal hardware.

For either direction, define the success metric and how the product handles stale, missing, or inaccurate detector data. Full specifications will live here when those decisions are made.

Future code location: [components/streetflow](../../components/streetflow/README.md).
