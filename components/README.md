# Component implementations

Code is organized into [simulator](simulator/README.md), [streetflow](streetflow/README.md), and [detector](detector/README.md). The simulator backend uses .NET 10; the two product implementations remain separate work. Shared C# observation DTOs live in `contracts/Streetflow.Traffic.Contracts/`.

The new backend is runnable through the root solution and Compose configuration. The preserved original engine remains in [legacy/simulator](../legacy/simulator/README.md).
