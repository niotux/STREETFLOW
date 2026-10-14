namespace Streetflow.Simulator.Scenarios;

public sealed record TransformerTensor
{
  public required string Dtype { get; init; }
  public required string[] AxisOrder { get; init; }
  public required int[] Shape { get; init; }
  public required double[] Values { get; init; }
}
