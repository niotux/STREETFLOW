# Podu Roș intersection project

Project accepted in task 2 on 2026-10-11. Backend import, continuous generation, and the city UI are available; manual runtime review is deferred.

This is a separate real-map project around the Piața Podu Roș roundabout, its bridge, and the Nicolina, Socola, Bahlui/Mangeron, Palat, and neighboring approaches. It is not the tiny synthetic junction or a renamed central-city network. The old OSM extract starts at latitude 47.155 and omits the roundabout; a fresh local OpenStreetMap extract was obtained for this project.

The network contains 106 passenger-usable ordinary directed edges in one weak connection component, 162 ordinary connections, one roundabout, and ten signal programs. Every usable edge has one explicit profile with 14 contiguous intervals covering the day. The converted map spans approximately 758 × 718 metres. Directed reachability still constrains journeys: weak connectivity alone does not mean every endpoint pair is usable. SUMO owns lanes, junction connectors, movement, and signals. Signal timing and junction inference are conversion estimates, not verified local timing plans. No vehicles or routes are authored. Detectors are an empty reserved array. The starting city time is 19:09, with a 0.5-second engine step; the daily profiles repeat until stopped. This default opens at the beginning of the approximate recorded-window replay, so its local variation is visible immediately.

[Map preview](../../../assets/maps/podu-ros.svg).

## Intersection terminals

[`terminals.json`](terminals.json) marks **22 entry lanes and 27 exit lanes**, at 23 exterior approach nodes on 29 directed edges. Each point refers to an existing ordinary lane; no artificial nodes, fixed turns, network changes, or ring origins are introduced. All passenger-permitted lanes at the selected exterior road ends receive a point. Entry points sit 6 metres from their lane's start, so a default 5-metre vehicle has room behind its front; exit points sit at the lane's end. The shortest selected entry lane is 7.53 metres long, on the west-boundary Cantemir approach, rather than an interior 20-centimetre splitter.

Boundary selection uses the retained OSM source and the documented conversion box: a road-end junction has one distinct neighboring ordinary junction, and its original geographic location lies outside that box. The 23 nodes comprise 17 SUMO `dead_end` junctions and six `priority` junctions: paired bidirectional ends on Sfântul Lazăr, Traian, Socola, Aleea Rozelor, an unnamed west-side local road, and Palat. The source road continues outside the retained network at these boundaries; the point represents a study boundary, not an actual physical dead end. Inbound-only and outbound-only boundary roads are retained as modeled, without inventing a reverse lane.

| Approach | Entry lanes | Exit lanes |
| --- | ---: | ---: |
| Bulevardul Dimitrie Cantemir | 6 | 3 |
| Strada Sfântul Lazăr | 2 | 2 |
| Strada Traian | 1 | 1 |
| Bulevardul Profesor Dimitrie Mangeron | 2 | 0 |
| Aleea Decebal | 1 | 2 |
| Bulevardul Socola | 2 | 2 |
| Șoseaua Nicolina | 2 | 2 |
| Șoseaua Națională | 0 | 2 |
| Bulevardul Regele Ferdinand I al României | 0 | 3 |
| Aleea Rozelor | 1 | 3 |
| Bulevardul Regele Mihai I al României | 0 | 2 |
| Bulevardul Țuțora | 1 | 1 |
| Strada Palat | 2 | 2 |
| Unnamed exterior local roads | 2 | 2 |

Three interior local ends (`2281135906`, `253486518`, and `726578956`) remain transit/local streets, with no intersection terminal. This selection does not equate every graph dead end with an external entrance or exit. Some exterior ends connect only neighboring local streets rather than the central ring; legal reachability and journey-length limits still decide which terminal pairs can generate journeys. Two entry lanes (`1390424230_0` and `-1068015153#1_1`) need to change lanes before their first downstream connector; their 46.46-metre and 80.35-metre edges provide room, but inferred SUMO connectivity and lane changes remain model limitations. The terminal annotations do not repair or calibrate junction geometry and signal timings.

Reproduce the annotations with `python3 docs/research/data/podu-ros/prepare_terminals.py` from the repository root. The script is preparation tooling; importing or running the project does not require Python. Terminal labels and locations identify the study boundary, not measured approach demand.

## Reproducing the map

The source API request was `https://api.openstreetmap.org/api/0.6/map?bbox=27.5815,47.1465,27.5945,47.1545`, retrieved on 2026-10-11. The selected conversion boundary was longitude 27.5835–27.5915, latitude 47.1480–47.1535, around the roundabout at approximately 47.1512, 27.5875. These are preparation boundaries, not measured traffic locations.

