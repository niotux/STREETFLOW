# Simulator implementation

The C# / .NET 10 SUMO wrapper lives in `Streetflow.Simulator/`; its unit, API, engine, and PostgreSQL tests live in `Streetflow.Simulator.Tests/`. Run it through the root Docker Compose configuration or directly with `dotnet run`. See [the backend run guide](../../docs/simulator/backend.md).

The browser inspector uses plain HTML and JavaScript served by ASP.NET Core. The new backend has no Python or Rust dependency. Its narrow C# TraCI client uses vehicle subscriptions and batched reads; SUMO remains a separate native process.

The preserved original implementation is in [legacy/simulator](../../legacy/simulator/README.md).
