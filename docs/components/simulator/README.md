# SUMO simulator design

The current implementation is a city simulator: load a project, generate traffic, run SUMO, and show the roads and cars. Detectors are reserved placeholders whose behavior will be defined later. See the [operation instructions](../../../components/simulator/README.md) and [deferred manual checks](../../active/plans/simulator/review-checklist.md). The existing backend has additional inspection/observation features described in its run guide; they are not requirements for the new project workflow.

The new simulator targets **Windows, Linux, and macOS**. Its scenario format accepts arbitrary networks; small Iași sections are development fixtures rather than a dataset-size restriction. Achievable speed and capacity depend on resources and scenario complexity. Its backend uses C# / .NET 10, SUMO, PostgreSQL, and Docker Compose. The web inspector uses plain HTML and JavaScript.

## Design documents

| Document | What it defines |
| --- | --- |
| [Physical traffic-light devices](traffic-light-devices.md) | Shared infrastructure mapping for both traffic formats, physical-only taco, and map revisions |
| [Experiment project format](experiment-project-format.md) | Accepted city files, continuous seeded traffic generation, and run controls |
| [Transformer Routes](transformer-routes.md) | Implemented version 2 weights model, four-way test project, inspection, and current limits; version 1 remains Edge Population |
| [Traffic route model handoff](../../active/ideas/simulator/traffic-route-model-handoff.md) | Context and user requirements for a future route model; mathematics and implementation remain undecided |
| [Time dependent probabilistic journey model](../../active/ideas/simulator/traffic-route-model-proposal.md) | Mathematical design for daily journey demand, contextual movement scores, and complete-route probabilities; format and parameter values remain separate work |
| [Traffic model weights project format, version 2](../../active/ideas/simulator/project-format-v2.md) | Model metadata, parameter tensors, schemas, and a complete synthetic example; implemented single-lane support is described in Transformer Routes |
| [Experiment integration tasks](../../archive/experiment-integration-plan.md) | Backend/UI changes and concrete user feedback checkpoints before integration |
| [Backend run guide](backend.md) | Running, configuring, and testing the implemented backend |
| [Stack decision](stack-decision.md) | Language, integration, storage, packaging, and verification scope |
| [Architecture](architecture.md) | SUMO responsibilities, wrapper structure, time, inspection, and scale |
| [Scenario and demand design](scenarios-and-demand.md) | Continuous probabilistic traffic over the road graph |
| [API design](api.md) | Experiment lifecycle, inspection, product observations, and optional future actions |
| [Implementation plan](../../archive/implementation-plan.md) | Required decisions and integration checks before choosing a stack |
| [Shared observation contract](../contracts/traffic-observations.md) | Meanings and identifiers shared with the real detector |

## Existing backend context

The following describes existing capabilities. The implemented city-project workflow above narrows the current scope and leaves detector behavior for a later task.

- SUMO owns vehicle and junction simulation; the wrapper owns configuration, observation generation, inspection, and records.
- One simulator service owns one active run and one SUMO process initially.
- Management and traffic-observation APIs are separate logical surfaces in the same service.
- The web inspector can see exact simulated state; Streetflow sees only configured detector observations.
- Demand can be synthetic, informed by local evidence, or calibrated to measurements. Each scenario states which it is.
- Navigation and traffic-light control remain open Streetflow product choices; the first simulator interface supports observation consumption without selecting either.
- Native C# TraCI integration and Linux packaging have been exercised; native Windows/macOS and ARM64 verification remain outstanding.

The backend implements the core API and SUMO wrapper; the design documents also describe later capabilities. [Backend run guide](backend.md) states the current scope. The original simulator is preserved in [legacy/simulator](../../../legacy/simulator/README.md), including its historical tools.
