using System.Globalization;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.Sumo;

namespace Streetflow.Simulator.Tests;

public sealed class ProtocolIntegrationTests
{
  [Fact]
  public async Task LargeSubscriptionResponsesAreReadCompletely()
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

    Scenario scenario = ScenarioCatalog.Load(Path.Combine(directory!.FullName, "scenarios", "iasi-central", "manifest.json"));
    var output = Path.Combine(Path.GetTempPath(), "streetflow-protocol", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(output);
    File.Copy(scenario.NetworkPath, Path.Combine(output, "network.net.xml"));
    XDocument routes = XDocument.Load(scenario.RoutesPath);
    List<XElement> originals = new(routes.Root!.Elements("vehicle"));
    for (var copy = 0; copy < 3; copy++)
    {
      foreach (XElement vehicle in originals)
      {
        XElement extra = new XElement(vehicle);
        extra.SetAttributeValue("id", (string)vehicle.Attribute("id")! + "-extra-" + copy);
        vehicle.AddAfterSelf(extra);
      }
    }

    // Added vehicles share departure times; sort so SUMO can load every trip.
    List<XElement> all = new(routes.Root.Elements("vehicle"));
    SortByDeparture(all);
    foreach (XElement vehicle in all)
    {
      vehicle.Remove();
    }

    routes.Root.Add(all);
    routes.Save(Path.Combine(output, "routes.rou.xml"));
    await using SumoEngine engine = new SumoEngine();
    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    RunConfiguration configuration = new("iasi-central", "1", 42, 0.5, 5, 180);
    SimulatorOptions options = new()
    {
      SumoBinary = Environment.GetEnvironmentVariable("Simulator__SumoBinary") ?? "sumo",
      EngineTimeoutSeconds = 15
    };
    await engine.StartAsync(scenario, configuration, output, options, timeout.Token);
    var peak = 0;
    for (var i = 0; i < 300; i++)
    {
      EngineSnapshot snapshot = await engine.StepAsync(timeout.Token);
      peak = Math.Max(peak, snapshot.Vehicles.Length);
      foreach (VehicleState vehicle in snapshot.Vehicles)
      {
        Assert.True(double.IsFinite(vehicle.X));
        Assert.NotEmpty(vehicle.LaneId);
      }
    }

    Assert.True(peak > 100, $"Fixture must exercise large subscription responses; peak was {peak}");
  }

  private static void SortByDeparture(List<XElement> all)
  {
    // Insert after equal departures to retain the original stable ordering.
    for (int index = 1; index < all.Count; index++)
    {
      XElement vehicle = all[index];
      double departure = Departure(vehicle);
      int position = index;
      while (position > 0 && Departure(all[position - 1]) > departure)
      {
        all[position] = all[position - 1];
        position--;
      }

      all[position] = vehicle;
    }
  }

  private static double Departure(XElement vehicle)
  {
    return double.Parse(vehicle.Attribute("depart")!.Value, CultureInfo.InvariantCulture);
  }
}
