# Podu Roș source preparation

Status: source evidence for the Podu Roș projects, including the trained [morning V3 project](../../../../experiments/drafts/podu-ros-neural/README.md). These files remain outside the simulator input project.

## Verified local study and released data

Cleju and Catargiu's [2026 Sensors study](https://www.mdpi.com/1424-8220/26/10/3027), DOI [10.3390/s26103027](https://doi.org/10.3390/s26103027), studies Podu Roș specifically. The [original PDF](https://mdpi-res.com/d_attachment/sensors/sensors-26-03027/article_deploy/sensors-26-03027.pdf) was downloaded and page 10 rendered for verification on 2026-10-11. Section 2.4 and Table 1 report:

- Recording: 16 July 2024, 19:09–21:55, stated as 166 minutes; accompanying prose rounds this to approximately 165 minutes. Timezone is not stated.
- Site/reference coordinates: 47.15210° N, 27.58801° E; a pole-mounted camera supplies a single view. This is the northern bridge/ingress portion of the broader Podu Roș junction, not a claim that every roundabout approach is visible.
- Dataset: 5,378 selected complete car trajectories after preprocessing. The pipeline excludes incomplete tracks and non-car classes. Section 2.4 on pages 11–12 explains image-to-GPS calibration and detection/occlusion limitations. Table 2 contains anomaly-class counts, not counts of turning movements or hourly demand.

The authors' [dataset documentation](https://trafalert-project.gitlab.io/TrafAlert/papers/trajoutlierdet.html) links the [original manually annotated dataset folder](https://drive.google.com/drive/folders/1Yz34Eonhiy-evEwFb3AvbO2IZ21F9OIS?usp=sharing). We downloaded its raw Parquet and selected-ID text list, rather than executing its pickle files. The raw release has 2,452,378 rows of image detections, including other classes and incomplete tracks. It has image bounding boxes, track IDs, region labels and processing timestamps; no GPS columns. All 5,378 IDs in the selected list are present and have car class 2.

## Prepared numerical evidence

- `observed-track-starts.csv`: count each selected ID once, at its first `kafka_timestamp`; 11 complete 900-second bins and a final 98.454-second bin. Counts sum to 5,378. `relative_weight` is `(bin_count / bin_duration) / (5378 / 9998.454)`; its duration-weighted mean is one.
- `selected-track-endpoints.csv`: first/last image positions for the same selected IDs, using the authors' implemented point convention: 50% box width and 10% height up from the bottom, equivalently 90% height down from the top (`y = 0.1 * top + 0.9 * bottom`). Their `add_centroid_xy_v2` function with weights `[0.5, 0.1]` establishes this convention. These are observations in image space, not authored simulation routes or road counts.
- `camera-calibration.json`: original authors' `PoduRos5_20240805.json`, identified explicitly for this raw release in their [mapping code](https://gitlab.com/trafalert-project/TrafAlert/-/blob/paperoutlierdet/trafalert/utils/mapping.py). Its camera coordinate is 47.15197798° N, 27.58837884° E. Geographic projection and matching to newer OSM roads are derived estimates with calibration and map-change uncertainty.
- `extraction-summary.json`: download checksums, schema totals, timestamp bounds, and the two distinct sample-throughput calculations.
- `sources.json`: primary URLs and locators, including download IDs for repeat extraction.
- `extract.py`: deterministic extraction from the raw Parquet and ID list; requires Python and `pyarrow`. Run `python extract.py RAW.parquet IDS.txt OUTPUT_DIRECTORY`.

The Parquet and ID list remain temporary downloads; their URLs, sizes and SHA-256 are retained. The source's public accessibility does not establish a separate data redistribution license. No raw pickles, videos or Parquet binaries are copied into the repository.

## Timing and demand assumptions

The raw processing epoch spans **2024-08-27 10:29:13.857–13:15:52.311 UTC**, whereas the paper dates filming to **16 July**. `start_timestamp` is constant, so it is not a per-track start time. The authors' preprocessing code uses `kafka_timestamp` directly; the released repository does not establish the upstream mapping to video presentation timestamps. There are 249,457 distinct frame timestamps, predominantly about 40 ms apart, with processing jitter and occasional larger gaps. Their total elapsed span, 9,998.454 seconds, is close to the reported 166-minute video duration but is not proof of exact capture-time synchronization.

A project may **estimate** recording clock as 19:09 plus elapsed processing time. This gives an approximate end at 21:55:38.454 and supports an explicitly labelled sample-derived evening preference curve. Its precise bin-to-filming-time correspondence remains unverified. Outside this short evening window, flat mean weights are extrapolations; no full local 24-hour curve was obtained. The existing PMUD three-site daily curve must not be relabelled as a Podu Roș measurement.

