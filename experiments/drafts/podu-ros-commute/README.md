# Podu Roș commute estimate

This variant retains the Podu Roș road network, all 49 boundary terminals and its external signal-control API. It adds the user's observed commute direction: toward the city/Palas in the morning, toward the southern approaches in the evening. It is **an estimated scenario, not measured full-day road density**. The original `podu-ros` camera-window project remains unchanged, and saved runs retain their original inputs.

Prepare this package with `python3 docs/research/data/podu-ros/prepare_commute.py`. The script copies map/terminal assets and materializes all 106 edge profiles; runtime does not need Python. Import this folder under its new ID `podu-ros-commute`. The default start is 07:30; compare with 17:00 using the same seed.

Inputs and assumptions:

- The original [camera-derived profiles](../podu-ros/README.md#traffic-evidence-and-clock-assumptions) retain their relative evening variation from 19:09 to about 21:55. They concern selected trajectories in limited camera coverage, not whole-intersection arrivals or measured density.
- Outside that window, the original per-edge baseline is multiplied by the [PMUD combined three-site daily curve](../../../docs/research/data/iasi-pmud-2023/README.md). Applying that citywide shape to Podu Roș is an estimate. It does not supply local directional counts.
- Peak windows of 07:00–09:00 and 16:00–19:00, and strengths of 2.4 in the preferred direction versus 0.35 in the opposite direction, are preparation assumptions. Shoulder windows of 06:00–07:00/09:00–10:00 and 15:00–16:00/19:00–20:00 use 1.4 versus 0.7. Other hours have no directional multiplier.
- Each edge's north/south alignment is its signed start-to-end displacement in SUMO's local Y coordinate divided by its polyline length. The multiplier interpolates from 1 for east/west edges to the directional strength for north/south edges. This approximates geographic commute direction; it does not establish destinations inside Palas or observed turning proportions.
- The arrival baseline remains the original 1,943.855422 selected trajectories/hour proxy. It is not calibrated whole-intersection demand. The generator normalizes the resulting daily weight integral and chooses legal routes by their current edge preferences. Congestion and signals affect resulting occupancy; the simulation does not force queues to match a supposed measured density.

With generator `city-weights-v3-through`, same-seed half-hour checks (seed 42) generated 325 journeys using the city-bound Nicolina road versus 123 using the opposite road at 08:00, and 107 versus 420 at 17:00. These figures count actual corridor use, rather than just displacement between journey endpoints. They verify the prepared model's change of direction, not observed traffic counts.

Traffic-light symbols remain visible when off or stopped. Lights start off and can be changed only through the API; there is no UI control panel. See [external signal control](../../../components/simulator/README.md#external-signal-control). Vehicles leave SUMO at their configured exit terminals; boundary tests exercise every exit with teleporting disabled.

## Nicolina gridlock correction (2026-10-14)

The paused run `7cfa39de37764106bed618fbcea6ad22` was reproduced with all 557 vehicle positions matching exactly at 2,492.5 simulated seconds. Generated routes included turning left from Nicolina onto Aleea Rozelor, making a U-turn on that side street and returning to Nicolina. Among the 270 Nicolina-entry journeys actually submitted by that time, 100 used this loop. These inferred U-turn links made cheap lane-position shortcuts and extra route alternatives; their traffic filled the local junction and blocked city-bound cars before the otherwise empty main road.

The center vehicle `city-car-424` was on a minor left-turn connector and yielded to priority traffic. `city-car-464` blocked a crossing while waiting behind `city-car-396` on a side-street loop; following cars were blocked by these vehicles. Empty space on the city-bound road did not remove those route/priority obligations. Traffic lights were off throughout this state.

Generator version `city-weights-v3-through` excludes SUMO connections marked `dir="t"` from both edge and lane route searches. The road network remains intact; generated through journeys no longer use immediate U-turn movements. At the same time and seed, SUMO arrivals rose from 641 to 1,919, pending entries fell from 450 to 5, and teleport starts fell from 59 to 4. City-bound Nicolina held 54 vehicles, with none on its opposite road. A regression checks this full morning load at 2,500 seconds.

An extended 8,000-second run recorded 5,543 arrivals and 73 teleports, so this does not establish congestion-free or calibrated traffic at the estimated peak rate. The correction removes the documented artificial loop blockage; remaining capacity, lane and priority behavior still depend on the imported network and external control. Old generator-version runs stay inspectable but require a new run to use corrected routing.
