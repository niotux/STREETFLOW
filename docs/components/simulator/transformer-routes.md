# Transformer Routes

The simulator supports two city-project formats. **Edge Population** is version 1, with its existing road profiles and generator. **Transformer Routes** is version 2, with numerical arrival, journey, and movement tensors. Both use the same projects folder, map, run controls, SUMO engine, and external signal-control API.

Transformer Routes is the product name for the implemented `contextual_journey_v1` probabilistic model. It uses authored weights and the destination-conditioning mathematics; it is not a trained neural transformer. The [file specification](../../active/ideas/simulator/project-format-v2.md) and [mathematical design](../../active/ideas/simulator/traffic-route-model-proposal.md) define its inputs and behavior.

## Four-way test project

Copy [four-way-transformer-routes](../../../experiments/drafts/four-way-transformer-routes/README.md) into the projects folder. It uses the same four-way geometry as the existing `tiny-graph` Edge Population project. Its five periods, four origins, four exits, one vehicle type/category, and twelve movements require **1,060 numeric parameters**.

The synthetic morning weights favor south-to-north traffic; evening weights reverse that direction. East/west turning shares also change. The project README describes expected shares and its preparation script. The project has no stored vehicles, named journey templates, or fixed routes.

The separate [north-to-east experiment](../../../experiments/drafts/four-way-north-east/README.md), project ID `four-way-north-east`, makes traffic from the top toward the right visually dominant. It requests 900 north arrivals/hour, with 96% selecting the east exit, and 30 arrivals/hour from each other entry. These weights apply all day; the original four-way checkpoint remains unchanged.

Select **Four-way intersection · Transformer Routes** in the Loader, use seed 42, and compare 07:30 with 17:00. Load creates a paused run; Start runs it. Loading another project replaces it. For an Edge Population comparison, select the existing `tiny-graph` project. Edit parameter files in place and refresh the list; saved runs keep their own snapshot.

Traffic lights start off. Vehicles follow SUMO priority rules until an external controller changes them. Lights remain visible; no traffic-light control panel is introduced.

## Runtime and inspection

The generator integrates the total origin arrival intensity, samples the origin and feasible destination/category/vehicle tuple, and calculates a complete destination-conditioned route before submitting its vehicle. All legal road movements remain in the base probability calculation, including unsuccessful exits. The solver retains legal loops and U-turns when the network contains them.

Routes are registered with SUMO as they are generated. Identical road paths reuse a registered route. Checkpoint metadata and numerical parameters are copied into each saved run and included in its input revision. The run records generator version `transformer-routes-v1`, seed, and operational limits. Restart replays its saved inputs; it does not switch generation formats.

| Endpoint | Version 1 | Version 2 |
| --- | --- | --- |
| `GET /api/v1/projects/{id}/demand` | Existing edge demand document | Model metadata |
| `GET /api/v1/projects/{id}/model` | HTTP 409, wrong format | Model metadata |
| `GET /api/v1/projects/{id}/weights` | HTTP 409, wrong format | Numeric parameter tensors |

Project information includes `model_kind`, with `Edge Population` or `Transformer Routes`. The inspector displays the selected period's requested arrival rate for Transformer Routes.

## Initial support and limits

Transformer Routes currently accepts one lane per usable ordinary road and one exit terminal per road. It checks permissions across the full internal connector chain. An entry on the same road as an exit must precede it. Multilane Transformer Routes projects are rejected explicitly; Edge Population retains its existing multilane support. This first version must not be used to claim a new Podu Roș model is ready.

Defaults are configured in the composition root and can be overridden through normal configuration:

| Setting | Default | Behavior |
| --- | ---: | --- |
| `Simulator__MaximumTransformerStates` | 512 | Caps the routing-state graph for the dense continuation solve |
| `Simulator__MaximumTransformerRouteMovements` | 10,000 | Fails if a sampled route exceeds this length; does not truncate or resample it |
| `Simulator__MaximumTransformerCachedContexts` | 128 | Evicts computed distributions and recomputes them deterministically |
| `Simulator__MaximumDynamicRoutes` | 4,096 | Caps distinct routes registered with SUMO; fails rather than substituting a route |

These are operational bounds, separate from model probabilities. Numerical underflow or a singular continuation solve is also an explicit error. The existing bounded pending-entry/backpressure behavior applies to both generators.

A single four-way junction has one route per reachable destination. It tests journey/turn probabilities but cannot independently demonstrate movement-score preferences between alternative routes. Automated cyclic-network coverage checks that destination conditioning retains the correct loop probability and unsuccessful-exit contribution.