The reproducible input is [source.osm.gz](../../../docs/research/data/podu-ros/source.osm.gz); decompress it before the command below. The uncompressed XML has 2,124,547 bytes and SHA-256 `53bdb131c0ed3b52f8892072c8258cd5cc00190bbce643d19ed905967a84f957`. [Conversion messages](../../../docs/research/data/podu-ros/network-conversion.log) are retained separately from this project.

Convert the retained source with SUMO netconvert 1.28.0:

```sh
netconvert --osm-files source.osm \
  --output-file network.net.xml \
  --keep-edges.in-geo-boundary 27.5835,47.1480,27.5915,47.1535 \
  --keep-edges.by-vclass passenger \
  --remove-edges.by-type highway.footway,highway.cycleway,highway.path,highway.steps \
  --geometry.remove --roundabouts.guess --ramps.guess \
  --tls.guess --tls.discard-simple --junctions.join \
  --output.street-names --output.original-names
```

The OSM API returns complete ways and regional relation members extending beyond its requested box. Consequently, the network's `origBoundary` describes those source objects, not the actual retained map. `convBoundary` and edge geometry describe the resulting local map. SUMO retains intersecting road segments rather than clipping their geometry at an exact rectangular line; a few approach endpoints extend beyond the selection boundary. Conversion uses the source's road direction, lane/access tags, and available restrictions; warnings report incomplete regional relations, removed transit-only items, inferred crossing/junction types, and connection geometry. Those omissions and inferred controls limit fidelity; this is an approximate passenger-road model.

Network attribution: © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright).

## Signal and gridlock audit (2026-10-14)

