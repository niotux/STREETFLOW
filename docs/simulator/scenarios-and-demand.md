# City traffic generation

The project stores a road graph and time-dependent probabilistic traffic rules. The simulator continuously generates vehicles, reachable start/end trips, and legal routes using a seed. SUMO moves the vehicles through the city.

Each usable ordinary directed edge has its own traffic profile, referenced by its existing map identifier. The profile stores relative traffic weights over the day. Prepare Iași profiles using the published aggregate hourly shape and explicitly documented spatial estimates; the available source does not measure each edge. Store the resulting weights in the project. The runtime reads them rather than supplying a hidden spatial fallback. Source-data processing remains outside the running application.

The new workflow loads a project, displays its map, starts traffic, and provides run/playback controls. Detectors remain empty reserved placeholders.

See [the project format](experiment-project-format.md) and [the task plan](experiment-integration-plan.md). The earlier activity-oriented proposal is [archived](../archive/2026-10-10-activity-demand-proposal.md).