`5378 / 166 * 60 = 1943.855422` complete tracks/hour is a **selected camera-sample throughput reference**, not a measured arrival rate for the full intersection, a number of distinct city journeys, or vehicle density. Normalizing using the raw processing span gives 1936.379364 tracks/hour instead. If the project uses the first value for its generation baseline, that conversion is an operational approximation. Unknown camera coverage, excluded incomplete tracks, missed detections, track fragmentation and absent vehicle classes prevent treating it as calibrated junction demand. Converting coordinates to an edge preference does not resolve those missing observations.

## Camera-to-road preference estimates

`map_tracks.py` applies the authors' camera calibration to every 25th detection within each selected track. It uses the same image point as their preprocessing (50% bounding-box width, 10% top / 90% bottom height), converts GPS into SUMO's UTM zone 35 coordinates and network offset, then matches the nearest passenger-lane segment. A match requires distance at most 12 m and a direction difference at most 45 degrees. Direction uses the previous and next sampled points; displacements under 2 m are excluded. These thresholds and sparse sampling are our preparation assumptions. Raw detections are not smoothed, and neither calibration nor mapping has been independently validated against ground-truth trajectories.

Count each selected track at most once on each inferred edge, using its first matched sample. `mapped-edge-track-starts.csv` contains those inferred counts in the same 12 processing-clock bins. `spatial-estimates.csv` records support and the model branch for all 106 ordinary edges; `mapping-summary.json` records settings, dependency versions, and match totals. Some tracks contribute to several edges; adding these edge counts does not give a distinct-journey total.

The procedure assigns 5,377 of 5,378 selected tracks somewhere in the map and produces 15,435 distinct track/edge matches across 16 edges. Fourteen edges have at least 50 matched tracks and use camera-derived preference estimates; the other 92 use passenger-lane count multiplied by `max(1, priority + 1)`, normalized by the graph's maximum supply of 48. The 50-track threshold is a declared modeling choice. Low support or no match does not establish an empty road. Median matched distance is 2.65 m and the 90th percentile is 7.90 m; these distances are diagnostics, not uncertainty bounds or proof of correct road assignment.

For the 14 supported edges, each evening preference is `(matched tracks in bin / bin duration) / (2143 / 9998.454)`. The common reference 2,143 is the largest whole-sample mapped edge count. This preserves differences between roads and their sample time patterns. Outside the observation window, the supported edge's preference is its whole-sample count divided by 2,143, an explicit flat extrapolation. Remaining roads use their lane/priority preference multiplied by the overall sample temporal curve during the window and flat lane/priority preference outside it. Project weights are rounded to six decimals. See the project README for the resulting operational model.

To reproduce the mapping after `extract.py`, install the dependency versions in `mapping-summary.json`, then run:

```sh
python map_tracks.py RAW.parquet IDS.txt ../../../../experiments/drafts/podu-ros/network.net.xml .
```

Run this from the source folder. These Python utilities process research evidence before import; the simulator remains C#/.NET with SUMO and reads only the prepared project. The resulting preferences are **model estimates informed by a camera sample**, not measured per-road densities or validated directional counts. Current OSM road geometry also postdates the 2024 recording.

## Other primary evidence and exclusions

[PMUD 2023](https://vision4iasi.ro/wp-content/uploads/2023/11/PMUD-Iasi-FINAL.pdf), printed/PDF page 89, lists the Socola–Splai Bahlui Mal Drept–Sfântul Lazăr–Șoseaua Națională–Nicolina junction among its three 24-hour sites. Its Figure 3.23, page 90, combines those sites; underlying Podu Roș hourly/turning annex tables have not been obtained. [Orange's March 2023 project description](https://5glab.orange.ro/en/5g-for-connected-intersections-specific-to-cities-of-the-future-the-5g-smart-intersection-solution/) independently documents local camera deployment but supplies no traffic-count series.

The suggested [arXiv:2011.09286 paper](https://arxiv.org/pdf/2011.09286) is **excluded**: section 2.1, PDF page 5, locates its measurements at Gonghe Road/Guangzhong Road in Shanghai, China. Its pollutant measurements cannot supply Podu Roș traffic density.

`source.osm.gz` and `network-conversion.log` retain the separately acquired OSM map input and conversion diagnostics. Road lanes, priorities, vehicle permissions and generated signal programs are map/conversion inputs, not survey observations; the project README records their preparation. No trustworthy per-edge vehicle-density observations were recovered.
