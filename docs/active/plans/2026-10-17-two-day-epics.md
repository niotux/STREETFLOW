# STREETFLOW: 17–18 October 2026 work plan

## Goal and current baseline

By the end of 18 October, demonstrate a route trained in the standalone trainer running in Balthasar, with Melchior controlling one mapped intersection from TACO traffic measurements. Compare the result with a fixed-time baseline on the same project and seed. A useful outcome is a measured improvement on that scenario, not a claim that the controller solves all traffic.

The merged `main` already includes the version 3 trainer and Balthasar runtime, the tlcapi → TACO rename, Casper's camera worker and ingestion service, and TACO traffic responses from Casper and Balthasar. Do not repeat the rename. Balthasar's current city traffic response reports visible lane counts; queue, crossing, and speed values are null. Casper reports camera queue estimates, but does not control physical lights. Melchior currently forwards traffic and manual device commands; it has no automatic controller.

Use [TACO](../../components/contracts/taco.md) as the shared API contract. Keep missing measurements as null and retain their time, status, and measurement method. Balthasar's complete-lane counts and Casper's camera-visible counts have different coverage and must remain labelled accordingly.

## Priority and sequence

| Priority | Epic | Dependency | Minimum deliverable |
| --- | --- | --- | --- |
| P0 | Balthasar trained-map run and measurements | Merged baseline | A trainer export loads, runs, and provides usable, honestly labelled observations for one mapped intersection. |
| P1 | Melchior adaptive controller | TACO measurements and device map | A safe one-intersection policy runs and can be compared with fixed-time control. |
| P1 | Casper and TACO contract readiness | Merged baseline | Camera data reaches Melchior with stable identity, freshness, and queue semantics. |
| P2 | Trainer author-to-run workflow | Merged baseline and Balthasar handoff | A user can author, train, inspect, export, and load a map without avoidable friction. |

## EPIC 1 — Balthasar: trained maps and comparable measurements

**Owner context:** `components/simulator/`, `experiments/drafts/`, and any dedicated evaluation fixture. Coordinate shared contract edits with Epic 3. Avoid changes under `components/route-trainer/` and `components/streetflow/`.

1. Run one fresh trainer export through Balthasar's `projects/` Loader, load it as a city run, and advance enough steps to show vehicles moving. Use a project with authored physical devices for the controller demonstration.
2. Identify what the city TACO response can actually measure. Add a queue or waiting measure with a declared lane coverage and measurement method if it can be computed reliably. Keep unsupported values null. Do not present full-lane SUMO counts as camera-visible-region measurements.
3. Provide a repeatable fixed-time baseline and record enough data to compare throughput and waiting or queueing under the same project, start time, seed, and run duration. Report gridlock recovery and inserted/arrived counts so recovery cannot masquerade as improvement.

**Done when:** The exported version 3 project runs in Balthasar; TACO data and device IDs match the active world; a documented comparison can be rerun. If queue data cannot be completed within the timebox, use visible count as an explicitly named control input and state that limitation in the comparison.

**Start prompt:** “Own Epic 1 in `docs/active/plans/2026-10-17-two-day-epics.md`. Work only in the Balthasar/simulator area and a dedicated evaluation fixture. Verify a fresh trainer export in a city run, make its TACO measurements useful and correctly labelled, and provide a reproducible fixed-time baseline. Report exact API or contract changes to the integration owner before editing shared contracts.”

## EPIC 2 — Melchior: safe adaptive signal control

**Owner context:** `components/streetflow/` and `components/frontend/Streetflow.Frontend/wwwroot/melchior/`. Consume the existing TACO types. Coordinate changes to `components/contracts/` with Epic 3.

1. Add an explicit manual/automatic mode for one configured intersection. Discover TACO capabilities, session, map, devices, and traffic; reject stale or changed-world input.
2. Use a simple, inspectable demand policy, such as choosing the approach with the larger valid queue after a minimum green. Apply maximum green, yellow, and all-red clearance before a conflicting green. Never assume Casper can accept device commands: its TACO capability currently says `signal_control=false`.
3. Expose current phase, last observation, reason for the choice, and a safe fallback when observations disappear. Make automatic mode stop cleanly on session change or operator switch to manual.
4. Compare with Epic 1's fixed-time baseline. Favor a smaller waiting/queue measure while keeping throughput and recovery visible.

