# Melchior logistics controller

Melchior connects to a provider's [physical-device tlcapi](../../docs/components/contracts/tlcapi.md), loads its world configuration, and provides manual traffic-light commands. Automatic traffic optimization remains future work.

## Run and connect

```sh
docker compose up --build -d --wait
```

Open **http://localhost:8090/melchior/**. In Docker, the Connection address is **http://simulator**, port **8080**. The Logistics server reaches this address; localhost inside its container refers to Logistics itself. A future compatible infrastructure provider can replace the simulator at that address.

The backend discovers active worlds every 1.5 seconds even with the page closed. The page automatically follows newly loaded sessions. Geometry is downloaded on world/configuration changes and reused when a new session uses the same infrastructure. The address, port, world ID, and revision identify the paired configuration.

Saved provider settings survive restarts in `logistics-data` and take precedence over `Logistics__TlcApiBaseUrl`. The map cache is bounded to one world in memory and keyed by provider URL/world/revision. No PostgreSQL storage is involved.

## Physical devices

The map and dropdown show only configured physical controls. A device can operate several lanes/movements together. Select it by name, search, or click its marker; controls change the whole device. There are no per-lane or SUMO movement controls in Logistics.

`on` enables at red. Red, red + yellow, green, yellow, and off are explicit commands. States hold until another command or a session restart. Off restores original road priority. Unknown states, unavailable providers, and changed sessions disable commands until refreshed. Commands carry the map's world ID and revision.

Use the prepared [four-way physical-device projects](../../docs/components/simulator/traffic-light-devices.md). Unmapped older projects still run, but their theoretical SUMO signals are not presented as real devices.

## API and settings

Melchior listens on **http://localhost:8081** by default. The frontend proxies it under `/melchior-api`. OpenAPI is `/openapi/v1.json`.

| Endpoint | Behavior |
| --- | --- |
| `GET /health` | Service health, manual mode |
| `GET /api/v1/connection` | Saved provider URL |
| `PUT /api/v1/connection` | Save `{ "base_url": "http://simulator:8080" }` |
| `GET /api/v1/session` | Discover active session/world/revision |
| `GET /api/v1/sessions/{id}/map` | Cached physical-device map |
| `GET /api/v1/sessions/{id}/devices` | Live device states |
| `PUT /api/v1/sessions/{id}/devices/{deviceId}` | Explicit action with world ID and revision |

Configure `Logistics__TlcApiBaseUrl`, `Logistics__ConnectionFile`, `Logistics__RequestTimeoutSeconds` (default 15), and `Logistics__DiscoveryIntervalMilliseconds` (default 1500). Native execution defaults to localhost:8080 and stores its connection in `.data/logistics/connection.json`. Use `--urls` or `URLS` to change its listener.

For native execution:

```sh
dotnet run --project components/streetflow/Streetflow.Logistics
```

See the [frontend guide](../frontend/README.md) and [component boundary](../../docs/components/streetflow/README.md).
