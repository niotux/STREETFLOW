# Iași PMUD source draft

These files are evidence inputs for a future experiment, **not a runnable or calibrated experiment project**. No vehicle routes, origin–destination weights, or per-edge measurement values have been invented.

## What was extracted

- `measurements.csv`: 24 hourly bars from Figure 3.23, visually read and rounded to approximately 100 vehicles. Each row is the **aggregate across three survey sites**, not an individual road flow or count of unique journeys.
- `profiles.csv`: the same hourly curve divided by its mean (7020.833333). Mean relative weight is 1; it can shape independently chosen scenario traffic over the day. It must not establish absolute network demand by itself.
- `peak-bars.csv`: four approximate Păcurari–Canta bars from Figure 3.24. The figure caption mentions AM/PM, but the visible chart does not label those groups or specify interval duration/units. They are deliberately excluded from machine-readable hourly demand constraints.
- `vehicle-categories.csv`: rounded chart percentages from Figures 3.25–3.26. Weekend percentages total 99; zero labels do not establish absence. Using these for a simulation fleet requires explicit normalization and handling of bicycles/trams.
- `locations.json`: version 1 survey names and a sum group containing the three sites. Survey members are explicitly `unmapped` until verified; there are no fabricated edge IDs.
- `sources.json`: version 1 provenance, dates, pages, and caveats. Surveys were conducted in May 2021; the report was published in 2023. PDF size and SHA-256 are retained in source notes.

Source: [Iași PMUD report](https://vision4iasi.ro/wp-content/uploads/2023/11/PMUD-Iasi-FINAL.pdf), printed/PDF pages 89–91. The original PDF is not duplicated in the repository. It describes directional counts and annexes, but those underlying annex tables have not been obtained.

## Original-report verification

On 2026-10-11, the published PDF was downloaded again and checked against the recorded source: 21,217,528 bytes, SHA-256 `094fd0197bc0f02846f3a5d66653300bc05cd4f6bcded3b3f1cc5ea19e67b6b6`. Printed page numbers match the PDF's one-based page numbers here; Figure 3.23 is therefore PDF page 90 (zero-based page index 89).

- Page 89, section 3.2.4.1, dates the video-based traffic counts to May 2021 and states that surveys covered both weekdays and weekends. It lists three 24-hour count sites and describes recording permitted turning movements by vehicle category. It does not identify which day type Figure 3.23 represents.
- Page 90, section 3.2.4.2, Figure 3.23, labels 24 consecutive one-hour bins from 00:00–01:00 through 23:00–24:00. Inspection of the rendered original confirms the existing CSV's overnight minimum, daytime plateau, 13:00–14:00 maximum, and evening decline at the precision of an approximate visual extraction. No hourly values were changed. The chart has no numeric bar labels or explicit vertical-axis unit label; interpreting values as vehicle counts in each labelled hourly interval follows the caption and surrounding vehicle-flow discussion. This is not a separately verified rate or directional count table.
- The same page says hourly distributions and category breakdowns are provided in an annex. Those raw annex tables have not been obtained. The nearby June–July 2021 dates describe origin–destination interviews, a separate activity from the May traffic counts.

Arithmetic checks confirm 24 contiguous bins covering 0–86400 seconds, a sum of 168,500 approximate counted passages, mean 7,020.833333, and normalized weights matching `value / mean` to six decimal places. Their rounded mean is 0.9999999583. The chart is used as one combined three-site curve; the report excerpt does not state a precise site/day aggregation formula or extraction uncertainty. Rounding to 100 vehicles describes the transcription method, not a guaranteed error bound. No site split, unique-trip total, or per-edge observations can be recovered from these bars alone.

## Applying this to the graph

Use the hourly profile as an approximate temporal shape when preparing explicit per-edge traffic profiles. Spatial estimates must be documented and materialized in the project before it runs; the simulator reads those weights to generate legal journeys. The project does not supply a prescribed route list or endpoint pairs. Do not divide the aggregate into three equal site measurements and do not interpret its daily sum (168500) as the number of distinct trips to create. See [the preparation and review task](../../../simulator/experiment-integration-plan.md#2-prepare-source-data-and-the-experiment-projects-with-subagents).

The existing `scenarios/iasi-central/network.net.xml` has a local bounding box of 2767.61 × 2710.11 metres. Transforming its UTM zone 35 bounding-box corners gives approximately longitude 27.5564–27.5932, latitude 47.1502–47.1744. The network's `origBoundary` is the larger original source bounding box and does not imply all that area is present. Survey-site coverage still needs verified geometry mapping; this draft does not claim to cover every surveyed location.

The Poisson arrival model, spatial route weights, exact signal programs, and absolute experiment arrival volume will be experiment assumptions unless independently sourced. Keep them separate from these measurements.
