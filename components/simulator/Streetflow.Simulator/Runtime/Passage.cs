namespace Streetflow.Simulator.Runtime;

public sealed class Passage
{
  public string PairId { get; }
  public string VehicleId { get; }
  public double StartedAt { get; }
  public double? FinishedAt { get; }
  public double? DurationSeconds { get; }
  public string Status { get; }

  public Passage(string pairId, string vehicleId, double startedAt, double? finishedAt, double? durationSeconds, string status)
  {
    PairId = pairId;
    VehicleId = vehicleId;
    StartedAt = startedAt;
    FinishedAt = finishedAt;
    DurationSeconds = durationSeconds;
    Status = status;
  }

  public static int Compare(Passage left, Passage right)
  {
    int timeComparison = left.StartedAt.CompareTo(right.StartedAt);
    if (timeComparison != 0)
    {
      return timeComparison;
    }

    return StringComparer.Ordinal.Compare(left.VehicleId, right.VehicleId);
  }
}
