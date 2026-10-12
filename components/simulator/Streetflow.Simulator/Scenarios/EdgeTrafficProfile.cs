namespace Streetflow.Simulator.Scenarios;

public sealed record EdgeTrafficProfile
{
  public required string Edge { get; init; }
  public required TrafficInterval[] Intervals { get; init; }
}
