namespace Streetflow.Simulator.Runtime;

public sealed class Checkpoint
{
  public string Id { get; }
  public string LaneId { get; }
  public double Position { get; }

  public Checkpoint(string id, string laneId, double position)
  {
    Id = id;
    LaneId = laneId;
    Position = position;
  }
}
