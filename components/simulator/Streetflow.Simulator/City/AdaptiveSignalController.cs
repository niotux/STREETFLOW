using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.City;

// Executes Melchior's declarative policy against the prepared plan once per
// simulation step. No wall-clock polling, network requests or graph searches.
public sealed class AdaptiveSignalController(CompiledSignalPlan plan, CityProject project)
{
  private sealed class Meter
  {
    public HashSet<string> Previous = new(StringComparer.Ordinal);
    public Queue<(double Time, int Count)> Arrivals = new();
    public int Recent;
    public double Read(double time, HashSet<string> vehicles, int stopped)
    {
      int entered = vehicles.Count(id => !Previous.Contains(id));
      Previous = vehicles;
      if (entered > 0) { Arrivals.Enqueue((time, entered)); Recent += entered; }
      while (Arrivals.TryPeek(out var arrival) && arrival.Time <= time - 60) Recent -= Arrivals.Dequeue().Count;
      // One queued vehicle contributes two arrival-equivalents to pressure.
      return Recent + 2.0 * stopped;
    }
  }
  private sealed class Guard
  {
    public bool Blocked;
    public double Changed;
    public double YellowUntil;
    public double HoldUntil;
    public bool Held;
    public double Occupancy;
  }
  private sealed class Clock(CompiledSignalPlan.Controller controller)
  {
    public int Phase;
    public double End;
    public bool Started;
    public double[] Durations = controller.Phases.Select(p => p.Seconds).ToArray();
    public double K = 1;
    public bool Rush;
    public string? Last;
    public Meter[] Meters = controller.GroupLanes.Select(_ => new Meter()).ToArray();
    public double[] Demands = new double[controller.GroupLanes.Length];
    public Guard[] Guards = controller.Movements.Select(_ => new Guard()).ToArray();
  }

  private readonly Clock[] clocks = plan.Controllers.Select(c => new Clock(c)).ToArray();
  public TacoSignalPolicy Policy { get; private set; } = new();
  public TacoAdaptiveDevice[] Devices { get; private set; } = [];

  public void Configure(TacoSignalPolicy policy) => Policy = policy;

  public static double DemandMultiplier(double demandA, double demandB) => Math.Clamp(Math.Sqrt((demandA + 1) / (demandB + 1)), .2, 1.8);
  public static bool RushHour(double citySeconds)
  {
    double hour = ((citySeconds % 86400 + 86400) % 86400) / 3600;
    return hour is >= 7 and < 9 or >= 16 and < 19;
  }

  public static double[] GreenDurations(CompiledSignalPlan.Phase[] phases, double k)
  {
    double[] result = phases.Select(p => p.Seconds).ToArray();
    int[] eligible = Enumerable.Range(0, phases.Length).Where(i => phases[i].Weighted).ToArray();
    if (eligible.Length == 0) return result;
    double[] weights = phases.Select(p => p.Seconds * (p.Group == 0 ? k : p.Group == 1 ? 1 / k : 1)).ToArray();
    double budget = eligible.Sum(i => phases[i].Seconds);
    // Solve the bounded proportional allocation jointly. Clamping several
    // lower/upper bounds independently can accidentally change the cycle sum.
    double lower = 0;
    double upper = eligible.Max(i => Math.Max(phases[i].Seconds, 90) / weights[i]);
    for (int iteration = 0; iteration < 64; iteration++)
    {
      double scale = (lower + upper) / 2;
      double total = eligible.Sum(i => Math.Clamp(scale * weights[i], phases[i].MinimumSeconds, Math.Max(phases[i].Seconds, 90)));
      if (total < budget) lower = scale;
      else upper = scale;
    }
    foreach (int i in eligible)
      result[i] = Math.Clamp((lower + upper) / 2 * weights[i], phases[i].MinimumSeconds, Math.Max(phases[i].Seconds, 90));
    return result;
  }

