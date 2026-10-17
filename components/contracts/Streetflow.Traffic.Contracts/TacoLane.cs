namespace Streetflow.Traffic.Contracts;

public sealed record TacoLane(string Id, TacoPoint[] Shape)
{
  // The single lane that feeds this one, when there is exactly one; queues extend back along it.
  public string? UpstreamLaneId { get; init; }
}
