# Scenarios and traffic demand

Archived proposal. The revised direction is [graph-based experiment projects](../components/simulator/experiment-project-format.md), with [reviewable integration tasks](experiment-integration-plan.md). The activity-based approach below is not part of the current project integration scope.

A scenario combines a road network with a timed demand description, vehicle behavior, signal plans, detector placement, and any incidents. A map alone does not determine how many people drive or where they go.

Use **simple activity-informed flows first**, then tune them with measured traffic. Represent school and work as demand profiles and destinations rather than building an entire population/household simulator initially.

## Scenario package

```text
scenario/
  manifest.json         Identity, revisions, provenance, assumptions
  source/               Map snapshot and any source counts
  network.net.xml       Corrected SUMO network
  demand.json           Zones, timed flows, and generation settings
  routes.rou.xml        Generated or explicitly prescribed vehicle routes
  signals.add.xml       Default signal plans when separate from network
  cameras.json          Visible regions, count lines, lane mappings
  checkpoints.json      Optional oriented passage lines and A-to-B pairs
  scenario.sumocfg      SUMO configuration
```

JSON is a proposed wrapper configuration format; SUMO's own network and route formats remain XML. Preserve source map and conversion settings so corrections can be reproduced. Retain map attribution. Paths are relative to the scenario package. The manifest records coverage and network revision; lane and camera mappings reference that revision.

A run uses the package plus validated overrides: seed, departure window, demand multiplier, time step, observation settings, and pacing. Save the final resolved configuration and generated departure/route list before comparing controllers. Keep demand multipliers distinct from vehicle speed and playback speed.

## School and work profiles

Define a small set of origin/destination zones: residential areas, schools, workplaces, shops, and network entry/exit boundaries. Zones map to suitable connected road edges. A profile states a trip purpose, origin/destination distribution, departure interval, count or rate, vehicle class, and route-choice rule.

For example, a **synthetic fixture** might contain 120 residential-to-school trips during 07:15–08:15 and 500 residential-to-work trips during 07:00–09:00, with an afternoon reverse profile. Those numbers are test inputs, not measured Iași volumes. Sample departure times and destinations from a fixed seed, validate routes, then persist the resulting trips. The first milestone can use fixed plausible routes; later experiments can vary route choice.

School drop-off may include a return trip after dwell time. Represent it as paired flows or trips where the scenario requires it. Avoid attributing convincing behavior to a label alone: congestion depends on the route, departure distribution, turns, road capacity, and signal programs.

Include through traffic from outside the map and local trips inside it. Define where cars enter/leave a clipped network and distinguish study-area trip duration from a person's full journey outside the extract. Parking or stopping only consumes road capacity when modeled explicitly.

Support prescribed trips/flows without requiring activity profiles. This lets any road dataset supply its own demand. SUMO/native tools such as netconvert and duarouter can prepare the network and routes; the wrapper's own profile generator must use the eventual selected non-Python stack. A Python-based SUMO demand script is not a dependency assumed by this design.

## Evidence levels

| Scenario label | Inputs | What a result supports |
| --- | --- | --- |
| Synthetic | Chosen flows, timings, and behavior | Functional checks and controlled comparisons under stated assumptions |
| Evidence-informed | Plausible flows shaped by land use and available counts | More relevant local experiments, with unresolved assumptions |
| Calibrated | Parameters fitted to measured counts, queues, and travel times | Claims within the conditions and locations validated |

Calibration needs independent validation data. Fitting one intersection's count does not prove that routes, travel times, or the rest of the network are realistic. Counts generally admit multiple different route/demand solutions. [SUMO demand from observation points](https://sumo.dlr.de/docs/Demand/Routes_from_Observation_Points.html)

## Getting actual Iași data

Start data collection in parallel with the runnable synthetic scenario. The [Iași research study](../research/02-iasi-traffic-infrastructure.md) identifies these leads:

| Need | Practical route | Limitation |
| --- | --- | --- |
| Roads and lanes | OSM extract, checked against site observation or imagery | Review turn lanes, permitted movements, junction clusters, and transit lanes |
| Traffic demand | Recent approach/turn counts from the municipality or our own timed video/manual counts | Sparse counts do not uniquely determine an origin/destination matrix |
| Signal behavior | Municipal plans or observed phase/cycle timings | OSM signal tags do not provide actual timing programs |
| Travel times and queues | Our observations, available probe data, and detector recordings | Transit travel time includes stops and priority effects |
| Transit | Available schedules and vehicle positions | Verify current access and validity before treating feeds as supported inputs |
| Wider demand | Access to the municipal transport model, or an estimated zone demand model | Older plans describe historical conditions, not today's demand |

Published 2020–2021 counts and the 2014-based planning model are useful historical context. Avoid treating their daily totals as current lane-level peak-hour measurements. Record source date, collection window, units, direction, vehicle classes, missing periods, and interpretation for every imported measurement.

For a first pilot, collect lane/approach volumes in time slices, turning proportions, queue lengths at selected times, queue discharge during green, signal timing, and a few corridor travel times. Use another period/day for validation. Keep the pilot representative enough to include its main bottlenecks; the same workflow scales to larger scenarios.

## Calibration sequence

1. Check network geometry, connectivity, turns, lanes, speed limits, and signal phases.
2. Match observed queue discharge and effective capacity before changing demand to hide supply errors.
3. Adjust timed origins/destinations, boundary flows, route choices, and turn proportions to match counts.
4. Compare travel time, queues, and congestion onset/duration.
5. Validate using withheld observations and repeat runs across seeds.

Record which parameters were fitted and which quantities were validated. An experiment's acceptance tolerances depend on its intended use and data quality; choose them before presenting a calibrated scenario.

## Run accounting

Distinguish scheduled trips, attempted departures, actual insertions, completed trips, unfinished active trips, pending departures, teleports, and other removals. Finish a demand-generation window, then allow a clearance period or report the remaining trips explicitly. Keep detector production running through the declared simulation interval.

The legacy simulator's disappearing delayed drivers are not a default model for the new simulator. Review SUMO's teleport/removal behavior and log it. Product improvements must not come from excluding the slowest or unadmitted trips. [SUMO teleport behavior](https://sumo.dlr.de/docs/Simulation/Why_Vehicles_are_teleporting.html)
