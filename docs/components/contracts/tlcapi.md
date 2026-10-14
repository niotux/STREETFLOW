# Traffic Light Control API: physical devices

The provider-neutral tlcapi contract publishes the active world, its map, and explicitly configured physical traffic-light devices. The simulator implements it now. A future infrastructure gateway can implement the same interface without SUMO or simulator run APIs. Detection observations remain a separate contract.

Public routes are relative to the configured provider URL and start with `/api/v1/tlc`. JSON uses `snake_case`. Shared transport types are `TlcSession`, `TlcMap`, `TlcDevice`, `TlcDeviceState`, and `TlcDeviceCommand` in `components/contracts/Streetflow.Traffic.Contracts`.

## Discovery and map

| Request | Result |
| --- | --- |
| `GET /api/v1/tlc/session` | `{id, world_id, revision, project_id, state}` for the active session |
| `GET /api/v1/tlc/sessions/{id}/map` | `{world_id, revision, attribution, coordinate_system, lanes, devices}` |
| `GET /api/v1/tlc/sessions/{id}/devices` | Array of `{id, state}` for configured physical devices |

With no active session, discovery returns null identity fields and `state: "none"`. The simulator exposes running and paused city sessions. `project_id` is explanatory simulator context; commands and map pairing use session ID, world ID, and infrastructure revision.

Lanes contain `id` and `shape: [{x,y}]`. Devices contain `id`, `label`, `x`, `y`, and `lane_ids`. Coordinates in the current provider are local metres. Devices include only authored physical controls: no theoretical movement list, SUMO controller IDs, or link indices are included in their records.

The current map inventory is static for a configuration revision. Device state is dynamic. States are `off`, `red`, `red-yellow`, `yellow`, and `green`. `mixed` means mapped lamps currently disagree; `unknown` means their actual state could not be resolved. A configured device always produces one map marker and one state record, regardless of its number of mapped movements or lanes.

## Commands

`PUT /api/v1/tlc/sessions/{id}/devices/{deviceId}` accepts:

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

Logistics browser requests use `/melchior-api/session` and `/melchior-api/sessions/{id}/{map|devices}` through the frontend proxy. The Logistics service exposes the equivalent `/api/v1/...` paths and forwards device commands. It does not load SUMO XML or own a separately authored map.

`Logistics__DiscoveryIntervalMilliseconds` defaults to 1500. Cached maps are in memory; saved provider addresses remain on the Logistics data volume. PostgreSQL is not part of this control path.

## Migration

The former tlcapi integration used simulator `/city-runs/.../signals` endpoints and raw signal indices. Logistics now uses `/tlc/.../devices` exclusively. Technical city-run signal endpoints remain for existing simulator diagnostics and tests; they are not the physical-device tlcapi surface.

Both project formats accept [physical-device mappings](../simulator/traffic-light-devices.md). Legacy projects without mappings still provide their roads but publish no inferred hardware. Old imported projects and run archives are preserved.
