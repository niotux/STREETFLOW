namespace Streetflow.Simulator.Runtime;

public sealed class CheckpointConfiguration
{
  public Checkpoint[] Checkpoints { get; }
  public CheckpointPair[] Pairs { get; }

  public CheckpointConfiguration(Checkpoint[] checkpoints, CheckpointPair[] pairs)
  {
    Checkpoints = checkpoints;
    Pairs = pairs;
  }
}
