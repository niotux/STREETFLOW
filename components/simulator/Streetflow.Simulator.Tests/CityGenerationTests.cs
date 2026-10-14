using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class CityGenerationTests
{
  private static CityProject Load(string id) => CityProjectLoader.Load(Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", id));

  [Theory]
  [InlineData("tiny-graph")]
  [InlineData("iasi-central-day")]
  [InlineData("podu-ros")]
  public void SameSeedReplaysIndependentlyOfBatchSize(string id)
  {
    CityProject project = Load(id);
    CityDemandGenerator one = new(project, 42, project.Manifest.Defaults.StartTimeSeconds);
    CityDemandGenerator many = new(project, 42, project.Manifest.Defaults.StartTimeSeconds);
    GeneratedArrival[] expected = one.GenerateUntil(600, 10000);
    List<GeneratedArrival> actual = [];
    for (double elapsed = 0.5; elapsed <= 600; elapsed += 0.5)
    {
      actual.AddRange(many.GenerateUntil(elapsed, 2));
    }

    actual.AddRange(many.GenerateUntil(600, 10000));
    Assert.True(expected.Length > 20);
    Assert.Equal(expected.Length, actual.Count);
    for (int index = 0; index < actual.Count; index++)
    {
      Assert.Equal(expected[index].DepartureSeconds, actual[index].DepartureSeconds);
      Assert.Equal(expected[index].Route.Edges, actual[index].Route.Edges);
      Assert.Equal(expected[index].Route.VehicleTypeId, actual[index].Route.VehicleTypeId);
    }

    CityDemandGenerator different = new(project, 43, project.Manifest.Defaults.StartTimeSeconds);
    Assert.NotEqual(expected[0].DepartureSeconds, different.GenerateUntil(600, 1)[0].DepartureSeconds);
  }

  [Theory]
  [InlineData("tiny-graph")]
  [InlineData("iasi-central-day")]
  [InlineData("podu-ros")]
  public void CandidatesRespectDirectionPermissionsAndDistance(string id)
  {
    CityProject project = Load(id);
    CityDemandGenerator generator = new(project, 7, 0);
    Assert.NotEmpty(generator.Candidates);
    Assert.True(generator.Candidates.Length <= project.Demand!.Generation.CandidateLimit);
    Dictionary<string, ProjectEdge> edges = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in project.Edges)
    {
      edges.Add(edge.Id, edge);
    }

    foreach (RouteCandidate route in generator.Candidates)
    {
      Assert.InRange(route.DistanceM, project.Demand!.Generation.MinTripDistanceM, project.Demand!.Generation.MaxTripDistanceM);
      Assert.Equal(route.Edges.Length, new HashSet<string>(route.Edges).Count);
      foreach (string edge in route.Edges)
      {
        Assert.True(edges[edge].Ordinary);
      }

      for (int index = 1; index < route.Edges.Length; index++)
      {
        bool legal = false;
        foreach (ProjectConnection connection in project.Connections)
        {
          if (connection.From == route.Edges[index - 1] && connection.To == route.Edges[index])
          {
            legal = edges[connection.From].Lanes[connection.FromLane].Permits("passenger") && edges[connection.To].Lanes[connection.ToLane].Permits("passenger");
            if (legal)
            {
              break;
            }
          }
        }

        Assert.True(legal, string.Join(" ", route.Edges));
      }
    }
  }

  [Fact]
  public void DailyRatesUseTheActualTotalWeights()
  {
    CityProject project = Load("iasi-central-day");
    CityDemandGenerator generator = new(project, 1, 0);
    Assert.True(generator.RatePerSecond(8 * 3600) > generator.RatePerSecond(3 * 3600));
    Assert.Equal(generator.RatePerSecond(8 * 3600), generator.RatePerSecond(8 * 3600 + 86400));
    double integral = 0;
    for (int hour = 0; hour < 24; hour++)
    {
      integral += generator.RatePerSecond(hour * 3600) * 3600;
    }

    Assert.Equal(project.Demand!.Generation.MeanDeparturesPerHour * 24, integral, precision: 6);
  }

  [Fact]
  public void NonrepeatingStopsAtFirstMidnightAndZeroIntervalsAreSkipped()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d =>
    {
      d["repeat_daily"] = false;
      d["generation"]!["mean_departures_per_hour"] = 3600;
    });
    CityDemandGenerator generator = new(CityProjectLoader.Load(fixture.Path), 42, 86390);
    GeneratedArrival[] arrivals = generator.GenerateUntil(1000, 10000);
    Assert.NotEmpty(arrivals);
    Assert.All(arrivals, arrival => Assert.True(arrival.DepartureSeconds < 10));
    Assert.True(generator.Ended);
    Assert.Equal(0, generator.RatePerSecond(10));

    fixture.ChangeDemand(d =>
    {
      d["repeat_daily"] = true;
      foreach (System.Text.Json.Nodes.JsonNode? profile in d["profiles"]!.AsArray())
      {
        profile!["intervals"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"start_seconds\":0,\"end_seconds\":10,\"weight\":0},{\"start_seconds\":10,\"end_seconds\":86400,\"weight\":1}]");
      }
    });
    generator = new CityDemandGenerator(CityProjectLoader.Load(fixture.Path), 42, 0);
    Assert.Empty(generator.GenerateUntil(10, 10));
    Assert.True(generator.NextDepartureSeconds > 10);
    Assert.NotEmpty(generator.GenerateUntil(20, 100));
  }

  [Fact]
  public void RoutePreferenceChangesWithoutTreatingZeroEdgesAsClosures()
  {
    CityProject original = Load("tiny-graph");
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d =>
    {
      foreach (System.Text.Json.Nodes.JsonNode? profile in d["profiles"]!.AsArray())
      {
        string edge = profile!["edge"]!.GetValue<string>();
        profile["intervals"]![0]!["weight"] = edge.StartsWith("west", StringComparison.Ordinal) ? 100 : 0;
      }
    });
    CityDemandGenerator normal = new(original, 11, 0);
    CityDemandGenerator biased = new(CityProjectLoader.Load(fixture.Path), 11, 0);
    GeneratedArrival[] before = normal.GenerateUntil(30000, 10000);
    GeneratedArrival[] after = biased.GenerateUntil(30000, 10000);
    int originalWest = CountWest(before);
    int biasedWest = CountWest(after);
    Assert.Equal(before.Length, after.Length);
    Assert.True(biasedWest > originalWest + before.Length / 5);
    Assert.All(after, arrival => Assert.True(arrival.Route.Edges.Length >= 2));
  }

  private static int CountWest(GeneratedArrival[] arrivals)
  {
    int count = 0;
    foreach (GeneratedArrival arrival in arrivals)
    {
      foreach (string edge in arrival.Route.Edges)
      {
        if (edge.StartsWith("west", StringComparison.Ordinal))
        {
          count++;
          break;
        }
      }
    }

    return count;
  }

  [Fact]
  public void MissingLegalCandidatesProduceAnExplicitGenerationError()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d => d["generation"]!["min_trip_distance_m"] = 4000);
    CityDemandGenerator generator = new(CityProjectLoader.Load(fixture.Path), 42, 0);
    ApiException error = Assert.Throws<ApiException>(() => generator.GenerateUntil(100, 10));
    Assert.Equal("generation_error", error.Code);
  }

  [Fact]
  public void BackpressureDoesNotConsumeOrDiscardArrivals()
  {
    CityProject project = Load("tiny-graph");
    CityDemandGenerator normal = new(project, 42, 0);
    CityDemandGenerator blocked = new(project, 42, 0);
    double next = blocked.NextDepartureSeconds;
    Assert.Empty(blocked.GenerateUntil(1000, 0));
    Assert.Equal(next, blocked.NextDepartureSeconds);
    GeneratedArrival[] expected = normal.GenerateUntil(1000, 10000);
    GeneratedArrival[] delayed = blocked.GenerateUntil(1000, 10000);
    Assert.Equal(expected.Length, delayed.Length);
    for (int index = 0; index < expected.Length; index++)
    {
      Assert.Equal(expected[index].DepartureSeconds, delayed[index].DepartureSeconds);
      Assert.Equal(expected[index].Route.Id, delayed[index].Route.Id);
    }
  }

  [Fact]
  public void PositiveCurrentRateWithoutAScoredCandidateDoesNotFallBackToUniform()
  {
    using ProjectFixture fixture = new();
    fixture.ChangeDemand(d =>
    {
      d["generation"]!["candidate_limit"] = 1;
      d["routing"]!["max_alternatives"] = 1;
    });
    CityDemandGenerator initial = new(CityProjectLoader.Load(fixture.Path), 42, 0);
    HashSet<string> chosenEdges = new(initial.Candidates[0].Edges, StringComparer.Ordinal);
    fixture.ChangeDemand(d =>
    {
      foreach (System.Text.Json.Nodes.JsonNode? profile in d["profiles"]!.AsArray())
      {
        string edge = profile!["edge"]!.GetValue<string>();
        profile["intervals"]![0]!["weight"] = chosenEdges.Contains(edge) ? 0 : 1;
      }
    });
    CityDemandGenerator generator = new(CityProjectLoader.Load(fixture.Path), 42, 0);
    Assert.True(generator.RatePerSecond(0) > 0);
    ApiException error = Assert.Throws<ApiException>(() => generator.GenerateUntil(100, 10));
    Assert.Equal("generation_error", error.Code);
  }

  [Fact]
  public void AlternativePathsRespectTheConfiguredDetourBound()
  {
    CityProject project = Load("podu-ros");
    CityDemandGenerator generator = new(project, 42, 0);
    Dictionary<string, List<RouteCandidate>> pairs = new(StringComparer.Ordinal);
    foreach (RouteCandidate route in generator.Candidates)
    {
      string key = route.EntryTerminalId + " " + route.ExitTerminalId;
      if (!pairs.TryGetValue(key, out List<RouteCandidate>? alternatives))
      {
        alternatives = [];
        pairs.Add(key, alternatives);
      }

      alternatives.Add(route);
    }

    bool hasAlternative = false;
    foreach (List<RouteCandidate> alternatives in pairs.Values)
    {
      Assert.InRange(alternatives.Count, 1, project.Demand!.Routing.MaxAlternatives);
      hasAlternative = hasAlternative || alternatives.Count > 1;
      double shortest = double.PositiveInfinity;
      foreach (RouteCandidate route in alternatives)
      {
        shortest = Math.Min(shortest, route.TravelTimeSeconds);
      }

      foreach (RouteCandidate route in alternatives)
      {
        Assert.True(route.TravelTimeSeconds <= shortest * project.Demand!.Routing.MaxDetourRatio);
      }
    }

    Assert.True(hasAlternative);
  }
}
