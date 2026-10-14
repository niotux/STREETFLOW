# Simulator stack decision

Use C# and .NET 10 for the simulator backend, with ASP.NET Core for HTTP APIs, SUMO 1.28 as a child process, PostgreSQL for indexed results, and files for complete run archives. Use plain HTML and JavaScript for the browser inspector. Backend implementation and verification take priority before the UI.

## Application and engine

One ASP.NET application contains scenario loading, run coordination, simulated detectors, inspection endpoints, and recording. One coordinator serializes mutations and owns one SUMO connection. A hosted service advances running simulations independently of HTTP clients. A disconnected browser has no effect on the simulation.

SUMO supplies the native traffic model. C# speaks its TCP TraCI protocol, using vehicle subscriptions and batched queries for simulation lifecycle and signals. The adapter implements the required protocol subset rather than every SUMO command. Messages use dynamic receive buffers and timeouts. No Python helper or native C# binding is involved. [TraCI protocol](https://sumo.dlr.de/docs/TraCI/Protocol.html)

The evaluated `CodingConnected.Traci` 1.2.0 package has a fixed receive buffer, unbounded connection retries, and a subscription dispatch path that returns before dispatching results. Those paths are important for the required workload, so the backend owns a small adapter instead. Future protocol additions belong in that adapter and need engine integration tests.

## Storage and deployment

Files preserve resolved network/demand, configuration, camera/checkpoint settings, engine output, events, observations, trips, and passages. PostgreSQL indexes run metadata, observation frames, and final trip records. The initial three tables use the Npgsql driver directly and an idempotent schema initializer. EF Core is unnecessary for this small schema; schema migrations will be needed when its structure changes. [Npgsql](https://www.npgsql.org/doc/)

Writes are awaited between steps. This supplies backpressure without an unbounded recording queue. Storage failure is visible as a failed run. The simulator does not store every car position every step. Files remain usable in native development without PostgreSQL; Compose enables PostgreSQL by default.

Compose has two services: simulator and PostgreSQL. The simulator image contains ASP.NET and SUMO, starts SUMO on demand, and serves HTTP on port 8080. Named volumes persist results and the database. TraCI stays inside the simulator container. The host binds HTTP to localhost. SUMO binaries are pinned to the stable 1.28 package with SHA256 checks for amd64 and arm64; Python tool packages are excluded. NuGet dependencies use lock files.

## Browser and products

The inspector is ordinary HTML and JavaScript served from ASP.NET. JSON polling is enough initially. Map geometry, bounded snapshots, selected vehicles, results, and checkpoints are available through the API. There is no frontend build tool or separate web container.

Streetflow stays a separate process consuming the detector-shaped observation API. The real detector is a separate product and is not needed to run the simulator. Navigation and signal-control actions remain open product decisions.

## Verification scope

Linux x64 native execution and Docker execution have integration coverage against SUMO 1.28 and PostgreSQL 18. The 22 backend tests cover lifecycle, deterministic reset, persistent request replay, counts and window boundaries, native counting-line events, trip/checkpoint results, engine startup/crash failure, process cleanup, inspection limits, retention gaps, Iași traffic, and large subscription replies. All four legacy CTest suites also pass.

Native Windows/macOS and ARM64 containers remain platform checks. Cross-platform .NET code and available native binaries make them targets, not completed verification claims. Future full-city performance claims need measurements on representative demand, geometry, camera coverage, and hardware.
