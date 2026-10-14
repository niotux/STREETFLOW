using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

public sealed class TransformerDemandGenerator : ICityTrafficGenerator
{
  public const string Version = "transformer-routes-v1";
  private const double SecondsPerDay = 86400;
  private const double SecondsPerHour = 3600;
  private readonly TransformerModel model;
  private readonly TransformerWeights weights;
  private readonly TransformerAxes axes;
  private readonly TransformerGenerationLimits limits;
  private readonly double startTime;
  private readonly Random timing;
  private readonly Random choices;
  private readonly TransformerRouteGraph[] graphs;
  private readonly double[] rates;
  private readonly Dictionary<int, TransformerRouteDistribution> distributions = [];
  private readonly Queue<int> distributionOrder = new();
  private long sequence;
  public RouteCandidate[] Candidates => [];
  public double NextDepartureSeconds { get; private set; }
  public bool Ended => double.IsPositiveInfinity(NextDepartureSeconds);

  public TransformerDemandGenerator(CityProject project, int seed, double startTime, TransformerGenerationLimits limits)
  {
    bool validLimits = limits.MaximumStates > 0 && limits.MaximumRouteMovements > 0;
    validLimits = validLimits && limits.MaximumCachedContexts > 0 && limits.MaximumDynamicRoutes > 0;
    if (!validLimits)
    {
      throw Error("Transformer Routes operational limits must be positive");
    }

    model = project.TransformerModel ?? throw Error("Transformer Routes requires model metadata");
    weights = project.TransformerWeights ?? throw Error("Transformer Routes requires model weights");
    axes = model.Axes;
    this.limits = limits;
    this.startTime = startTime;
    timing = new Random(seed);
    choices = new Random(seed ^ 0x5f3759df);
    graphs = new TransformerRouteGraph[axes.VehicleType.Length];
    for (int vehicle = 0; vehicle < graphs.Length; vehicle++)
    {
      graphs[vehicle] = new TransformerRouteGraph(project, axes.VehicleType[vehicle].VehicleClass);
      if (graphs[vehicle].States.Length > limits.MaximumStates)
      {
        throw Error($"Transformer Routes exceeds the configured limit of {limits.MaximumStates} routing states");
      }
    }

    rates = new double[axes.Period.Length];
    for (int period = 0; period < rates.Length; period++)
    {
      for (int origin = 0; origin < axes.Origin.Length; origin++)
      {
        rates[period] += weights.Tensors.ArrivalRates.Values[period * axes.Origin.Length + origin] / SecondsPerHour;
      }
    }

    NextDepartureSeconds = NextTime(0);
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
      TransformerJourneyContext context = ChooseContext(period);
      RouteCandidate route = SampleRoute(context, sequence + 1);
      result.Add(new GeneratedArrival($"city-car-{++sequence}", departure, route));
      NextDepartureSeconds = NextTime(departure);
    }

