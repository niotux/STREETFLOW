using Streetflow.Simulator.City;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Tests;

public sealed class AdaptiveSignalTests
{
  private static (CityProject Project, CompiledSignalPlan Plan) Prepare(string id = "tiny-graph-devices")
  {
    CityProject project = CityProjectLoader.Load(Path.Combine(ProjectLoadingTests.Workspace, "projects", "drafts", id));
    var programs = project.SignalIds.ToDictionary(id => id, _ => "0");
    return (project, CompiledSignalPlan.Compile(project, programs)!);
  }

  [Fact]
  public void CompilesPoduRosStorageConnectionsAndPedestrianMinimumsAheadOfTime()
  {
    var (_, plan) = Prepare("podu-ros-neural");
    Assert.Equal(10, plan.Controllers.Length);
    Assert.Contains(plan.Controllers.SelectMany(c => c.Movements), movement => movement.QueueBuffer && movement.CapacityVehicles >= 20);
    Assert.All(plan.Controllers.SelectMany(c => c.Movements), movement =>
    {
      Assert.NotEmpty(movement.Exits);
      Assert.All(movement.Exits, exit => Assert.True(exit.Capacity > 0 && exit.Lanes.Length > 0));
    });
    Assert.All(plan.Controllers.SelectMany(c => c.Phases), phase =>
    {
      Assert.InRange(phase.MinimumSeconds, 0, phase.Seconds);
      if (phase.State.Any(c => c is 'y' or 'Y' or 'u') || phase.Seconds < 10) Assert.False(phase.Weighted);
    });
  }

  [Theory]
  [InlineData(100, 10, 1.8)]
  [InlineData(10, 100, 0.3300165012376031)]
  [InlineData(0, 1000, 0.2)]
  [InlineData(50, 50, 1)]
  public void DemandDirectionUsesFixedAAndBAndClampsMultiplier(double a, double b, double expected)
  {
    Assert.Equal(expected, AdaptiveSignalController.DemandMultiplier(a, b), 8);
  }

  [Theory]
  [InlineData(.2)]
  [InlineData(.7)]
  [InlineData(1)]
  [InlineData(1.8)]
  public void WeightingPreservesCycleAndClearanceWhileProtectingMinimumService(double k)
  {
    var phases = new[]
    {
      new CompiledSignalPlan.Phase(42, "Grr", 0, 10, true),
      new CompiledSignalPlan.Phase(3, "yrr", -1, 3, false),
      new CompiledSignalPlan.Phase(42, "rGr", 1, 10, true),
      new CompiledSignalPlan.Phase(3, "ryr", -1, 3, false)
    };
    double[] durations = AdaptiveSignalController.GreenDurations(phases, k);
    Assert.Equal(90, durations.Sum(), 8);
    Assert.Equal(3, durations[1]); Assert.Equal(3, durations[3]);
    Assert.InRange(durations[0], 10, 74); Assert.InRange(durations[2], 10, 74);
    if (k > 1) Assert.True(durations[0] > durations[2]);
    if (k < 1) Assert.True(durations[0] < durations[2]);
    if (k == 1) Assert.Equal(durations[0], durations[2]);
    if (k is > .5 and < 2) Assert.Equal(k * k, durations[0] / durations[2], 8);
  }

  [Fact]
  public void PedestrianMinimumsAndMaximumsAreSolvedTogetherWithoutChangingTheCycleBudget()
  {
    var phases = new[]
    {
      new CompiledSignalPlan.Phase(80, "Gr", 0, 80, true),
      new CompiledSignalPlan.Phase(85, "rG", 1, 80, true)
    };
    double[] durations = AdaptiveSignalController.GreenDurations(phases, 1.8);
    Assert.Equal(165, durations.Sum(), 8);
    Assert.Equal(85, durations[0], 8);
    Assert.Equal(80, durations[1], 8);
  }