**Done when:** One mapped intersection follows live measurements, transitions safely, survives stale data, and produces a repeatable comparison. Keep any real-world controller integration outside this two-day demonstration.

**Start prompt:** “Own Epic 2 in `docs/active/plans/2026-10-17-two-day-epics.md`. Implement Melchior's one-intersection automatic mode using existing TACO traffic and physical-device commands. Keep phase transitions and stale-data fallback explicit. Coordinate the required observation fields with the Balthasar and TACO owners; do not edit their components.”

## EPIC 3 — Casper and TACO: detector-data contract

**Owner context:** `components/detector/`, `components/contracts/`, and `docs/components/contracts/taco.md`. Coordinate changes to the shared types before other owners consume them. Avoid Melchior and simulator implementation files.

1. Verify the already merged Pi worker → Casper → TACO → Melchior path using the synthetic demo. Check lane, street, intersection, and device identity; timestamp/age; zero versus null; status; and queue measurement method.
2. Document any contract issue immediately for the Balthasar and Melchior owners. Make only the small compatibility changes needed for both providers to emit and consume the same stable shape.
3. Keep camera queue estimates labelled as image-motion estimates. Do not infer unobserved cars or grant Casper signal-control capability without a physical adapter.

**Done when:** A fresh synthetic observation reaches Melchior with correct mapping and freshness, and the shared TACO fields used by the controller are settled. Real-camera accuracy remains a separate evaluation task.

**Start prompt:** “Own Epic 3 in `docs/active/plans/2026-10-17-two-day-epics.md`. The TACO rename and Casper pipeline are already merged. Validate and tighten the detector-to-TACO contract, then send any shared-type changes to the Balthasar and Melchior owners before they implement against them.”

## EPIC 4 — Trainer: route authoring to Balthasar handoff

**Owner context:** `components/route-trainer/`. Coordinate export compatibility problems with Epic 1 rather than changing simulator files.

1. Follow the actual sequence: import a map, author and save routes, validate the dataset, train, inspect progress/report, download a version 3 ZIP, and load it in Balthasar.
2. Fix the largest workflow obstacles found in that sequence. Prioritize clear next actions, validation errors near the affected input, visibility of unsaved edits, training state, and an obvious export-to-Loader instruction.
3. Keep the UI focused on route authoring and training. Preserve the established project and dataset formats unless Epic 1 finds a concrete compatibility defect.

**Done when:** A new user can complete the sequence without reading implementation details or losing edits. Record any remaining usability issues by impact.

**Start prompt:** “Own Epic 4 in `docs/active/plans/2026-10-17-two-day-epics.md`. Refine the trainer's UI around one complete author → train → export → Balthasar workflow. Work within `components/route-trainer/`, preserve dataset/export compatibility, and send simulator defects to the Balthasar owner.”

## Two-day checkpoints

**17 October:** Spend about 90 minutes establishing a fresh trainer-to-Balthasar run. Launch the four owner contexts from the same merged baseline. Give them a three-hour implementation block while you settle TACO field meanings and ownership. Reserve the final hour for an integration pass: build, detector ingestion, Balthasar run, and a list of remaining controller inputs.

**18 October:** Give the first three-hour block to Melchior's controller and the fixed-time comparison. Reserve two hours for merging owner branches and rerunning the complete demonstration, then one hour for the highest-impact trainer UI fixes and docs. Cut optional UI polish before cutting phase safety or measurement validity.

## Coordination for separate Codex instances

- Start each instance in its own branch and worktree from the merged `main`. Do not run several editing instances in the same working tree.
- Treat `components/contracts/` and `docs/components/contracts/taco.md` as Epic 3 owned. Other owners request a field change before changing shared types. Root `compose.yaml`, `README.md`, and shared frontend proxy files belong to the integration pass.
- Each owner reports changed files, API/format changes, the exact scenario used, checks run, and remaining limitations. Merge Epic 3's contract changes before dependent simulator or Melchior changes, then merge the trainer branch.
- The integration owner verifies the same project, seed, time window, and SUMO settings for both controllers. Keep fixed-time and adaptive outputs together with the comparison so the result is reviewable.
