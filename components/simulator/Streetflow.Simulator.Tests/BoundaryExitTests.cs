using Streetflow.Simulator.City;
using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.Sumo;

namespace Streetflow.Simulator.Tests;

public sealed class BoundaryExitTests
{
  [Fact]
  public async Task EveryPoduRosExitRemovesVehiclesWithoutTeleporting()
  {
    string source = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros");
    CityProject project = CityProjectLoader.Load(source);
    CityDemandGenerator generator = new(project, 42, 68940);
    Dictionary<string, RouteCandidate> exits = new(StringComparer.Ordinal);
    foreach (RouteCandidate route in generator.Candidates)
    {
      exits.TryAdd(route.ExitTerminalId!, route);
    }

    Assert.Equal(27, exits.Count);
    using ProjectFixture fixture = new();
    SimulatorOptions options = new() { DataRoot = fixture.Path, SumoBinary = Environment.GetEnvironmentVariable("Simulator__SumoBinary") ?? "sumo", EngineTimeoutSeconds = 15 };
    CityRunStorage storage = new(options);
    storage.CopyProject(project, "boundary-test");
    storage.WriteRoutes("boundary-test", project, generator.Candidates);
    CityRunConfiguration config = new(project.Manifest.Id, project.Revision, CityDemandGenerator.Version, 42, 68940, 0.5, 5000, 512, 64, -1, "external-v1");
    List<GeneratedArrival> arrivals = [];
    foreach (RouteCandidate route in exits.Values)
    {
      arrivals.Add(new GeneratedArrival($"boundary-car-{arrivals.Count}", arrivals.Count * 30, route));
    }

    await using SumoEngine engine = new();
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(90));
    await engine.StartCityAsync(project, config, storage.DirectoryFor("boundary-test"), options, timeout.Token);
    await engine.AddVehiclesAsync(arrivals.ToArray(), 0, timeout.Token);
    HashSet<string> arrived = new(StringComparer.Ordinal);
    EngineSnapshot? last = null;
    for (int step = 0; step < 3000 && arrived.Count < arrivals.Count; step++)
    {
      last = await engine.StepAsync(timeout.Token);
      Assert.Empty(last.Teleports);
      foreach (string id in last.Arrived)
      {
        arrived.Add(id);
      }
    }

    Assert.Equal(arrivals.Count, arrived.Count);
    Assert.NotNull(last);
    Assert.Empty(last.Vehicles);
    Assert.Equal(0, last.ExpectedVehicles);
  }
}
