# City simulator execution plan

Status: task 1 accepted by the user on 2026-10-11; task 2 accepted by the user on 2026-10-11; task 3 provisionally accepted on 2026-10-12. Tasks 4–8 are implemented and verified under the user’s authorization to proceed using tests, with manual review deferred. See [the short review checklist](review-checklist.md).

## What we are building

Load a city project, generate traffic from its per-edge time-dependent weights, run SUMO, and show roads and moving cars. An edge is one directed road segment. Its identifier comes directly from the map; the profile uses that same identifier. Each usable ordinary edge has one profile. The project stores neither individual vehicles nor predetermined journeys. A seed controls the generated realization.

Use C#/.NET 10 for the backend, SUMO for vehicle movement and junctions, and plain HTML/JavaScript for the view. Reuse Docker Compose and the current PostgreSQL setup. Detectors remain empty placeholders with no defined behavior. Schools, population routines, traffic analysis, count panels, trip tables, and reports are outside this implementation.

## Where we stand

- The existing application runs SUMO through C#/TraCI and has controls, a browser map, tests, and Docker packaging.
- The legacy scenario workflow remains available. The new city-project loader imports folders/ZIPs, persists validated inputs, and exposes map/demand APIs. Continuous per-edge generation, controls, the city UI, and Docker delivery are implemented.
- [The accepted format](experiment-project-format.md) and [draft projects](../../experiments/README.md) are available. Iași has 713 passenger-usable ordinary road edges; the tiny graph has eight. Podu Roș adds 106 usable edges. All three projects reference actual map identifiers and were accepted in task 2; directory names remain under drafts/ for continuity.
- [Research inputs](../research/data/iasi-pmud-2023/README.md) contain extracted source material. These are separate from the files the simulator loads.
- Tasks 1–2 are accepted. Task 3 is provisionally accepted; tasks 4–8 are implemented and verified; manual behavior/design review is deferred.

## Review rule: after every task

On 2026-10-12 the user authorized proceeding through the remaining tasks without waiting for individual reviews, trusting automated checks for now. Keep each task's results and pending manual checks recorded. This supersedes the earlier stop-after-every-task rule for this implementation. Manual approval of behavior and visual design remains deferred, not assumed.

## Tasks in order

### 1. Agree on the project format and generation rules

**Status:** accepted by the user on 2026-10-11.

Accepted decisions: explicit per-edge daily profiles; total current edge weight relative to its full-day mean scales the departure rate; distance-weighted mean edge weight scores legal candidate journeys. Zero weight is a preference, not an access restriction; positive requested arrivals with no positive legal candidate must produce an error. Nonrepeating generation ends at the first city midnight. Record the generator version and operational settings for replay; apply bounded backpressure and expose persistent overload. Task 5 review will establish concrete limits and pause/drain behavior. No additional project files are required for these definitions.

Checks passed: JSON parses, manifest file references resolve, all 8/713 usable ordinary edges have unique profiles, and intervals cover the full day. User approved retaining the format and tightening the three semantics above.

Read [experiment-project-format.md](experiment-project-format.md). Review the manifest, graph references, per-edge intervals and weights, seed behavior, overall traffic scale, continuous operation, signal files, and empty detector placeholder. Resolve any remaining choices here before coding.

Deliverable: an accepted format and a small example the user can understand. Explicitly agree how edge weights influence legal journeys and daily traffic volume; weights are preferences, not guaranteed measured road counts.

Checks: examples parse as JSON; each draft's profiles reference real usable edges without omissions or duplicates; time intervals cover the day.

**User review:** accepted. Proceed to task 2 review.

### 2. Prepare source data and the experiment projects with subagents

**Status:** accepted by the user on 2026-10-11, including the additional Podu Roș project and its documented estimation limits.

Delivered by three subagents and reconciled by the root agent: original-report verification in the source README; reproducible preparation assumptions in both project READMEs; independent validation of all JSON, paths, edge profiles, intervals, and absence of authored traffic. All 17,112 Iași hourly values match the documented spatial formula and canonical curve. The original report supports the approximate curve without extraction changes; its chart units are inferred from caption/context. Iași has two weakly connected passenger road portions (659 and 54 edges), both retained for future legal journey generation. Project loading is implemented in task 3; generation remains pending.

