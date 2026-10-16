# Component implementations

Code is organized into [Balthasar / simulator](simulator/README.md), [Melchior / logistics](streetflow/README.md), [Casper / camera data](detector/README.md), and the [MAGI frontend](frontend/README.md). These are separate .NET 10 servers; camera inference runs in Python workers on Raspberry Pis. Shared observation and TACO DTOs live in `contracts/Streetflow.Traffic.Contracts/`.

The new backend is runnable through the root solution and Compose configuration. The preserved original engine remains in [legacy/simulator](../legacy/simulator/README.md).
