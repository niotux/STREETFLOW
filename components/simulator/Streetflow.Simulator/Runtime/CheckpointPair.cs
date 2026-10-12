namespace Streetflow.Simulator.Runtime;

public sealed class CheckpointPair
{
  public string Id { get; }
  public string From { get; }
  public string To { get; }

  public CheckpointPair(string id, string from, string to)
  {
    Id = id;
    From = from;
    To = to;
  }
}
