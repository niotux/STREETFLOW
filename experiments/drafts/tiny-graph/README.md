# Tiny city graph project

Synthetic project accepted in task 2 on 2026-10-11. Backend import, continuous generation, and the city UI are available; manual runtime review is deferred.

This folder contains a small SUMO graph, explicit flat daily profiles for all eight ordinary directed edges, an estimated mean of 600 departures/hour, generation settings, and an empty detector placeholder. The synthetic relative weights are west 1, east 0.8, south 0.6, and north 0.4, for both incoming and outgoing edges. The profile references each edge's existing map identifier. It starts at 07:00 and keeps generating traffic until stopped.

To reproduce `demand.json`, give each `west_in`/`west_out` edge weight 1, each `east_in`/`east_out` edge 0.8, each `south_in`/`south_out` edge 0.6, and each `north_in`/`north_out` edge 0.4 for the half-open interval `[0, 86400)`. These are deliberate synthetic values, with no measured source or lane/priority weighting. Each usable ordinary edge has exactly one profile; internal junction edges need none.

The 600 departures/hour baseline, signals, passenger-only fleet, and all spatial weights are test assumptions. Flat profiles make the proposed total-weight daily multiplier equal to 1 throughout the day, so the expected arrival rate stays 600/hour; Poisson timing still varies realized counts. Legal candidate journeys are favored by their distance-weighted mean edge weight. This toy map has no relation to the PMUD observations and is not a calibrated traffic scenario.

Endpoints, routes, and vehicles are generated while running. The generator uses the same graph-based process as the Iași project. This small test graph does not impose a junction shape on the project format.

Review [the format](../../../docs/simulator/experiment-project-format.md) and [tasks](../../../docs/simulator/experiment-integration-plan.md).
