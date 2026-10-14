# Component implementations

Code is organized into [Balthasar / simulator](simulator/README.md), [Melchior / logistics](streetflow/README.md), [Casper / detector](detector/README.md), and the [MAGI frontend](frontend/README.md). Balthasar, Melchior, and the frontend are separate .NET 10 servers. Shared observation and tlcapi DTOs live in `contracts/Streetflow.Traffic.Contracts/`.

The new backend is runnable through the root solution and Compose configuration. The preserved original engine remains in [legacy/simulator](../legacy/simulator/README.md).