  [Fact]
  public void BlockedHighDemandDirectionCannotTakeTheClearDirectionsRushHourBudget()
  {
    var (project, plan) = Prepare();
    var junction = plan.Controllers[0];
    var fullExit = junction.Movements[junction.GroupMovements[0][0]].Exits[0];
    var clearExit = junction.Movements.SelectMany(m => m.Exits).First(e => e.Lanes[0] != fullExit.Lanes[0]);
    for (int index = 0; index < junction.Movements.Length; index++)
      junction.Movements[index] = junction.Movements[index] with
      { Exits = [junction.GroupMovements[0].Contains(index) ? fullExit : clearExit] };
    LaneMap blocked = plan.Lanes[fullExit.Lanes[0]];
    LaneMap sourceA = plan.Lanes[junction.GroupLanes[0][0]], sourceB = plan.Lanes[junction.GroupLanes[1][0]];
    List<VehicleState> cars = [new("blocked-exit", blocked.Id, 6, 0, 0, 0, 0, blocked.EdgeId)];
    for (int i = 0; i < 20; i++) cars.Add(new VehicleState($"a-{i}", sourceA.Id, 20 + i * 7.5, 0, 0, 0, 0, sourceA.EdgeId));
    cars.Add(new VehicleState("b", sourceB.Id, 100, 0, 0, 5, 0, sourceB.EdgeId));
    var controller = new AdaptiveSignalController(plan, project);
    controller.Update(cars.ToArray(), 0, 8 * 3600);
    TacoAdaptiveDevice status = controller.Devices[0];
    Assert.Equal(0, status.DemandA);
    Assert.True(status.DemandB > 0 && status.Multiplier < 1);
    Assert.True(status.GreenBSeconds > status.GreenASeconds);
  }

  [Fact]
  public void FullReceivingRoadClosesBufferedApproachWithYellowAndReopensAfterSpaceClears()
  {
    var (project, plan) = Prepare();
    var controller = new AdaptiveSignalController(plan, project);
    controller.Configure(new TacoSignalPolicy(WeightDemand: false));
    var junction = plan.Controllers[0];
    int link = junction.Movements.First(m => CompiledSignalPlan.Green(junction.Phases[0].State[m.Link])).Link;
    var movement = junction.Movements.Single(m => m.Link == link);
    Assert.True(movement.QueueBuffer);
    int receiving = movement.Exits[0].Lanes[0];
    LaneMap lane = plan.Lanes[receiving];
    VehicleState[] jam = Enumerable.Range(0, (int)(lane.Length / 7.5)).Select(i =>
      new VehicleState($"jam-{i}", lane.Id, 6 + i * 7.5, 0, 0, 0, 0, lane.EdgeId)).ToArray();
    Assert.True(CompiledSignalPlan.Green(controller.Update([], 0, 8 * 3600)[0].State[link]));
    Assert.Equal('y', controller.Update(jam, .5, 8 * 3600 + .5)[0].State[link]);
    Assert.Equal('r', controller.Update(jam, 3.5, 8 * 3600 + 3.5)[0].State[link]);
    Assert.Contains(controller.Devices, d => d.BlockedMovements > 0 && d.QueueBuffer);
    Assert.Equal('r', controller.Update([], 4, 8 * 3600 + 4)[0].State[link]);
    Assert.True(CompiledSignalPlan.Green(controller.Update([], 4.5, 8 * 3600 + 4.5)[0].State[link]));
    Assert.All(controller.Devices, d => Assert.Equal(0, d.BlockedMovements));
  }

  [Fact]
  public void BlockedAtActivationStaysRedAndUnblockedTurnsRemainAvailable()
  {
    var (project, plan) = Prepare();
    var controller = new AdaptiveSignalController(plan, project);
    controller.Configure(new TacoSignalPolicy(WeightDemand: false));
    var junction = plan.Controllers[0];
    var movement = junction.Movements.First(m => CompiledSignalPlan.Green(junction.Phases[0].State[m.Link]));
    var exit = plan.Lanes[movement.Exits[0].Lanes[0]];
    VehicleState[] jam = [new VehicleState("blocked-entry", exit.Id, 6, 0, 0, 0, 0, exit.EdgeId)];
    string state = controller.Update(jam, 0, 8 * 3600)[0].State;
    Assert.Equal('r', state[movement.Link]);
    Assert.Contains(junction.Movements.Where(m => m.Link != movement.Link), m => CompiledSignalPlan.Green(state[m.Link]));
    controller.Configure(new TacoSignalPolicy(PreventSpillback: false, WeightDemand: false));
    Assert.True(CompiledSignalPlan.Green(controller.Update(jam, 2, 8 * 3600 + 2)[0].State[movement.Link]));
  }

  [Theory]
  [InlineData(6.99, false)]
  [InlineData(7, true)]
  [InlineData(8.99, true)]
  [InlineData(9, false)]
  [InlineData(16, true)]
  [InlineData(19, false)]
  [InlineData(32, true)]
  public void WeightingUsesRepeatingCityRushHours(double hour, bool rush) => Assert.Equal(rush, AdaptiveSignalController.RushHour(hour * 3600));
}
