namespace Streetflow.Simulator.Runtime;

public sealed class PassageRecorder
{
  private readonly CheckpointConfiguration configuration;
  private readonly Dictionary<string, VehicleState> previous = new(StringComparer.Ordinal);
  private readonly Dictionary<PassageKey, double> starts = new();
  private readonly List<Passage> completed = [];
  private double previousTime;
  public PassageRecorder(CheckpointConfiguration configuration)
  {
    this.configuration = configuration;
  }

  public void Advance(EngineSnapshot snapshot)
  {
    List<PassageEvent> events = CollectEvents(snapshot);
    events.Sort(PassageEvent.Compare);
    foreach (PassageEvent crossing in events)
    {
      RecordCrossing(crossing);
    }

    previous.Clear();
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      previous.Add(vehicle.Id, vehicle);
    }

    previousTime = snapshot.Time;
  }

  private List<PassageEvent> CollectEvents(EngineSnapshot snapshot)
  {
    List<PassageEvent> events = [];
    if (snapshot.Crossings is not null)
    {
      foreach (LineCrossing crossing in snapshot.Crossings)
      {
        foreach (Checkpoint checkpoint in configuration.Checkpoints)
        {
          if (checkpoint.LaneId == crossing.LaneId && checkpoint.Position == crossing.Position)
          {
            events.Add(new PassageEvent(crossing.VehicleId, checkpoint.Id, crossing.Time));
          }
        }
      }

      return events;
    }

    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      if (!previous.TryGetValue(vehicle.Id, out VehicleState? oldVehicle))
      {
        continue;
      }

      if (oldVehicle.LaneId != vehicle.LaneId || vehicle.LanePosition <= oldVehicle.LanePosition)
      {
        continue;
      }

      foreach (Checkpoint checkpoint in configuration.Checkpoints)
      {
        bool crossedCheckpoint = checkpoint.LaneId == vehicle.LaneId && oldVehicle.LanePosition < checkpoint.Position && vehicle.LanePosition >= checkpoint.Position;
        if (!crossedCheckpoint)
        {
          continue;
        }

        double fraction = (checkpoint.Position - oldVehicle.LanePosition) / (vehicle.LanePosition - oldVehicle.LanePosition);
        double crossingTime = previousTime + fraction * (snapshot.Time - previousTime);
        events.Add(new PassageEvent(vehicle.Id, checkpoint.Id, crossingTime));
      }
    }

    return events;
  }

  private void RecordCrossing(PassageEvent crossing)
  {
    foreach (CheckpointPair pair in configuration.Pairs)
    {
      PassageKey key = new(pair.Id, crossing.VehicleId);
      if (pair.From == crossing.CheckpointId)
      {
        starts.TryAdd(key, crossing.Time);
      }
      else if (pair.To == crossing.CheckpointId && starts.Remove(key, out double startTime))
      {
        double duration = crossing.Time - startTime;
        completed.Add(new Passage(pair.Id, crossing.VehicleId, startTime, crossing.Time, duration, "completed"));
      }
    }
  }

  public Passage[] Records()
  {
    List<Passage> records = new(completed.Count + starts.Count);
    records.AddRange(completed);
    foreach (KeyValuePair<PassageKey, double> start in starts)
    {
      records.Add(new Passage(start.Key.PairId, start.Key.VehicleId, start.Value, null, null, "incomplete"));
    }

    records.Sort(Passage.Compare);
    return records.ToArray();
  }
}
