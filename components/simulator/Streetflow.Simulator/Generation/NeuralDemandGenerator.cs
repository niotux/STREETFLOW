using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

public sealed class NeuralDemandGenerator : ICityTrafficGenerator, IDisposable
{
  public const string Version = "neural-routes-v1";
  private const double SecondsPerDay = 86400;
  private const double SecondsPerHour = 3600;
  private readonly NeuralModel model;
  private readonly NeuralRouteNetwork network;
  private readonly NeuralRouteGraph graph;
  private readonly Dictionary<string, int> roadIndices = new(StringComparer.Ordinal);
  private readonly double[] rates;
  private readonly Random timing;
  private readonly Random choices;
  private readonly double startTime;
  private readonly int maximumMovements;
  private long sequence;
  public RouteCandidate[] Candidates => [];
  public double NextDepartureSeconds { get; private set; }
  public bool Ended => double.IsPositiveInfinity(NextDepartureSeconds);

  public NeuralDemandGenerator(CityProject project, int seed, double startTime, NeuralGenerationLimits limits)
  {
    if (limits.MaximumRouteMovements <= 0 || limits.MaximumDynamicRoutes <= 0)
    {
      throw Error("Neural Routes operational limits must be positive");
    }

    model = project.NeuralModel ?? throw Error("Neural Routes requires model metadata");
    this.startTime = startTime;
    maximumMovements = Math.Min(model.MaximumRouteMovements, limits.MaximumRouteMovements);
    timing = new Random(seed);
    choices = new Random(seed ^ 0x5f3759df);
    graph = new NeuralRouteGraph(project.Edges, project.Connections, model.VehicleType.VehicleClass);
    for (int index = 0; index < model.Roads.Length; index++)
    {
      roadIndices.Add(model.Roads[index].EdgeId, index);
    }

    rates = new double[model.Periods.Length];
    for (int period = 0; period < rates.Length; period++)
    {
      foreach (NeuralArrival arrival in model.Periods[period].Arrivals)
      {
        rates[period] += arrival.VehiclesPerHour / SecondsPerHour;
      }
    }

    NextDepartureSeconds = NextTime(0);
    network = new NeuralRouteNetwork(ProjectPaths.Resolve(project.Directory, model.ModelFile), model);
  }

  public double RatePerSecond(double elapsed)
  {
    if (!model.RepeatDaily && startTime + elapsed >= SecondsPerDay)
    {
      return 0;
    }

    return rates[PeriodAt(elapsed)];
  }

  public GeneratedArrival[] GenerateUntil(double elapsed, int capacity)
  {
    List<GeneratedArrival> result = [];
    while (result.Count < capacity && NextDepartureSeconds <= elapsed)
    {
      double departure = NextDepartureSeconds;
      int period = PeriodAt(departure);
      string origin = ChooseOrigin(period);
      RouteCandidate route = SampleRoute(origin, period, sequence + 1);
      result.Add(new GeneratedArrival($"city-car-{++sequence}", departure, route));
      NextDepartureSeconds = NextTime(departure);
    }

    return result.ToArray();
  }

  private string ChooseOrigin(int period)
  {
    double choice = choices.NextDouble() * rates[period] * SecondsPerHour;
    NeuralArrival[] arrivals = model.Periods[period].Arrivals;
    foreach (NeuralArrival arrival in arrivals)
    {
      choice -= arrival.VehiclesPerHour;
      if (choice < 0)
      {
        return arrival.StartEdge;
      }
    }

    return arrivals[^1].StartEdge;
  }