Following this audit, new city runs use external signal control: **all signals start off**, embedded timing programs remain inactive, and traffic follows the network's priority rules until API commands enable/change a movement. `on` enables red; `red`, `green`, `yellow`, `red-yellow` and `off` directly change SUMO's state and hold until changed. Off restores the original movement priority. The read-only map shows live three-lamp lights; signal control is available only through the API. See the [control API](../../../components/simulator/README.md#external-signal-control). The timing and congestion findings below describe the previous automatic-control behavior of the saved runs.

The embedded signal definitions are **generated assumptions, not measured Podu Roș plans**. Conversion enabled `--tls.guess` and `--tls.discard-simple`, as recorded in the network's header. All ten `<tlLogic>` controllers use `type="static"`, `programID="0"`, `offset="0"`, and a 90-second cycle. Eight alternate two 42-second green groups with 3-second yellow transitions. The joined controller `cluster_335640349_9804878311_9804878312_9804878316` has the same timing over 18 controlled connections. The other joined controller, `cluster_12043430975_2178366591_2613532418_31202497_#2more`, has 24/26/26-second green phases, 3-second yellow transitions and 2/1/2-second all-red clearances. No phase contains SUMO's `u` (red + yellow) state. There is no adaptive controller, measured coordination offset, or external controller in this package; `additional_files` is empty.

SUMO can run because these definitions already supply complete signal programs. Importing roads does not require surveyed signal timings: netconvert generates plausible programs when timings are missing. That is sufficient to execute a model, not to validate it against reality. See [SUMO's traffic-light documentation](https://sumo.dlr.de/docs/Simulation/Traffic_Lights.html).

The service advances SUMO from elapsed time zero. The selected city start time shifts the demand profile and displayed clock; it does **not** align the signal programs with a real time-of-day plan. Under the previous automatic-control behavior, all these programs restarted at the same zero offset on every new run. New runs leave them inactive.

Live snapshots already contain each controller's state string. Its character at a connection's `linkIndex` gives that movement's state: `r` = stop, `y` = yellow, `u` = red + yellow (remain stopped), `G` = priority green, `g` = green with yielding. A green does not overcome a full downstream lane. The city map now exposes the 81 controlled connections at their incoming lane endpoints, with separate lights for different movements on one lane. Hover a light to see its controller, link index, destination lane and current state. Pause and Step to inspect transitions. Dark three-lamp symbols remain visible for off signals and unavailable live states; the stopped view does not invent colors. The display supports `u`: the external API now sets it directly, even though the embedded programs never use it.

Read-only inspection of the saved `podu-ros-terminals` run `1176d3ce22d744dca44b635de4a00e0c` found 145 teleport starts in `engine.log`: **88 jam, 39 yield, 18 wrong lane**. At stop (2,693.5 simulated seconds), the status reported 242 active vehicles, eight pending entries, and an entry blocked for 2,063 seconds. These are evidence of this model's congestion, not measurements of the physical intersection. Frequent blocked lanes include the 17.28-metre ring edge `324784649#1`, the 1.23-metre Palat fragment `277230359#1`, and the 2.74-metre Ferdinand fragment `682623766#0`. Short fragments deserve review; their existence alone does not prove invalid geometry.

City runs currently set SUMO's `--time-to-teleport` to `MaximumBacklogSeconds` (300 seconds by default). SUMO removes stuck cars and attempts to reinsert them farther along their routes. The coordinator counts these starts as `gridlock_recoveries`; that count is not a count of trips completed normally. When an entry has waited longer than the same limit, new submissions pause while SUMO keeps stepping. Demand waiting in the generator can later catch up. The historical runtime test that required continuing movement and positive recovery counts established service continuity, **not** faithful intersection operation. See [SUMO's teleport reasons](https://sumo.dlr.de/docs/Simulation/Why_Vehicles_are_teleporting.html).

Fixing fidelity requires the following evidence and comparisons:

1. Survey actual stop lines, lane-to-lane turns, prohibited movements and signal groups. Review wrong-lane warnings and short ring/splitter fragments against those observations in netedit/sumo-gui.
2. Obtain or observe each controller's phase order, durations, intergreen clearances, red + yellow transitions, offsets and time-of-day/adaptive rules. Author reviewed `<tlLogic>` programs in an additional XML file and reference it in `additional_files`. State strings must match the network's movement indices. `u` must be an engine phase, not an animation inserted by the UI. Example durations should remain explicitly estimated until measured.
3. Calibrate approach counts and turning proportions. The 1,943.855422 vehicles/hour baseline comes from selected camera trajectories, not a measured whole-intersection arrival rate. Most spatial profiles are estimates, and generated routes are not observed origin–destination trips.
4. Compare low-demand and full-demand runs, then separate changes to geometry, control and demand. For diagnostic SUMO runs, disable teleporting (`--time-to-teleport -1`) and record queue lengths, waiting times, normal arrivals, insertion delay and teleport events. The city API currently couples recovery and backlog limits and does not expose a separate teleport setting; use a separate diagnostic harness or add independent run options before making this comparison. Accept a model based on normal throughput and observed queues, not merely because teleporting keeps vehicles moving.

A subsequent boundary check exercised all 27 declared exits with teleporting disabled: every test vehicle arrived and SUMO removed it, leaving no active or pending vehicles. A 3,000-second direct-engine diagnostic also recorded 967 arrivals and no stopped-car samples within one metre of an actual exit lane end. Stopped cars near the lower map boundary were on incoming/interior roads, not on declared exits. This does not validate saturated-network throughput, but it rules out a general missing deletion rule at these exits. New run status exposes cumulative `arrived_vehicles`, displayed beside active cars and waiting entries.

The present evidence establishes generated controls and multiple forms of blockage. It does not isolate the contribution of signal timing, connectivity and demand; that needs controlled comparisons. Adding red + yellow alone cannot resolve the documented wrong-lane and downstream-capacity failures. Preserve the original package and reimport revised inputs under a distinct project ID to keep old runs reviewable.

Review [the accepted format](../../../docs/components/simulator/experiment-project-format.md) and [the task plan](../../../docs/archive/experiment-integration-plan.md).

## Traffic evidence and clock assumptions

This project's local evidence is the authors' 5,378 selected complete car trajectories from a Podu Roș traffic-camera recording on 16 July 2024, labelled 19:09–21:55 (166 minutes). The [source notes](../../../docs/research/data/podu-ros/README.md) distinguish the original paper, downloaded data, and derived files. These are camera tracks filtered for complete trajectories, not all vehicles entering the roundabout, measured road densities, or origin–destination journeys across this map. The paper's camera/reference coordinate (47.15210 N, 27.58801 E) is near the bridge approach, north of the ring, and does not establish full intersection coverage.

[observed-track-starts.csv](../../../docs/research/data/podu-ros/observed-track-starts.csv) counts each selected track once at its first recorded processing timestamp, in 900-second bins and a final 98.454-second bin. The stored timestamps span 9,998.454 seconds and reflect a later processing epoch, not verified recording-clock timestamps. For each bin, `temporal_weight = (track_starts / bin_duration) / (5378 / 9998.454)`. Its duration-weighted mean is 1. Using first appearances measures camera-track starts, not a vehicle's entry to the network.

For demonstration, the prepared profiles place these elapsed bins at city-clock 19:09 (68,940 seconds) plus elapsed processing time, ending at 78,938.454 seconds (approximately 21:55:38). This is an explicitly approximate replay-clock proxy; frame cadence and process jitter do not establish an exact recording-time alignment. Before and after this window, temporal weight is flat 1, an unsupported full-day extrapolation. Daily repetition repeats this single-day approximation; it does not claim observed morning, overnight, or other-day behavior.

`mean_departures_per_hour = round(5378 * 60 / 166, 6) = 1943.855422` uses the paper's recording duration as an operational camera-sample scale proxy. This value is not the calibrated rate for the entire roundabout or every approach. The accepted generator shapes it using the total current stored edge weight relative to that total's full-day mean; candidate route preference uses the distance-weighted mean of edge weights. Poisson arrival timing, generated legal journeys, a passenger-only fleet, and all extrapolations are model assumptions.

## Spatial inference and preparation estimates

Camera image coordinates are not road-edge coordinates. A separate preparation step uses the authors' camera calibration to project sampled track positions into geographic coordinates and geometrically match them to passenger-lane segments. It samples every 25 observations in each selected trajectory, accepts matches within 12 metres with local motion heading within 45 degrees of the lane direction, and counts each `(track, edge)` once at its first accepted sample. These are inferred road associations, not surveyed counts on SUMO edges. Calibration, sampling, camera occlusion, lane geometry, and matching thresholds limit correspondence, and a track can support several consecutive edges.

Matching only uses heading estimates with at least two metres of local displacement. It associates 5,377 selected tracks with at least one edge; the median accepted geometric residual is 2.65 metres and the 90th percentile is 7.90 metres. These residuals describe proximity to the chosen lane segment, not independent mapping accuracy.

Of the 106 ordinary passenger edges, 16 receive any inferred track support; 14 have at least 50 matched trajectories and use local evidence in their profiles. The other 92 use explicit lane/priority estimates, including low-support roads. Lack of mapped support does not prove no traffic: most roads are outside the camera view.

For an edge with at least 50 matched trajectories, divide its inferred per-bin track rate by the largest whole-recording inferred edge rate, `2143 / 9998.454` tracks/second. Thus its observed-window weight is `(matched_tracks_in_bin / bin_duration) / (2143 / 9998.454)`. Outside that window, its weight is `whole_recording_matched_tracks / 2143`, a flat extrapolation of its recorded mean. Different supported roads can therefore have different evening patterns, with their shared scale preserving relative amplitudes.

For every other usable edge `e`, let `L(e)` be its number of passenger-permitted lanes, `P(e)` its integer SUMO priority, and `S(e) = L(e) * max(1, P(e) + 1)`. The maximum supply in this graph is 48. Its observed-window weight is `S(e) / 48 * temporal_weight(bin)` from the global selected-track-start curve; its outside-window weight is flat `S(e) / 48`. These values are unsupported spatial estimates, not mapped measurements. Internal junction connectors have no profiles. All prepared weights are rounded to six decimal places and explicitly stored; runtime code must not regenerate missing values or replace them with a fallback.

Observed-track starts and first mapped edge matches count different events. The source CSVs and geometric association rules remain outside the runnable project; the project contains only the resulting per-edge preferences. This preparation defines neither fixed journeys nor guaranteed observed road density.

To reproduce `demand.json`, read [spatial-estimates.csv](../../../docs/research/data/podu-ros/spatial-estimates.csv) for all 106 edge identifiers, support basis, and outside-window values; use [mapped-edge-track-starts.csv](../../../docs/research/data/podu-ros/mapped-edge-track-starts.csv) for the 14 supported edges and [observed-track-starts.csv](../../../docs/research/data/podu-ros/observed-track-starts.csv) for the others. Add the two flat outside-window intervals and the 12 recorded-proxy intervals at `68940 + elapsed_seconds`, applying the formulas above and rounding weights to six decimals. Fractional seconds preserve the final bin's duration. The profile must reference the exact map edge identifier, not the road's display name. Journey bounds of 40–2,000 metres, a candidate limit of 1,000, three route alternatives, and a 1.5 detour ratio are operational estimates for this small map.

Terminal routing uses explicit lane connectivity across short splitter roads and permits strategic lane changes only where at least 20 m of the current road remains. This is a conservative planning allowance, not a measured driver calibration. Candidate preparation covers viable entrance/exit lanes before filling its remaining budget. The minimum trip distance is now 40 m, allowing the short clipped boundary-to-boundary approaches (about 48 m and 86 m) to participate; the former arbitrary 100 m setting excluded those terminals. Edge profile weights and the camera-derived overall rate are unchanged.
