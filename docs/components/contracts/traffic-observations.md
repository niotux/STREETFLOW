# Traffic observation contract

This proposed contract is shared by the simulated observation producer and the real detector. Streetflow consumes it without depending on SUMO, a camera model, or full simulated vehicle state. Version the schema and map/camera configuration together.

## Counts have different meanings

| Field | Meaning |
| --- | --- |
| `visible_vehicle_count` | Vehicles whose tracked reference point lies in the lane's configured visible region at the frame's observation time |
| `crossed_vehicle_count` | Distinct track passages crossing the oriented counting line in the completed time window |
| `queued_vehicle_count` | Visible vehicles meeting the configured stopped-speed and minimum-duration rule |
| `mean_speed_mps` | Mean speed of eligible visible tracked vehicles at observation time, if calibrated and supported |

Visible count is not a count over the whole incoming road. Queue count is limited by camera coverage and is not a guaranteed total queue length. A crossing count is an event count, not the sum of detection boxes across video frames. Repeated legitimate crossings by the same track are separate passages; deduplicate the same event. Junction totals must avoid double counting overlapping camera regions.

Queue thresholds, visible-region membership, eligible classes, and speed estimation definitions live in the detector configuration. The simulator and real detector must implement the same chosen definitions. Unsupported or missing values are `null`, not zero. An actual measured zero is `0`.

## Identity and time

Use stable `intersection_id`, `approach_id`, `lane_id`, and `detector_id` from a versioned network mapping. A lane describes a physical direction and region; it is not an index guessed from a drawing. Map SUMO lane IDs and camera lane polygons explicitly to those IDs. A turning movement can be observed only when the configured coverage sees the relevant traversal; a future intended turn must not be inferred from the simulator's known route.

Each producer session has a monotonic sequence across its emitted frames, including frames for different detectors. Emit simultaneous detector frames in a stable order. Frames use completed half-open windows `[start, end)`; crossings on the end boundary belong to the next window. Convert underlying SUMO detector behavior to this convention and test boundary cases. Snapshot counts/speeds are sampled at `end`.

Simulation frames carry simulation seconds and a run identity. Real detector frames carry UTC observation timestamps and a detector-session identity. `time_basis` makes the distinction explicit. Optional scenario epoch mapping may aid real-time integration but does not replace simulation time. Receipt time is separate from observation time; delayed delivery must not shift a measurement window.

## Implemented camera ingestion

Pi workers POST CameraObservation to Casper at /api/v1/cameras/{id}/observations with a camera bearer token. It uses the UTC variant described below, plus optional synthetic_demo=true for explicitly labelled demos. The [Casper guide](../../../components/detector/README.md) defines the current image-motion queue estimate, local tracking limits, authoritative camera mapping, and freshness handling. Physical speed remains null. The original simulator stopped-speed definition and the Pi image-motion estimate are distinct measurement methods, exposed by TACO.

## Example simulated frame

```json
{
  "schema_version": "1",
  "source": "simulation",
  "session_id": "run-001",
  "sequence": 24,
  "network_revision": "iasi-pilot-001",
  "detector_config_revision": "camera-config-001",
  "time_basis": "simulation_seconds",
  "window": {"start": 115.0, "end": 120.0},
  "intersection_id": "junction-01",
  "detector_id": "camera-01",
  "status": "ok",
  "lanes": [{
    "approach_id": "northbound",
    "lane_id": "northbound-1",
    "visible_vehicle_count": 12,
    "crossed_vehicle_count": 7,
    "queued_vehicle_count": 9,
    "mean_speed_mps": 1.2,
    "measurement_status": "ok"
  }]
}
```

For real data, `time_basis` is `utc` and window boundaries are UTC timestamp strings; `source` is `camera`. Use a schema variant so units and timestamp types are validated explicitly. The example numbers are illustrative, not Iași observations.

## Missing data and limitations

Publish configured `ok`, `partial`, or `unavailable` measurement status, including lane-specific status when only one region is affected. A total outage carries unavailable/null measurements; a stale cached frame retains its original sequence and observation time. A transport response may indicate staleness, but must not relabel the frame as a new sample.

Include partial-window/coverage notes when relevant. Confidence scores, if added, need an explicit meaning and validation; synthetic error settings do not establish an AI model's confidence. The receiver can detect sequence gaps and decide how to handle missing frames.

Full routes, destinations, city-wide identities, per-car trip histories, and traffic beyond declared coverage are outside this contract. Those remain privileged simulator inspection/evaluation data. Real detector local tracking supports measurement generation without promising cross-camera vehicle identification.

## First contract checks

Verify counting-line direction, lane changes across regions, overlap handling, repeated track passages, exact window boundaries, empty lanes, missing speed, partial coverage, delayed delivery, stream resets, and duplicate frames. Run the same small contract fixtures through synthetic production and later real-detector adapters.
