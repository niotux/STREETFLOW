# Melchior logistics controller

Melchior connects to a provider's [physical-device taco](../../docs/components/contracts/taco.md), loads its world configuration, and automatically enables the provider’s existing signal timing programs for each loaded simulation. Manual traffic-light commands remain available. Long-road spillback protection and rush-hour demand weighting are available for prepared static timing plans.

## Run and connect

```sh
docker compose up --build -d --wait
```

Open **http://localhost:8090/melchior/**. In Docker, the Connection address is **http://simulator**, port **8080**. The Logistics server reaches this address; localhost inside its container refers to Logistics itself. A future compatible infrastructure provider can replace the simulator at that address.

The backend discovers active worlds every 1.5 seconds even with the page closed. The page automatically follows newly loaded sessions. Geometry is downloaded on world/configuration changes and reused when a new session uses the same infrastructure. The address, port, world ID, and revision identify the paired configuration.

Saved provider settings survive restarts in `logistics-data` and take precedence over `Logistics__TacoApiBaseUrl`. The map cache is bounded to one world in memory and keyed by provider URL/world/revision. No PostgreSQL storage is involved.

## Physical devices

The map and dropdown show only configured physical controls. A device can operate several lanes/movements together. Select it by name, search, or click its marker; controls change the whole device. There are no per-lane or SUMO movement controls in Logistics.

New sessions automatically enter timed control after backend discovery, including while the browser is closed. The **Automatic light cycling** switch returns the current session to manual control, holding every controller’s current lamps. Manual commands are disabled while automatic timing is active. A manual choice remains in effect for the current Melchior process/session; loading a new simulation or restarting Melchior enables automatic timing again.

Automatic timing prepares the project’s selected static programs and road-storage graph once when loading the simulation. Melchior sends a declarative traffic policy; the simulator executes it on every simulation step, including at accelerated speeds. The phase order, yellow/all-red transitions, permissive movements and short pedestrian clearance stages are retained. Green walk stages keep a crossing-length-based minimum. Timing follows simulation seconds, so pause, stepping and speed controls remain synchronized. Generated project timings are assumptions, not calibrated real-world plans. Providers without automatic-control support, including Casper, receive no automatic commands.

In manual mode, `on` enables at red. Red, red + yellow, green, yellow, and off are explicit commands. States hold until another command or a session restart. Off restores original road priority. Unknown states, unavailable providers, and changed sessions disable commands until refreshed. Commands carry the map's world ID and revision.

Use the prepared [four-way physical-device projects](../../docs/components/simulator/traffic-light-devices.md). Unmapped older projects still run, but their theoretical SUMO signals are not presented as real devices.

## API and settings

Melchior listens on **http://localhost:8081** by default. The frontend proxies it under `/melchior-api`. OpenAPI is `/openapi/v1.json`.

| Endpoint | Behavior |
| --- | --- |
| `GET /health` | Service health, automatic-ready mode |
| `GET /api/v1/connection` | Saved provider URL |
| `PUT /api/v1/connection` | Save `{ "base_url": "http://simulator:8080" }` |
| `GET /api/v1/session` | Discover active session/world/revision |
| `GET /api/v1/sessions/{id}/map` | Cached physical-device map |
| `GET /api/v1/sessions/{id}/devices` | Live device states |
| `GET /api/v1/sessions/{id}/signal-mode` | Current mode and automatic support |
| `PUT /api/v1/sessions/{id}/signal-mode` | Select `automatic` or `manual`, with world ID and revision |
| `PUT /api/v1/sessions/{id}/devices/{deviceId}` | Explicit action with world ID and revision |

Configure `Logistics__TacoApiBaseUrl`, `Logistics__ConnectionFile`, `Logistics__RequestTimeoutSeconds` (default 15), and `Logistics__DiscoveryIntervalMilliseconds` (default 1500). Native execution defaults to localhost:8080 and stores its connection in `.data/logistics/connection.json`. Use `--urls` or `URLS` to change its listener.

For native execution:

```sh
dotnet run --project components/streetflow/Streetflow.Logistics
```

See the [frontend guide](../frontend/README.md) and [component boundary](../../docs/components/streetflow/README.md).

## Downstream protection and A/B weighting

Melchior’s traffic rules default to enabled. **Hold red when the exit is full** uses long approaches as queue buffers: at least 100 m along a lane’s approach and about 20 vehicles of aggregate storage. Storage assumes 7.5 m per passenger car including gap. The road connections, receiving corridors, lane capacities and signal groups are prepared ahead of time (AOT preparation, not a C# native-AOT build).

A receiving corridor is full at 90% estimated storage occupancy, or when a stopped vehicle leaves insufficient space at its entrance. Buffered movements close with their yellow clearance before red. They reopen below 75% occupancy with entrance space and a short hold interval, avoiding rapid color oscillation. Checks follow individual movements, so unblocked turns remain available. A downstream corridor follows unique unsignalized continuations and stops at a branch or the next controlled stop line. This is a local congestion rule, not a guarantee that network-wide gridlock is impossible.

**Weight rush-hour green time** runs during city-clock 07:00–09:00 and 16:00–19:00, repeating daily. A and B are stable vehicle-serving groups from the project’s phase plan, named in the selected light’s details. Recent arrivals within 60 simulation seconds plus twice the stopped queue form a demand score. A group whose receiving movements are all blocked has its demand deferred, so it cannot take extra green time away from a direction that can actually move. Automatic `k = clamp(sqrt((A + 1) / (B + 1)), 0.2, 1.8)` is greater than 1 when A has higher demand and below 1 when B has higher demand.

Weights are `A × k` and `B ÷ k`. Eligible green durations are normalized to the original green-time budget; short clearance stages remain unchanged. Minimum green service is 10 seconds (or the original duration if shorter), increased where necessary for the project’s pedestrian crossings. Extreme factors therefore cannot remove the other direction’s entire green. Additional groups beyond A/B retain neutral weights. Changes take effect at a cycle boundary; blocked exits are checked every step regardless of rush hour.

Leave **Multiplier k** blank for automatic demand weighting, or enter an override between 0.2 and 1.8. For two 42-second green stages and unchanged 3-second yellow stages, `k=1.5` gives about 58.2 s to A and 25.8 s to B. `k=0.5` gives 16.8 s to A and 67.2 s to B. Off peak, the multiplier is 1. The live readout shows A/B names, demand scores, green budgets, downstream occupancy and held movements. Demand scores are pressure estimates, not observed hourly flow measurements.

Traffic-rule preferences are saved alongside the provider connection in `connection.json.signals.json` and applied to new sessions. The mode command may include `policy: {prevent_spillback: true, weight_demand: true, multiplier: null}`. Invalid factors return 400. Projects without a supported static plan continue using the provider’s native timing and expose `adaptive_supported: false`; adaptive settings are disabled there. Physical infrastructure and camera-only providers receive no inferred control plan.