Following user review questions, task 2 was expanded to include a separate [Podu Roș project](../../experiments/drafts/podu-ros/README.md). A fresh OSM intersection map was converted with SUMO 1.28.0. [Primary study and released camera data](../research/data/podu-ros/README.md) provide 5,378 selected complete car trajectories from a reported 166-minute evening recording on 16 July 2024. The project covers 106 usable directed edges: 14 use explicit camera-to-map preference estimates, 92 use lane/priority estimates, and unobserved hours are flat extrapolations. Processing-to-recording clock alignment remains approximate. Its 1,943.855422/hour operational scale is a selected-camera-track proxy, not calibrated full-junction demand or density. Validation and native SUMO map loading pass; project assumptions were accepted during task 2 review. All source conversion stays outside the simulator runtime.

The root agent assigns three bounded subtasks in parallel:
- **Source agent:** open the original report, verify the extracted hourly curve and its units/limitations, and record exact source/page references. Start from the links and repository paths below. Do not promise raw per-edge timestamp tables that have not been obtained.
- **Project agent:** use the accepted format, existing map, and verified temporal curve to prepare explicit profiles for every usable edge. Document the spatial estimates and absolute traffic scale. Prepare both the tiny synthetic project and the Iași project; do not write fixed routes or vehicle schedules.
- **Review agent:** independently check graph references, edge coverage, interval coverage, file references, provenance, and the distinction between observations and estimates. Report issues to the root agent before delivery.

The root agent reconciles their results, fixes discrepancies, and presents one coherent deliverable. Source extraction and conversion are preparation work; the running simulator only loads the resulting project.

#### Where to get the data and what to do with it