    return result.ToArray();
  }

  private TransformerJourneyContext ChooseContext(int period)
  {
    double choice = choices.NextDouble() * rates[period] * SecondsPerHour;
    int origin = -1;
    for (int index = 0; index < axes.Origin.Length; index++)
    {
      double rate = weights.Tensors.ArrivalRates.Values[period * axes.Origin.Length + index];
      if (rate <= 0)
      {
        continue;
      }

      origin = index;
      choice -= rate;
      if (choice < 0)
      {
        break;
      }
    }

    List<TransformerJourneyContext> contexts = [];
    List<double> logits = [];
    double maximum = double.NegativeInfinity;
    for (int destination = 0; destination < axes.Destination.Length; destination++)
    {
      for (int category = 0; category < axes.Category.Length; category++)
      {
        for (int vehicle = 0; vehicle < axes.VehicleType.Length; vehicle++)
        {
          if (!graphs[vehicle].CanReach(origin, destination))
          {
            continue;
          }

          int index = ContextIndex(period, origin, destination, category, vehicle);
          double logit = weights.Tensors.JourneyLogits.Values[index];
          contexts.Add(new TransformerJourneyContext(origin, destination, category, vehicle, index));
          logits.Add(logit);
          maximum = Math.Max(maximum, logit);
        }
      }
    }

    if (contexts.Count == 0)
    {
      throw Error($"Positive-rate origin '{axes.Origin[origin]}' has no feasible journey");
    }

    double total = 0;
    for (int index = 0; index < logits.Count; index++)
    {
      double probability = Math.Exp(logits[index] - maximum);
      if (probability <= 0 || !double.IsFinite(probability))
      {
        throw Error("Journey logits exceed the supported numerical range; a feasible journey underflowed");
      }

      logits[index] = probability;
      total += probability;
    }

    choice = choices.NextDouble() * total;
    for (int index = 0; index < contexts.Count; index++)
    {
      choice -= logits[index];
      if (choice < 0)
      {
        return contexts[index];
      }
    }

    return contexts[^1];
  }

  private int ContextIndex(int period, int origin, int destination, int category, int vehicle)
  {
    int index = period * axes.Origin.Length + origin;
    index = index * axes.Destination.Length + destination;
    index = index * axes.Category.Length + category;
    return index * axes.VehicleType.Length + vehicle;
  }

  private RouteCandidate SampleRoute(TransformerJourneyContext context, long vehicleSequence)
  {
    TransformerRouteGraph graph = graphs[context.VehicleType];
    TransformerRouteDistribution distribution = Distribution(context, graph);
    ProjectTerminal entry = graph.OriginTerminal(context.Origin);
    List<string> edges = [];
    List<double> lengths = [];
    double distance = 0;
    double time = 0;
    int stateIndex = graph.OriginState(context.Origin);
    for (int movement = 0; ; movement++)
    {
      TransformerRoutingState state = graph.States[stateIndex];
      double length = state.Lane.Map.Length;
      if (movement == 0)
      {
        length -= entry.PositionM;
      }

      bool arrived = graph.IsDestination(stateIndex, context.Destination);
      if (arrived)
      {
        length -= state.Lane.Map.Length - state.Exit!.PositionM;
      }

      edges.Add(state.Edge.Id);
      lengths.Add(length);
      distance += length;
      time += length / state.Lane.Map.Speed;
      if (arrived)
      {
        return new RouteCandidate($"transformer-route-{vehicleSequence}", axes.VehicleType[context.VehicleType].Id, edges.ToArray(), lengths.ToArray(), distance, time)
        {
          EntryTerminalId = entry.Id,
          ExitTerminalId = state.Exit!.Id,
          DepartureLaneIndex = 0,
          DeparturePositionM = entry.PositionM,
          ArrivalLaneIndex = 0,
          ArrivalPositionM = state.Exit.PositionM,
          IsDynamic = true
        };
      }

      if (movement >= limits.MaximumRouteMovements)
      {
        throw Error($"Sampled route exceeds {limits.MaximumRouteMovements} movements; route was not truncated or resampled");
      }

      stateIndex = distribution.Next(stateIndex, choices);
    }
  }

  private TransformerRouteDistribution Distribution(TransformerJourneyContext context, TransformerRouteGraph graph)
  {
    if (distributions.TryGetValue(context.WeightIndex, out TransformerRouteDistribution? existing))
    {
      return existing;
    }

    TransformerRouteDistribution created = new(graph, context.Destination, weights.Tensors.MovementLogits.Values, context.WeightIndex * axes.Movement.Length);
    if (distributions.Count >= limits.MaximumCachedContexts)
    {
      distributions.Remove(distributionOrder.Dequeue());
    }

    distributions.Add(context.WeightIndex, created);
    distributionOrder.Enqueue(context.WeightIndex);
    return created;
  }

  private int PeriodAt(double elapsed)
  {
    double city = (startTime + elapsed) % SecondsPerDay;
    for (int index = 0; index < axes.Period.Length; index++)
    {
      if (city < axes.Period[index].EndSeconds)
      {
        return index;
      }
    }

    throw Error("City clock is outside the model's daily periods");
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
      double duration = axes.Period[period].EndSeconds - absolute % SecondsPerDay;
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
}
