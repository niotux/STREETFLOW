# Central Iași city project

Project accepted in task 2 on 2026-10-11. Backend import, continuous generation, and the city UI are available; manual runtime review is deferred.

This folder contains the existing OSM-derived central road graph and explicit profiles for all 713 passenger-usable ordinary directed edges. Each profile has 24 hourly traffic weights. Its edge identifier is taken directly from network.net.xml.

The hourly shape comes from the approximate PMUD Figure 3.23 curve in [the research inputs](../../../docs/research/data/iasi-pmud-2023/README.md). Those counts are aggregated across three survey sites, not measured per edge. Spatial weights are estimates: multiply the passenger-usable lane count by max(1, SUMO priority + 1), divide by the largest such value across the graph, then multiply by the hourly relative curve and round to six decimal places. This calculation prepares explicit project values; the runtime does not infer or replace missing edge profiles. All edges currently share the same hourly shape, with different relative amplitudes.

To reproduce the stored values, use the six-decimal `relative_weight` column in [profiles.csv](../../../docs/research/data/iasi-pmud-2023/profiles.csv), not a fresh normalization of the raw measurements. For ordinary edge `e`, let `L(e)` count its lanes that permit passenger cars and `P(e)` be its integer SUMO `priority`. Then `S(e) = L(e) * max(1, P(e) + 1)` and `weight(e, hour) = round(S(e) / 39 * relative_weight(hour), 6)`; 39 is the maximum `S` in this graph. Internal junction edges are excluded. A lane with no `allow`/`disallow` restriction permits passengers; otherwise honor its explicit class permissions. Copy each CSV interval's start/end seconds unchanged. The project's graph is a byte-for-byte copy of [the existing network](../../../scenarios/iasi-central/network.net.xml).

The 600 departures/hour baseline is a configurable estimate, not a measured demand total for this map. The proposed generator uses the stored edge weights to choose endpoints and legal journeys while running. Generated road usage is an approximation, not a guarantee that each edge matches an observed count.

Under the proposed rule, hourly departures are `600 * total_current_edge_weight / full_day_mean_total_edge_weight`. This preserves the approximate daily shape; 600 is the full-day mean, not every hour's departure count. Legal candidate journeys are favored by their distance-weighted mean edge weight. The source chart is an aggregate of May 2021 surveys in a report published in 2023; its values were visually extracted, and hourly count units are inferred from the figure context. Survey locations have not been matched to network edges, so this project makes no claim of measured coverage, per-road calibration, directional counts, or observed origin–destination journeys. Lane supply, priority, the absolute baseline, Poisson timing, passenger-only fleet, and generated signals are preparation/model assumptions.

The simulation starts at 07:00 and keeps running until stopped, repeating the daily profile. Passenger cars are the initial vehicle type. Signals come from the network. Detectors are an empty placeholder.

The ordinary passenger connection graph has two weakly connected portions, containing 659 and 54 edges. All 713 profiles remain present. Journeys must use reachable directed endpoints within a portion; the later generator must handle both portions explicitly rather than discard the smaller one or invent connections. The network contains 87 signal programs; these are generated model inputs, not verified local signal timing plans.

The project contains only the manifest, graph, traffic rules, optional placeholders, and this description. Source preparation remains outside the project. No routes or vehicle schedules are preselected.

Network attribution: © OpenStreetMap contributors, https://www.openstreetmap.org/copyright. Generated signal programs and spatial weighting are approximate.

Review [the format](../../../docs/simulator/experiment-project-format.md) and [tasks](../../../docs/simulator/experiment-integration-plan.md).