  private RouteCandidate SampleRoute(string origin, int period, long vehicleSequence)
  {
    string edge = origin;
    int previous = model.MaxTurns + 1;
    float[] hidden = new float[model.HiddenSize];
    HashSet<int> lanes = graph.InitialLanes(edge);
    List<string> path = [];
    List<double> lengths = [];
    double distance = 0;
    double travelTime = 0;
    for (int step = 0; ; step++)
    {
      NeuralRoad road = model.Roads[roadIndices[edge]];
      ProjectLane lane = graph.Edge(edge).Lanes[MinimumLane(lanes)];
      path.Add(edge);
      lengths.Add(lane.Map.Length);
      distance += lane.Map.Length;
      travelTime += lane.Map.Length / lane.Map.Speed;
      float[] logits = network.Step(roadIndices[edge], previous, period, ref hidden);
      List<int> legal = [];
      List<HashSet<int>> followingLanes = [];
      for (int turn = 0; turn < road.NextEdgeIds.Length; turn++)
      {
        HashSet<int> next = graph.Advance(edge, road.NextEdgeIds[turn], lanes);
        if (next.Count > 0)
        {
          legal.Add(turn);
          followingLanes.Add(next);
        }
      }

      if (road.CanExit)
      {
        legal.Add(model.MaxTurns);
        followingLanes.Add([]);
      }

      if (legal.Count == 0)
      {
        throw Error($"Neural route reached road '{edge}' with no legal continuation or exit");
      }

      int selected = SampleChoice(logits, legal);
      int choice = legal[selected];
      if (choice == model.MaxTurns)
      {
        return new RouteCandidate($"neural-route-{vehicleSequence}", model.VehicleType.Id, path.ToArray(), lengths.ToArray(), distance, travelTime) { IsDynamic = true, DeparturePositionM = 0 };
      }

      if (step >= maximumMovements)
      {
        throw Error($"Neural route exceeds {maximumMovements} movements; it was not truncated or resampled");
      }

      edge = road.NextEdgeIds[choice];
      previous = choice;
      lanes = followingLanes[selected];
    }
  }

  private int SampleChoice(float[] logits, List<int> legal)
  {
    double maximum = double.NegativeInfinity;
    foreach (int choice in legal)
    {
      maximum = Math.Max(maximum, logits[choice]);
    }

    double total = 0;
    foreach (int choice in legal)
    {
      total += Math.Exp(logits[choice] - maximum);
    }

    double sampled = choices.NextDouble() * total;
    for (int index = 0; index < legal.Count; index++)
    {
      sampled -= Math.Exp(logits[legal[index]] - maximum);
      if (sampled < 0)
      {
        return index;
      }
    }

    return legal.Count - 1;
  }

  private static int MinimumLane(HashSet<int> lanes)
  {
    int result = int.MaxValue;
    foreach (int lane in lanes)
    {
      result = Math.Min(result, lane);
    }

    return result;
  }

  private int PeriodAt(double elapsed)
  {
    double cityTime = (startTime + elapsed) % SecondsPerDay;
    for (int index = 0; index < model.Periods.Length; index++)
    {
      if (cityTime < model.Periods[index].EndSeconds)
      {
        return index;
      }
    }

    throw Error("Clock is outside the model's daily periods");
  }

  private double NextTime(double elapsed)
  {
    bool anyTraffic = false;
    foreach (double rate in rates)
    {
      anyTraffic = anyTraffic || rate > 0;
    }

    if (!anyTraffic)
    {
      return double.PositiveInfinity;
    }

    double hazard = -Math.Log(1 - timing.NextDouble());
    while (true)
    {
      double absolute = startTime + elapsed;
      if (!model.RepeatDaily && absolute >= SecondsPerDay)
      {
        return double.PositiveInfinity;
      }

      int period = PeriodAt(elapsed);
      double duration = model.Periods[period].EndSeconds - absolute % SecondsPerDay;
      double rate = rates[period];
      if (rate > 0 && hazard < rate * duration)
      {
        return elapsed + hazard / rate;
      }

      hazard -= rate * duration;
      elapsed += duration;
    }
  }

  private static ApiException Error(string message) => new(400, "generation_error", message);
  public void Dispose() => network.Dispose();
}
