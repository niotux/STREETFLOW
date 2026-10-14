using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class TransformerGenerationTests
{
  private static TransformerDemandGenerator Generator(CityProject project, int seed, double start) => new(project, seed, start, new TransformerGenerationLimits(512, 10000, 128, 4096));

  [Fact]
  public void SamplingIsReproducibleAcrossPollingAndBackpressure()
  {
    CityProject project = CityProjectLoader.Load(TransformerProjectFixture.Source);
    TransformerDemandGenerator one = Generator(project, 42, 8 * 3600);
    TransformerDemandGenerator many = Generator(project, 42, 8 * 3600);
    double next = many.NextDepartureSeconds;
    Assert.Empty(many.GenerateUntil(7200, 0));
    Assert.Equal(next, many.NextDepartureSeconds);
    GeneratedArrival[] expected = one.GenerateUntil(7200, 10000);
    List<GeneratedArrival> actual = [];
    for (double elapsed = 0.5; elapsed <= 7200; elapsed += 0.5)
    {
      actual.AddRange(many.GenerateUntil(elapsed, 1));
    }

    actual.AddRange(many.GenerateUntil(7200, 10000));
    Assert.True(expected.Length > 100);
    Assert.Equal(expected.Length, actual.Count);
    for (int index = 0; index < expected.Length; index++)
    {
      Assert.Equal(expected[index].DepartureSeconds, actual[index].DepartureSeconds);
      Assert.Equal(expected[index].Route.Edges, actual[index].Route.Edges);
      Assert.Equal(expected[index].Route.ExitTerminalId, actual[index].Route.ExitTerminalId);
      Assert.Equal(expected[index].Route.EntryTerminalId, actual[index].Route.EntryTerminalId);
    }

    GeneratedArrival[] different = Generator(project, 43, 8 * 3600).GenerateUntil(7200, 10000);
    Assert.NotEqual(expected[0].DepartureSeconds, different[0].DepartureSeconds);
    Assert.Empty(one.Candidates);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(42)]
  [InlineData(77)]
  public void AuthoredMorningAndEveningWeightsReverseTheMainCorridor(int seed)
  {
    CityProject project = CityProjectLoader.Load(TransformerProjectFixture.Source);
    AssertCorridor(Generator(project, seed, 8 * 3600).GenerateUntil(3600, 10000), true);
    AssertCorridor(Generator(project, seed, 17 * 3600).GenerateUntil(3600, 10000), false);
  }

  [Fact]
  public void EverySampleContainsItsCompleteLegalRouteAndTerminalPositions()
  {
    CityProject project = CityProjectLoader.Load(TransformerProjectFixture.Source);
    Dictionary<string, ProjectTerminal> terminals = new(StringComparer.Ordinal);
    foreach (ProjectTerminal terminal in project.Terminals)
    {
      terminals.Add(terminal.Id, terminal);
    }

    GeneratedArrival[] arrivals = Generator(project, 42, 8 * 3600).GenerateUntil(3600, 10000);
    Assert.NotEmpty(arrivals);
    foreach (GeneratedArrival arrival in arrivals)
    {
      RouteCandidate route = arrival.Route;
      Assert.Equal(2, route.Edges.Length);
      Assert.Equal("car", route.VehicleTypeId);
      ProjectTerminal entry = terminals[route.EntryTerminalId!];
      ProjectTerminal exit = terminals[route.ExitTerminalId!];
      Assert.Equal("entry", entry.Role);
      Assert.Equal("exit", exit.Role);
      Assert.Equal(route.Edges[0] + "_0", entry.LaneId);
      Assert.Equal(route.Edges[1] + "_0", exit.LaneId);
      Assert.Equal(entry.PositionM, route.DeparturePositionM);
      Assert.Equal(exit.PositionM, route.ArrivalPositionM);
      Assert.Equal(0, route.DepartureLaneIndex);
      Assert.Equal(0, route.ArrivalLaneIndex);
      bool legal = false;
      foreach (ProjectConnection connection in project.Connections)
      {
        if (connection.From == route.Edges[0] && connection.To == route.Edges[1])
        {
          legal = true;
          break;
        }
      }

      Assert.True(legal, string.Join(" ", route.Edges));
    }
  }

  [Fact]
  public void RatesHaveTheirOwnUnitsAndDailyBoundaries()
  {
    CityProject project = CityProjectLoader.Load(TransformerProjectFixture.Source);
    TransformerDemandGenerator generator = Generator(project, 42, 0);
    double expected = 0;
    TransformerModel model = project.TransformerModel!;
    for (int period = 0; period < model.Axes.Period.Length; period++)
    {
      double sum = 0;
      for (int origin = 0; origin < model.Axes.Origin.Length; origin++)
      {
        sum += project.TransformerWeights!.Tensors.ArrivalRates.Values[period * model.Axes.Origin.Length + origin];
      }

      double start = model.Axes.Period[period].StartSeconds;
      Assert.Equal(sum / 3600, generator.RatePerSecond(start), precision: 12);
      Assert.Equal(generator.RatePerSecond(start), generator.RatePerSecond(start + 86400));
      expected += sum * (model.Axes.Period[period].EndSeconds - start) / 3600;
    }

    double integrated = 0;
    for (int hour = 0; hour < 24; hour++)
    {
      integrated += generator.RatePerSecond(hour * 3600) * 3600;
    }

    Assert.Equal(expected, integrated, precision: 9);
  }

  [Fact]
  public void NonrepeatingTrafficEndsAtTheFirstMidnight()
  {
    using TransformerProjectFixture fixture = new();
    fixture.Change("journey-model.json", model => model["repeat_daily"] = false);
    TransformerDemandGenerator generator = Generator(CityProjectLoader.Load(fixture.Path), 42, 86300);
    GeneratedArrival[] arrivals = generator.GenerateUntil(200, 10000);
    Assert.NotEmpty(arrivals);
    foreach (GeneratedArrival arrival in arrivals)
    {
      Assert.True(arrival.DepartureSeconds < 100);
    }

    Assert.True(generator.Ended);
    Assert.Equal(0, generator.RatePerSecond(100));
  }

  [Fact]
  public void ZeroArrivalRatesEndGenerationWithoutSamplingVehicles()
  {
    using TransformerProjectFixture fixture = new();
    fixture.Change("weights.json", weights =>
    {
      System.Text.Json.Nodes.JsonArray rates = weights["tensors"]!["arrival_rates"]!["values"]!.AsArray();
      for (int index = 0; index < rates.Count; index++)
      {
        rates[index] = 0;
      }
    });
    TransformerDemandGenerator generator = Generator(CityProjectLoader.Load(fixture.Path), 42, 0);
    Assert.Equal(0, generator.RatePerSecond(0));
    Assert.Empty(generator.GenerateUntil(86400, 10000));
    Assert.True(generator.Ended);
  }

  [Fact]
  public void ZeroRatePeriodIsSkippedUntilItsHalfOpenBoundary()
  {
    using TransformerProjectFixture fixture = new();
    fixture.Change("weights.json", weights =>
    {
      for (int origin = 0; origin < 4; origin++)
      {
        weights["tensors"]!["arrival_rates"]!["values"]![origin] = 0;
      }
    });
    TransformerDemandGenerator generator = Generator(CityProjectLoader.Load(fixture.Path), 42, 0);
    Assert.Equal(0, generator.RatePerSecond(25199));
    Assert.True(generator.RatePerSecond(25200) > 0);
    Assert.Empty(generator.GenerateUntil(25200, 10000));
    Assert.True(generator.NextDepartureSeconds > 25200);
    Assert.NotEmpty(generator.GenerateUntil(25260, 10000));
  }

  [Fact]
  public void SolverCacheEvictionDoesNotChangeTheSampledTraffic()
  {
    CityProject project = CityProjectLoader.Load(TransformerProjectFixture.Source);
    TransformerDemandGenerator normal = Generator(project, 42, 8 * 3600);
    TransformerDemandGenerator tinyCache = new(project, 42, 8 * 3600, new TransformerGenerationLimits(512, 10000, 1, 4096));
    GeneratedArrival[] expected = normal.GenerateUntil(1800, 10000);
    GeneratedArrival[] actual = tinyCache.GenerateUntil(1800, 10000);
    Assert.Equal(expected.Length, actual.Length);
    for (int index = 0; index < actual.Length; index++)
    {
      Assert.Equal(expected[index].DepartureSeconds, actual[index].DepartureSeconds);
      Assert.Equal(expected[index].Route.Edges, actual[index].Route.Edges);
    }
  }

  [Fact]
  public void RouteSafetyLimitReportsAnErrorInsteadOfRemovingLoops()
  {
    using TransformerCycleFixture fixture = new();
    CityProject project = CityProjectLoader.Load(fixture.Path);
    TransformerDemandGenerator generator = new(project, 42, 0, new TransformerGenerationLimits(512, 1, 128, 4096));
    ApiException error = Assert.Throws<ApiException>(() => generator.GenerateUntil(100, 10000));
    Assert.Equal("generation_error", error.Code);
    Assert.Contains("movements", error.Message);
  }

  [Fact]
  public void DestinationConditioningPreservesLegalLoopsAndTheBaseWrongExitProbability()
  {
    using TransformerCycleFixture fixture = new();
    TransformerDemandGenerator generator = Generator(CityProjectLoader.Load(fixture.Path), 42, 0);
    GeneratedArrival[] arrivals = generator.GenerateUntil(4000, 10000);
    int looped = 0;
    int repeatedLoop = 0;
    foreach (GeneratedArrival arrival in arrivals)
    {
      RouteCandidate route = arrival.Route;
      Assert.Equal("a", route.Edges[0]);
      Assert.Equal("goal", route.Edges[^1]);
      Assert.Equal("goal-exit", route.ExitTerminalId);
      if (route.Edges.Length > 2)
      {
        looped++;
        Assert.Equal("b", route.Edges[1]);
        Assert.Equal("a", route.Edges[2]);
      }

      if (route.Edges.Length > 4)
      {
        repeatedLoop++;
      }
    }

    Assert.True(arrivals.Length > 3500);
    Assert.True(repeatedLoop > 0);
    // q(a→b)=q(a→goal)=q(b→a)=q(b→wrong)=1/2.
    // H(a)=2/3, H(b)=1/3, so the conditioned loop probability is 1/4.
    Assert.InRange((double)looped / arrivals.Length, 0.22, 0.28);
  }

  private static void AssertCorridor(GeneratedArrival[] arrivals, bool morning)
  {
    int northbound = 0;
    int southbound = 0;
    foreach (GeneratedArrival arrival in arrivals)
    {
      RouteCandidate route = arrival.Route;
      if (route.Edges[0] == "south_in" && route.Edges[^1] == "north_out")
      {
        northbound++;
      }
      else if (route.Edges[0] == "north_in" && route.Edges[^1] == "south_out")
      {
        southbound++;
      }
    }

    bool expectedDirection = morning ? northbound > southbound * 2 : southbound > northbound * 2;
    Assert.True(expectedDirection, $"northbound={northbound}, southbound={southbound}");
  }
}
