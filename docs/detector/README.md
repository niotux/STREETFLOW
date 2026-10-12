# Traffic detector product

The detector converts real camera video into traffic observations that Streetflow can consume. It is a separate product and process, with the same [output contract](../contracts/traffic-observations.md) that the simulator implements for Streetflow tests.

## Processing responsibilities

```text
Timestamped camera frames
    -> vehicle detection
    -> local tracking across frames
    -> assignment to calibrated lane regions
    -> crossing events and windowed measurements
    -> traffic observation output
```

A model such as YOLO is a candidate for vehicle detection, not a complete traffic-measurement system. Counting distinct crossings needs tracking and a counting line; lane membership needs configured regions; reliable physical speed estimates need geometry calibration and timing. The detector specification must distinguish what each camera can actually observe.

Camera configuration includes intersection/approach/lane mappings, visible regions, counting lines, time synchronization, supported classes, and output frequency. Quality fields describe coverage, missing data, and measurement status. Track IDs are local to a camera session and must not become a promise of city-wide vehicle identification.

## How the simulator relates to the detector

Initially, the simulator generates measurements from SUMO vehicle state inside configured virtual camera regions. That tests Streetflow's ability to use detector output. It does **not** evaluate detection accuracy, tracking accuracy, occlusion handling, or YOLO performance.

Evaluate the detector separately with real labeled video or a later dedicated synthetic-video system. Recorded detector observations may be replayed to test Streetflow's data handling; aggregate observations alone are insufficient to reconstruct the original road traffic exactly.

## Decisions before a detector specification

Choose camera placement and resolution, required vehicle classes, tracking/counting accuracy, latency, supported hardware, day/night/weather conditions, and what data may be retained. Choose the AI model and stack after establishing those requirements. Existing simulator stack constraints do not select the detector's implementation.

Future code location: [components/detector](../../components/detector/README.md).
