# TACO: traffic observations and physical devices

TACO is the provider-neutral interface for an active world, its map, physical stoplights, and traffic measurements. Balthasar and Casper implement it; Melchior selects either provider by URL. TACO is an interface exposed by each provider, not a separate service. Pi-to-Casper ingestion is a separate authenticated contract.

Public routes are relative to the configured provider URL and start with `/api/v1/taco`. JSON uses `snake_case`. Shared transport types are `TacoSession`, `TacoMap`, `TacoDevice`, `TacoDeviceState`, and `TacoDeviceCommand` in `components/contracts/Streetflow.Traffic.Contracts`.

## Discovery and map

| Request | Result |
| --- | --- |
| `GET /api/v1/taco/session` | `{id, world_id, revision, project_id, state}` for the active session |
| `GET /api/v1/taco/sessions/{id}/map` | `{world_id, revision, attribution, coordinate_system, lanes, devices, detectors}` |
| `GET /api/v1/taco/sessions/{id}/devices` | Array of `{id, state}` for configured physical devices |
| `GET /api/v1/taco/capabilities` | Traffic, signal-control support, and source |
| `GET /api/v1/taco/sessions/{id}/traffic` | Latest lane counts with street/intersection/stoplight mapping, timestamps, status, and measurement method |
| `GET /api/v1/taco/sessions/{id}/queues` | Binary queue frame: queued vehicles per controlled lane of every device (see [Queue frames](#queue-frames)) |

Traffic uses shared TacoTraffic/TacoLaneTraffic types. Each response carries session/world/revision, source, time_basis, and sequence. Camera lanes preserve observed_at and age_seconds independently; stale/offline values are null. Camera queue estimates use image motion and dwell. Balthasar city snapshots expose visible lane counts only when complete, with unsupported crossing/speed null and method simulation_lane_snapshot (simulation_detector on detector lanes). Controlled lanes also report queued_vehicle_count, using the same approach count as the binary frame. Street/intersection mapping for those city snapshots remains explicitly unmapped; no camera geometry is inferred.

Casper advertises signal_control=false and returns unknown device states. Physical commands return 501 until a controller adapter exists. Synthetic camera demo data uses measurement_method=synthetic_demo and is labelled in both frontends.

This rename changes public routes from /api/v1/tlc to /api/v1/taco, .NET Tlc types to Taco, and Logistics__TlcApiBaseUrl to Logistics__TacoApiBaseUrl. Update callers/configuration together; old routes are not aliases. Saved provider addresses retain their format.

With no active session, discovery returns null identity fields and `state: "none"`. The simulator exposes running and paused city sessions. `project_id` is explanatory simulator context; commands and map pairing use session ID, world ID, and infrastructure revision.

TACO covers vehicle traffic only: sidewalks, crossings and walking areas are not published in maps or traffic. Lanes contain `id`, `shape: [{x,y}]`, and `upstream_lane_id` when exactly one lane feeds them (null otherwise); a queue extends back along that chain. Devices contain `id`, `label`, `x`, `y`, and `lane_ids`. Coordinates in the current provider are local metres. Devices include only authored physical controls: no theoretical movement list, SUMO controller IDs, or link indices are included in their records.

The current map inventory is static for a configuration revision. Device state is dynamic. States are `off`, `red`, `red-yellow`, `yellow`, and `green`. `mixed` means mapped lamps currently disagree; `unknown` means their actual state could not be resolved. A configured device always produces one map marker and one state record, regardless of its number of mapped movements or lanes.

## Queue frames

`GET /api/v1/taco/sessions/{id}/queues` returns `application/vnd.streetflow.taco-queues`, a little-endian binary frame small enough to poll several times a second:

| Field | Type |
| --- | --- |
| magic | 4 bytes, `TQ01` |
| sequence | int64 |
| time | float64; simulation seconds (simulator) or Unix seconds (Casper); NaN when unknown |
| available | uint8, 0 or 1 |
| device count | uint16, in the map's `devices` order |
| per device | uint8 lane count, then one uint16 vehicle count per lane in that device's `lane_ids` order; `0xFFFF` means unknown |

Positions are not sent; the frame only makes sense with the map of the same session and revision. `TacoQueueFrame` in the contracts project encodes and decodes it.

The simulator counts every vehicle on each controlled lane's approach, moving or stopped: from the stop line backwards along `upstream_lane_id`, up to 250 m. Casper reports its camera queue estimate for each lane.

## Commands

`PUT /api/v1/taco/sessions/{id}/devices/{deviceId}` accepts:

```json
{
  "action": "green",
  "world_id": "four-way",
  "revision": "the-revision-returned-by-discovery-and-map"
}
```

All three fields are required. Supported actions are `on`, `off`, `red`, `red-yellow`, `yellow`, and `green`. `on` enables at red; `off` restores original road priority. A successful response is the applied physical-device state, for example `{ "id": "north", "state": "green" }`.

The provider resolves each device's private bindings and applies the change to all mapped movements together. Session, world, and revision checks prevent a command composed for an old map from controlling a new scenario, including scenarios with identical device names. No public command accepts `link_index`.

The simulator serializes writes/readback with engine steps. Once a write begins, it completes even if the HTTP caller disconnects. A caller with an interrupted response should read device state before retrying. Commands set explicit states and do not advance the simulation clock. There are no automatic phase cycles or clearance timings in this manual-control contract.

Errors use `{code, message}`. Invalid commands return 400, unknown devices/sessions return 404, and stopped sessions or mismatched world revisions return 409. Logistics preserves provider errors and reports connectivity failures as 502/504.

## Logistics discovery and caching

The Logistics backend checks the provider's active session every 1.5 seconds, including while its page is closed. It loads a changed world's map and keeps one bounded cached map keyed by provider URL, world ID, and revision. New sessions on unchanged infrastructure reuse it. Traffic-weight changes alone do not require geometry transfer.

The Logistics page also refreshes its session identity and device states every 1.5 seconds, including when no session is selected. It follows new sessions automatically and reuses its own bounded map cache for the same provider/world/revision. A changed world, revision, or provider invalidates the old view before new commands are enabled. Map geometry is not fetched every frame. Provider failure disables commands; recovery is retried automatically.

Logistics browser requests use `/melchior-api/session` and `/melchior-api/sessions/{id}/{map|devices|traffic|queues}` through the frontend proxy. Melchior polls queue frames every 250 ms and draws each queued vehicle behind its stop line, using the same map renderer and vehicle drawing as Balthasar; it has no other source of vehicle positions. The Logistics service exposes the equivalent `/api/v1/...` paths and forwards device commands. It does not load SUMO XML or own a separately authored map.

`Logistics__DiscoveryIntervalMilliseconds` defaults to 1500. Cached maps are in memory; saved provider addresses remain on the Logistics data volume. PostgreSQL is not part of this control path.

## Migration

The former taco integration used simulator `/city-runs/.../signals` endpoints and raw signal indices. Logistics now uses `/taco/.../devices` exclusively. Technical city-run signal endpoints remain for existing simulator diagnostics and tests; they are not the physical-device taco surface.

Both project formats accept [physical-device mappings](../simulator/traffic-light-devices.md). Projects without mappings still provide their roads but publish no inferred hardware. Edit the registered project folder, refresh from disk, and load a new simulation to apply a mapping. Saved run snapshots are preserved.
