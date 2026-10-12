namespace Streetflow.Simulator.Runtime;

public sealed class PassageEvent
{
  public string VehicleId { get; }
  public string CheckpointId { get; }
  public double Time { get; }

  public PassageEvent(string vehicleId, string checkpointId, double time)
  {
    VehicleId = vehicleId;
    CheckpointId = checkpointId;
    Time = time;
  }

  public static int Compare(PassageEvent left, PassageEvent right)
  {
    int timeComparison = left.Time.CompareTo(right.Time);
    if (timeComparison != 0)
    {
      return timeComparison;
    }

    return StringComparer.Ordinal.Compare(left.CheckpointId, right.CheckpointId);
  }
}
