using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Tests;

public sealed class BackendFactory : WebApplicationFactory<Program>
{
  public string DataRoot { get; }
  public string? ConnectionString { get; init; }
  public string? SumoBinary { get; init; }
  public int MaximumLiveVehicles { get; init; } = 5000;
  public int MaximumPendingEntries { get; init; } = 512;
  public double MaximumBacklogSeconds { get; init; } = 300;
  public int Retention { get; init; } = 2000;

  public BackendFactory(string? directory = null)
  {
    DataRoot = directory ?? Path.Combine(Path.GetTempPath(), "streetflow-tests", Guid.NewGuid().ToString("N"));
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");
    builder.ConfigureServices(services =>
    {
      DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
      while (directory is not null)
      {
        if (File.Exists(Path.Combine(directory.FullName, "Streetflow.slnx")))
        {
          break;
        }

        directory = directory.Parent;
      }

      if (directory is null)
      {
        throw new InvalidOperationException("Cannot find workspace");
      }

      services.RemoveAll<SimulatorOptions>();
      services.AddSingleton(new SimulatorOptions { ScenarioRoot = Path.Combine(directory.FullName, "scenarios"), DataRoot = DataRoot, SumoBinary = SumoBinary ?? Environment.GetEnvironmentVariable("Simulator__SumoBinary") ?? "sumo", ConnectionString = ConnectionString, ObservationRetention = Retention, EngineTimeoutSeconds = 5, NetconvertBinary = "netconvert", DuarouterBinary = "duarouter", MaximumProjectBytes = 256L * 1024 * 1024, MaximumProjectFiles = 4096, MaximumLiveVehicles = MaximumLiveVehicles, MaximumPendingEntries = MaximumPendingEntries, GenerationBatchSize = 64, MaximumBacklogSeconds = MaximumBacklogSeconds, MaximumTransformerStates = 512, MaximumTransformerRouteMovements = 10000, MaximumTransformerCachedContexts = 128, MaximumDynamicRoutes = 4096, MaximumNeuralRouteMovements = 10000 });
    });
  }
}
