using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Tests;

public sealed class CommuteProfileTests
{
  private const string NorthboundNicolina = "1134276130#0";
  private const string SouthboundNicolina = "305271347";
  private const double MinimumDirectionalRatio = 1.7;

  [Theory]
  [InlineData(1)]
  [InlineData(42)]
  [InlineData(77)]
  public void MorningJourneysFavorTheCityAndEveningJourneysFavorSouthernApproaches(int seed)
  {
    string folder = Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "podu-ros-commute");
    CityProject project = CityProjectLoader.Load(folder);
    AssertDirection(project, seed, 8 * 3600, true);
    AssertDirection(project, seed, 17 * 3600, false);
    CityDemandGenerator morning = new(project, seed, 8 * 3600);
    CityDemandGenerator night = new(project, seed, 3 * 3600);
    Assert.True(morning.RatePerSecond(0) > night.RatePerSecond(0) * 5);
  }

  private static void AssertDirection(CityProject project, int seed, double startTime, bool northbound)
  {
    CityDemandGenerator generator = new(project, seed, startTime);
    GeneratedArrival[] arrivals = generator.GenerateUntil(1800, 10000);
    int north = 0;
    int south = 0;
    foreach (GeneratedArrival arrival in arrivals)
    {
      RouteCandidate route = arrival.Route;
      foreach (string edge in route.Edges)
      {
        if (edge == NorthboundNicolina)
        {
          north++;
        }
        else if (edge == SouthboundNicolina)
        {
          south++;
        }
      }
    }

    Assert.True(north > 0 && south > 0);
    bool favorsExpectedDirection = northbound ? north > south * MinimumDirectionalRatio : south > north * MinimumDirectionalRatio;
    Assert.True(favorsExpectedDirection, $"At {startTime / 3600}: north={north}, south={south}");
  }
}