| Input | Where to look | Preparation action |
| --- | --- | --- |
| Original Iași PMUD report | [Published PDF](https://vision4iasi.ro/wp-content/uploads/2023/11/PMUD-Iasi-FINAL.pdf), printed pages 89–91; Figure 3.23 on page 90 | Download and inspect the chart. Verify the existing 24 approximate hourly values against it. Surveys date from May 2021; publication is 2023. |
| Hourly values and normalized curve | [measurements.csv](../research/data/iasi-pmud-2023/measurements.csv), [profiles.csv](../research/data/iasi-pmud-2023/profiles.csv) | Use the normalized hourly curve for temporal variation. It is an aggregate across three survey sites, not measurements on each map edge or a count of distinct journeys. |
| Source references and survey locations | [sources.json](../research/data/iasi-pmud-2023/sources.json), [locations.json](../research/data/iasi-pmud-2023/locations.json), [source README](../research/data/iasi-pmud-2023/README.md) | Verify provenance and unresolved location mapping. Do not distribute the aggregate equally among sites or invent directional counts. |
| Existing Iași road graph | [network.net.xml](../../scenarios/iasi-central/network.net.xml); copied into [the draft](../../experiments/drafts/iasi-central-day/README.md) | Read ordinary edge identifiers, direction, connectivity, passenger permissions, lanes, and priority. Prepare spatial weights explicitly in demand.json. |
| Map source for a different city or larger area | [OpenStreetMap export](https://www.openstreetmap.org/export); SUMO [OSM import instructions](https://sumo.dlr.de/docs/Networks/Import/OpenStreetMap.html) | Obtain the area's OSM data and convert it with SUMO netconvert. Keep attribution and check bounds, connectivity, vehicle permissions, and generated signals. The first milestone accepts a prepared SUMO network; an in-app OSM importer is not part of it. |
| Small development map | [tiny draft](../../experiments/drafts/tiny-graph/README.md) | Use explicit synthetic edge weights for fast correctness checks. Its junction shape is not a restriction on other projects. |

The available verified source is an approximate published daily curve, **not a complete measured per-edge traffic dataset**. Spatial distribution therefore needs declared estimates. The current Iași draft uses passenger lane count and SUMO road priority for relative edge weights, multiplied by the hourly curve. Its 600 departures/hour baseline and generated signals are estimates. Validate and review these choices rather than presenting them as city measurements.

If additional raw municipal tables are found, retain their provenance outside the project and convert them into revised edge profiles. Acquiring unavailable annex tables is not a prerequisite for this approximate first version.

Deliverables: reviewed source notes under docs/research/data, two complete projects under experiments/drafts, and a README in each describing preparation and assumptions.

Expanded deliverable: the additional Podu Roș project and its separate source evidence and map preview, requested by the user. All three projects were accepted during task 2 review.

Checks: all JSON parses; graph/file references resolve; all usable edges have profiles; no authored vehicles/routes; hourly source curve is preserved; estimated values are clearly identified.

**User review:** accepted. Proceed to task 3.

### 3. Implement project loading

**Status:** provisionally accepted on 2026-10-12 after the user refactored the code and reported passing tests. Manual review deferred.

Delivered: isolated persistent package copies, folder/ZIP import, strict file/network/profile validation, project listing, paginated map geometry, and demand inspection. See [requests and manual review](project-loading.md). Importing creates no run and starts no SUMO process. Checks cover all three projects, restart persistence, malformed inputs, archive traversal/symlinks, duplicate IDs, and resource limits. A preexisting observation-ordering failure found during regression testing was corrected by sorting camera IDs before emitting frames; the same test exposed duplicate fallback crossing counts for shared camera lines, fixed by counting each physical line once per passage. Verification: native build passes with zero warnings/errors; 47 project-loading cases and the full SUMO suite pass (69 passed, one PostgreSQL test skipped because no test database is configured). A live HTTP smoke check imported all three projects, returned Podu Roș geometry, and confirmed zero runs after import.

Start with components/simulator/Streetflow.Simulator/Scenarios/ScenarioCatalog.cs and ScenarioPreparation.cs, plus Api/Endpoints.cs and persistence as needed. Add folder/ZIP import, validation, project listing, and persistent storage. Load the map without starting traffic.

Validate manifest paths, safe archive extraction, network readability, vehicle permissions, unique and complete edge profiles, finite nonnegative weights, contiguous daily intervals, and generation settings. Return useful errors naming the relevant file and edge. Do not silently generate missing profiles.

Deliverable: backend endpoints that import, list, and load a project, with documented requests and a manual example.

Checks: import both projects; reject malformed files, unknown/missing/duplicate edges, and unsafe archive paths; imported projects survive service restart.

**User review:** try importing/loading a project and inspect error behavior. Manual inspection is deferred; generation proceeded under the user’s authorization.

### 4. Implement seeded continuous traffic generation

**Status:** implemented and tested; manual review deferred under the 2026-10-12 authorization.

Delivered: `Generation/CityDemandGenerator.cs` and `CandidateRoutes.cs` generate seeded piecewise Poisson arrivals, bounded permitted shortest paths and detour alternatives, and distance-weighted spatial choices. Independent timing/choice streams and integrated hazard preserve arrivals across batch boundaries and backpressure. Tests cover all three projects, rate scaling, route preference, zero-weight transit, explicit no-positive-candidate errors, alternatives, midnight, and delayed-generation replay. Candidate discovery is bounded and approximate; [the run guide](../../components/simulator/README.md) states its limits.

Start with Scenarios/DemandGenerator.cs and ScenarioPreparation.cs. Replace fixed scenario demand in the new project workflow with the accepted generator. Read current edge weights, generate reachable endpoints and legal candidate journeys, choose routes using those weights, and add arrivals in bounded batches.

Use the agreed overall arrival scale and time-of-day behavior. Poisson controls arrival timing; edge profiles control spatial preferences. Respect direction, permitted movements, vehicle access, minimum/maximum trip distance, and detour limits. Do not hardcode a junction layout or silently substitute a different spatial model.

Deliverable: a generator exercised on tiny and Iași projects through the backend, with a minimal way to run/replay the result for review.

Checks: same project/seed/version reproduces choices; different seeds vary them; changing edge weights changes route preferences; daily changes affect traffic; generated routes are legal; midnight and batch boundaries work.

**User review:** inspect generated moving traffic and agree whether its behavior matches the intended approximation. Automated checks are implementation verification, not simulator analysis features.

### 5. Integrate continuous runs and controls

**Status:** implemented and tested; manual review deferred under the 2026-10-12 authorization.

Delivered: a dedicated city coordinator and TraCI vehicle insertion, live snapshots, start/pause/resume/step/stop/restart/pacing endpoints, city clock, and recorded input/version/limit metadata. One engine owner is shared with legacy runs. No city observations/detectors/completed-trip history is activated. Defaults: 5000 live vehicles, 512 pending entries, 64 arrivals per step, 300 simulation seconds of backlog or pending-entry age before a visible congestion warning. Blocked entries suspend generation while existing traffic continues; congestion does not fail the run. City runs enable SUMO teleport recovery at the same waiting threshold and expose a cumulative gridlock recovery count; capacity includes teleporting vehicles. Pause freezes simulation/generation; stop closes the engine and cancels remaining traffic; nonrepeating traffic drains after midnight without ending the run. SUMO integration checks exercise moving traffic and identical replay on Tiny, Iași, and Podu Roș, engine cleanup, controls, overload, zero traffic, midnight drain, and service-restart replay.

Start with Runtime/RunCoordinator.cs, Runtime/Models.cs, Sumo/SumoEngine.cs, and Api/Endpoints.cs. Connect project loading and generation to start, pause, resume, step, stop, restart, city clock, and playback speed. SUMO owns movement and signals.

Runs continue until stopped, with daily profiles repeating when configured. Keep generation and snapshots bounded; discard completed-vehicle history from the new workflow. A temporary lack of departures must not end the run. Congested entries remain pending; apply backpressure rather than losing vehicles or building an unlimited application queue. State limits and behavior for overload explicitly.

Deliverable: usable control endpoints and live map/vehicle snapshots. Do not activate existing observations, passage recording, trip tables, or detector APIs for this workflow.

Checks: controls, restart cleanup, pending entries, repeated days, continuous operation, and bounded retained state. Preserve unrelated existing functionality.

**User review:** run both projects, try every control, and inspect clock/traffic behavior during deferred manual review.

### 6. Propose the simple web view

**Status:** proposal prepared and implemented under the user’s authorization; visual acceptance is deferred.

Delivered: [the view proposal](web-view.md), with [desktop](../../assets/images/city-view-desktop.png) and [mobile](../../assets/images/city-view-mobile.png) previews of the working UI. The user authorized proceeding without the earlier design gate; this is not recorded as manual design acceptance.

Prepare a lightweight preview using plain HTML/JavaScript. Show project import/selection, map, moving cars, seed, starting city time, playback speed, and controls. Avoid analysis tables and detector counts; detectors remain undefined placeholders.

Deliverable: a concrete preview or working mockup the user can inspect.

**User review:** explicitly ask what they like or dislike. Design feedback is deferred; implementation proceeded under the user’s authorization.

### 7. Implement the accepted web view

**Status:** implemented and browser-tested; manual review deferred.

Delivered: ZIP/server-folder import, project selection and map preview, seed/start-time/speed controls, moving cars, city clock, pan/zoom/fit, and clear errors. The previous inspector remains at `/legacy-inspector.html`. Headless Chromium checked invalid ZIP handling, folder/ZIP imports, all three maps with moving traffic, midnight, controls, active-run reconnect, and desktop/mobile layout without JavaScript errors.

Work in components/simulator/Streetflow.Simulator/wwwroot/index.html, app.js, and styles.css. Connect the accepted view to import, loading, snapshots, and controls. Display understandable loading and validation errors.

Deliverable: the working browser UI for the new project workflow.

Checks: import/load tiny and Iași, show movement, use all controls, handle errors and browser resizing; no analytical panels in the new workflow.

**User review:** try the actual UI and request adjustments. The design checkpoint does not replace this working-UI checkpoint.

### 8. Verify Docker packaging and document operation

**Status:** implemented and verified on Linux x64; manual setup review deferred.

Delivered: bundled example projects in the runtime image, persistent imported packages/run inputs on the existing data volume, [operation/replay/limits/troubleshooting documentation](../../components/simulator/README.md), and portable generator checks in CI. Fresh isolated Compose startup passed with healthy simulator/PostgreSQL containers. All three imports and saved city runs survived container recreation. The packaged test image passed all 91 tests, including PostgreSQL, with no skips. Native build passed with zero warnings/errors. Browser smoke checks passed against Compose. Windows/macOS engine execution and ARM64 remain untested.

Reuse Dockerfile, compose.yaml, and docker/. Keep SUMO and .NET dependencies inside containers and imported projects on persistent storage. Update components/simulator/README.md with prerequisites, build/start/stop commands, port, project import, seed replay, and troubleshooting.

Document Docker Engine/Desktop with Compose as the normal installation requirement. Explain initial image build/download versus subsequent starts, storage needs, and that performance depends on map size and active traffic. Do not promise unlimited city size or identical performance on every device. State which host architectures were actually tested.

Deliverable: Compose setup, reproducible commands, and a fresh-start walkthrough using the two projects.

Checks: relevant .NET tests, fresh Compose startup, browser smoke check, project persistence across restarts, and documented commands. Report any untested platform limitations.

**User review:** run the documented setup and approve final delivery.

## Starting points for the next implementer

Read this plan, then [the format](experiment-project-format.md), [the source README](../research/data/iasi-pmud-2023/README.md), and both project READMEs. Follow the current review authorization recorded above. Preserve legacy code and fixtures; the first milestone changes the new project workflow.

Use the existing test project at components/simulator/Streetflow.Simulator.Tests. Tests should verify the new behavior and failure cases. Keep source conversion, generation verification, and simulator UI separate: the user wants to see a city running, not a data-analysis application.

## Intersection terminal follow-up (2026-10-13)

The user requested lane terminals for the intersection and map display, with workers and root inspection. Three workers prepared exterior lane annotations, backend loading/map support, and the existing dark UI markers. Root reconciled them, implemented terminal-only lane-continuous routing and fixed the misleading overload diagnosis.

Podu Roș now has 49 explicit points: 22 entry lanes and 27 exit lanes, prepared from retained OSM boundary evidence rather than hardcoded runtime locations. No ring/internal endpoints are used. `endpoint_selection: terminals` is explicit; city projects retain their existing endpoint policy. Minimum journey distance is 40 m to cover short clipped boundary pairs. Lane search conservatively avoids changes on short fragments, with a 20 m manoeuvring-distance allowance and 3-second lane-change search cost. All ports are represented across seeds 1/42/1337.

The original high camera-track proxy still produces congestion with inferred junction geometry/signals. That is a recorded unresolved model issue, not hidden by increasing queue limits or changing measured-profile estimates. The longer flow verification uses an explicitly 600/hour fixture and runs 2000 simulation seconds, beyond the original 483.5-second failure; it verifies cars leave at exits. Default-rate failure now identifies the specific blocked entry and retains the last snapshot.

The existing imported map/run packages were preserved. A separate review copy, `podu-ros-terminals` (Podu Roș · lane terminals), is loaded at [localhost:8080](http://localhost:8080/?project=podu-ros-terminals). Browser checks cover all markers, grouped-lane zoom, per-lane selection, run maps, mobile layout, controls, and no JavaScript errors. Root inspected [the map preview](../../assets/images/podu-ros-terminals.png) and [a focused lane](../../assets/images/podu-ros-terminal-lane.png).

Final follow-up verification: native build has zero warnings/errors; all 108 tests pass including PostgreSQL, terminal validation/persistence, coverage across three seeds, and the 2000-second reduced-rate flow/exit check. Chromium inspection passed against the updated Compose service; the original imports and run history remain available.
