# Podu Roș intersection project

Project accepted in task 2 on 2026-10-11. Backend import/map loading is available; continuous generation remains pending.

This is a separate real-map project around the Piața Podu Roș roundabout, its bridge, and the Nicolina, Socola, Bahlui/Mangeron, Palat, and neighboring approaches. It is not the tiny synthetic junction or a renamed central-city network. The old OSM extract starts at latitude 47.155 and omits the roundabout; a fresh local OpenStreetMap extract was obtained for this project.

The network contains 106 passenger-usable ordinary directed edges in one weak connection component, 162 ordinary connections, one roundabout, and ten signal programs. Every usable edge has one explicit profile with 14 contiguous intervals covering the day. The converted map spans approximately 758 × 718 metres. Directed reachability still constrains journeys: weak connectivity alone does not mean every endpoint pair is usable. SUMO owns lanes, junction connectors, movement, and signals. Signal timing and junction inference are conversion estimates, not verified local timing plans. No vehicles or routes are authored. Detectors are an empty reserved array. The starting city time is 19:09, with a 0.5-second engine step; the daily profiles repeat until stopped. This default opens at the beginning of the approximate recorded-window replay, so its local variation is visible immediately.

[Map preview](../../../assets/maps/podu-ros.svg).

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

Review [the accepted format](../../../docs/simulator/experiment-project-format.md) and [the task plan](../../../docs/simulator/experiment-integration-plan.md).

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

To reproduce `demand.json`, read [spatial-estimates.csv](../../../docs/research/data/podu-ros/spatial-estimates.csv) for all 106 edge identifiers, support basis, and outside-window values; use [mapped-edge-track-starts.csv](../../../docs/research/data/podu-ros/mapped-edge-track-starts.csv) for the 14 supported edges and [observed-track-starts.csv](../../../docs/research/data/podu-ros/observed-track-starts.csv) for the others. Add the two flat outside-window intervals and the 12 recorded-proxy intervals at `68940 + elapsed_seconds`, applying the formulas above and rounding weights to six decimals. Fractional seconds preserve the final bin's duration. The profile must reference the exact map edge identifier, not the road's display name. Journey bounds of 100–2,000 metres, a candidate limit of 1,000, three route alternatives, and a 1.5 detour ratio are operational estimates for this small map.