  public SignalState[] Update(VehicleState[] vehicles, double time, double citySeconds)
  {
    int[] counts = new int[plan.Lanes.Length], stopped = new int[plan.Lanes.Length];
    double[] entry = Enumerable.Repeat(double.PositiveInfinity, plan.Lanes.Length).ToArray();
    double[] entrySpeed = new double[plan.Lanes.Length];
    HashSet<string>?[] identifiers = new HashSet<string>?[plan.Lanes.Length];
    foreach (VehicleState vehicle in vehicles)
    {
      if (!plan.LaneIndices.TryGetValue(vehicle.LaneId, out int index)) continue;
      counts[index]++;
      if (vehicle.SpeedMps < .5) stopped[index]++;
      if (vehicle.LanePosition < entry[index]) { entry[index] = vehicle.LanePosition; entrySpeed[index] = vehicle.SpeedMps; }
      (identifiers[index] ??= new(StringComparer.Ordinal)).Add(vehicle.Id);
    }

    SignalState[] states = new SignalState[plan.Controllers.Length];
    List<TacoAdaptiveDevice> diagnostics = [];
    for (int controllerIndex = 0; controllerIndex < plan.Controllers.Length; controllerIndex++)
    {
      var controller = plan.Controllers[controllerIndex];
      Clock clock = clocks[controllerIndex];
      for (int index = 0; index < controller.Movements.Length; index++)
      {
        var movement = controller.Movements[index];
        Guard guard = clock.Guards[index];
        bool sealedEntry = false;
        guard.Occupancy = 0;
        foreach (var exit in movement.Exits)
        {
          guard.Occupancy = Math.Max(guard.Occupancy, Math.Min(1, exit.Lanes.Sum(lane => counts[lane]) / exit.Capacity));
          if (exit.Lanes.Length > 0)
          {
            int first = exit.Lanes[0];
            // SUMO positions are front-bumper positions: require room for a car
            // plus its gap beyond the stop line, not just spare space far away.
            sealedEntry |= entry[first] < 12.5 && entrySpeed[first] < .5;
          }
        }
        bool protect = Policy.PreventSpillback && movement.QueueBuffer;
        bool full = sealedEntry || guard.Occupancy >= .9;
        if (protect && full && !guard.Blocked) { guard.Blocked = true; guard.Changed = time; }
        else if (guard.Blocked && (!protect || (!sealedEntry && guard.Occupancy < .75 && time - guard.Changed >= 3)))
          guard.Blocked = false;

      }
      for (int group = 0; group < controller.GroupLanes.Length; group++)
      {
        HashSet<string> present = new(StringComparer.Ordinal);
        int queued = 0;
        foreach (int lane in controller.GroupLanes[group])
        {
          if (identifiers[lane] is { } ids) present.UnionWith(ids);
          queued += stopped[lane];
        }
        double demand = clock.Meters[group].Read(time, present, queued);
        // A full receiving road cannot use extra green. Defer that demand so
        // it does not take the clear cross direction's rush-hour budget.
        clock.Demands[group] = controller.GroupMovements[group].Any(i => !clock.Guards[i].Blocked) ? demand : 0;
      }
      void Budget()
      {
        clock.Rush = RushHour(citySeconds);
        clock.K = Policy.WeightDemand && clock.Rush && clock.Demands.Length >= 2
          ? Policy.Multiplier ?? DemandMultiplier(clock.Demands[0], clock.Demands[1]) : 1;
        clock.Durations = GreenDurations(controller.Phases, clock.K);
      }
      if (!clock.Started)
      {
        Budget();
        double cycle = clock.Durations.Sum();
        double position = ((time - controller.Offset) % cycle + cycle) % cycle;
        while (position >= clock.Durations[clock.Phase]) { position -= clock.Durations[clock.Phase]; clock.Phase++; }
        clock.End = time + clock.Durations[clock.Phase] - position;
        clock.Started = true;
      }
      while (time >= clock.End - 1e-8)
      {
        clock.Phase = (clock.Phase + 1) % controller.Phases.Length;
        if (clock.Phase == 0) Budget();
        clock.End += clock.Durations[clock.Phase];
      }
      char[] desired = controller.Phases[clock.Phase].State.ToCharArray();
      for (int index = 0; index < controller.Movements.Length; index++)
      {
        var movement = controller.Movements[index];
        Guard guard = clock.Guards[index];
        char nominal = desired[movement.Link];
        char previous = clock.Last is not null ? clock.Last[movement.Link] : 'r';
        if (guard.Blocked && !guard.Held)
        {
          guard.Held = true;
          double yellow = controller.Phases.Where(p => p.State[movement.Link] is 'y' or 'Y').Select(p => p.Seconds).DefaultIfEmpty(3).Min();
          guard.YellowUntil = CompiledSignalPlan.Green(previous) ? time + yellow : time;
          guard.HoldUntil = guard.YellowUntil + 1;
        }
        if (guard.Held && (guard.Blocked || time < guard.HoldUntil))
        {
          if (CompiledSignalPlan.Green(nominal)) desired[movement.Link] = time < guard.YellowUntil ? 'y' : 'r';
          else if (nominal is 'y' or 'Y' && previous == 'r') desired[movement.Link] = 'r';
        }
        else if (CompiledSignalPlan.Green(nominal)) guard.Held = false;
      }
      clock.Last = new string(desired);
      states[controllerIndex] = new SignalState(controller.Id, clock.Last);
      foreach (var device in (project.TrafficLights?.Devices ?? []).Where(d => d.SignalId == controller.Id))
      {
        int[] owned = Enumerable.Range(0, controller.Movements.Length).Where(i => device.LinkIndices.Contains(controller.Movements[i].Link)).ToArray();
        diagnostics.Add(new TacoAdaptiveDevice(device.Id, owned.Any(i => controller.Movements[i].QueueBuffer),
          owned.Select(i => controller.Movements[i].CapacityVehicles).DefaultIfEmpty(0).Max(),
          owned.Count(i => clock.Guards[i].Blocked), owned.Select(i => clock.Guards[i].Occupancy).DefaultIfEmpty(0).Max(), clock.K,
          clock.Demands.ElementAtOrDefault(0), clock.Demands.ElementAtOrDefault(1), clock.Rush,
          controller.GroupLabels.ElementAtOrDefault(0) ?? "A", controller.GroupLabels.ElementAtOrDefault(1) ?? "B",
          controller.Phases.Select((phase, i) => phase.Group == 0 ? clock.Durations[i] : 0).Sum(),
          controller.Phases.Select((phase, i) => phase.Group == 1 ? clock.Durations[i] : 0).Sum()));
      }
    }
    Devices = diagnostics.ToArray();
    return states;
  }
}
