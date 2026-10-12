using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

public sealed class CityDemandGenerator
{
  public const string Version = "city-weights-v2-terminals";
  public const double SecondsPerDay = 86400;
  private const double SecondsPerHour = 3600;
  private readonly CityProject project;
  private readonly double startTime;
  private readonly Random timing;
  private readonly Random choices;
  private readonly Dictionary<string, EdgeTrafficProfile> profiles = new(StringComparer.Ordinal);
  private readonly double[] boundaries;
  private readonly double dailyMean;
  private long sequence;
  public RouteCandidate[] Candidates { get; }
  public double NextDepartureSeconds { get; private set; }
  public bool Ended => double.IsPositiveInfinity(NextDepartureSeconds);

  public CityDemandGenerator(CityProject project, int seed, double startTime)
  {
    this.project = project;
    this.startTime = startTime;
    timing = new Random(seed);
    choices = new Random(seed ^ 0x5f3759df);
    SortedSet<double> times = new() { 0, SecondsPerDay };
    double dailyIntegral = 0;
    foreach (EdgeTrafficProfile profile in project.Demand.Profiles)
    {
      profiles.Add(profile.Edge, profile);
      foreach (TrafficInterval interval in profile.Intervals)
      {
        dailyIntegral += interval.Weight * (interval.EndSeconds - interval.StartSeconds);
        times.Add(interval.EndSeconds);
      }
    }

    dailyMean = dailyIntegral / SecondsPerDay;
    boundaries = new double[times.Count];
    times.CopyTo(boundaries);
    Candidates = new CandidateRoutes(project, seed).Build();
    NextDepartureSeconds = NextTime(0);
  }

  public double RatePerSecond(double elapsed)
  {
    if (!project.Demand.RepeatDaily && startTime + elapsed >= SecondsPerDay)
    {
      return 0;
    }

    if (dailyMean == 0 || project.Demand.Generation.MeanDeparturesPerHour == 0)
    {
      return 0;
    }

    double cityTime = CityTime(elapsed);
    double total = 0;
    foreach (EdgeTrafficProfile profile in project.Demand.Profiles)
    {
      total += Weight(profile, cityTime);
    }

    double rate = project.Demand.Generation.MeanDeparturesPerHour / SecondsPerHour * (total / dailyMean);
    if (!double.IsFinite(rate))
    {
      throw new ApiException(400, "generation_error", "Traffic rate exceeds finite numeric limits");
    }

    return rate;
  }

  public GeneratedArrival[] GenerateUntil(double elapsed, int capacity)
  {
    List<GeneratedArrival> arrivals = [];
    while (arrivals.Count < capacity && NextDepartureSeconds <= elapsed)
    {
      double departure = NextDepartureSeconds;
      RouteCandidate candidate = ChooseRoute(departure);
      arrivals.Add(new GeneratedArrival($"city-car-{++sequence}", departure, candidate));
      NextDepartureSeconds = NextTime(departure);
    }

    return arrivals.ToArray();
  }

  public double Score(RouteCandidate candidate, double elapsed)
  {
    double cityTime = CityTime(elapsed);
    double score = 0;
    for (int index = 0; index < candidate.Edges.Length; index++)
    {
      score += Weight(profiles[candidate.Edges[index]], cityTime) * (candidate.Lengths[index] / candidate.DistanceM);
    }

    return score;
  }

  private RouteCandidate ChooseRoute(double elapsed)
  {
    double typeTotal = 0;
    foreach (ProjectVehicleType type in project.Demand.VehicleTypes)
    {
      typeTotal += type.Weight;
    }

    double typeChoice = choices.NextDouble() * typeTotal;
    ProjectVehicleType selected = project.Demand.VehicleTypes[^1];
    foreach (ProjectVehicleType type in project.Demand.VehicleTypes)
    {
      typeChoice -= type.Weight;
      if (typeChoice < 0)
      {
        selected = type;
        break;
      }
    }

    double total = 0;
    foreach (RouteCandidate candidate in Candidates)
    {
      if (candidate.VehicleTypeId == selected.Id)
      {
        total += Score(candidate, elapsed);
      }
    }

    if (total <= 0 || !double.IsFinite(total))
    {
      throw new ApiException(400, "generation_error", $"No positive legal candidate for vehicle type '{selected.Id}' at city time {CityTime(elapsed)}; check distance/candidate limits and road weights");
    }

    double choice = choices.NextDouble() * total;
    RouteCandidate? last = null;
    foreach (RouteCandidate candidate in Candidates)
    {
      if (candidate.VehicleTypeId != selected.Id)
      {
        continue;
      }

      double score = Score(candidate, elapsed);
      if (score <= 0)
      {
        continue;
      }

      last = candidate;
      choice -= score;
      if (choice < 0)
      {
        return candidate;
      }
    }

    return last ?? throw new InvalidOperationException("No scored route");
  }

  private double NextTime(double elapsed)
  {
    if (dailyMean == 0 || project.Demand.Generation.MeanDeparturesPerHour == 0)
    {
      return double.PositiveInfinity;
    }

    // Integrate one exponential hazard across every profile boundary, including midnight.
    // Scheduling never depends on polling/batch size and does not skip random draws under pressure.
    double hazard = -Math.Log(1 - timing.NextDouble());
    while (true)
    {
      double absolute = startTime + elapsed;
      if (!project.Demand.RepeatDaily && absolute >= SecondsPerDay)
      {
        return double.PositiveInfinity;
      }

      double city = CityTime(elapsed);
      double boundary = SecondsPerDay;
      foreach (double time in boundaries)
      {
        if (time > city)
        {
          boundary = time;
          break;
        }
      }

      double duration = boundary - city;
      double rate = RatePerSecond(elapsed);
      if (rate > 0 && hazard < duration * rate)
      {
        return elapsed + hazard / rate;
      }

      hazard -= duration * rate;
      elapsed += duration;
    }
  }

  private double CityTime(double elapsed) => (startTime + elapsed) % SecondsPerDay;

  private static double Weight(EdgeTrafficProfile profile, double cityTime)
  {
    foreach (TrafficInterval interval in profile.Intervals)
    {
      if (cityTime >= interval.StartSeconds && cityTime < interval.EndSeconds)
      {
        return interval.Weight;
      }
    }

    throw new InvalidOperationException($"No interval for '{profile.Edge}'");
  }
}
