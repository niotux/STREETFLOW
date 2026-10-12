using System.Text.Json;
using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Runtime;

public sealed partial class RunCoordinator
{
  public sealed class RunReader(RunCoordinator owner)
  {
    public RunStatus Status(string id)
    {
      return owner.Find(id);
    }

    public RunStatus[] List()
    {
      List<RunStatus> runs = new(owner.archive.Count);
      foreach (RunStatus archivedRun in owner.archive.Values)
      {
        RunStatus run = owner.current?.Id == archivedRun.Id ? owner.current : archivedRun;
        runs.Add(run);
      }

      runs.Sort((left, right) => right.CreatedAt.CompareTo(left.CreatedAt));
      return runs.ToArray();
    }

    public Snapshot Snapshot(string id)
    {
      RunStatus status = owner.Find(id);
      if (owner.snapshot?.RunId == id)
      {
        return owner.snapshot;
      }

      return new Snapshot(id, 0, status.SimulationTime, status.ActiveVehicles, [], [], true, false);
    }

    public Scenario Scenario(string id)
    {
      _ = owner.Find(id);
      return owner.current?.Id == id ? owner.scenario! : ScenarioCatalog.Load(Path.Combine(owner.storage.DirectoryFor(id), "manifest.json"));
    }

    public TripRecord[] Trips(string id)
    {
      if (owner.current?.Id != id)
      {
        return owner.storage.LoadTrips(id);
      }

      List<TripRecord> orderedTrips = new(owner.trips.Values);
      orderedTrips.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
      return orderedTrips.ToArray();
    }

    public Passage[] Passages(string id)
    {
      _ = owner.Find(id);
      if (owner.current?.Id == id)
      {
        return owner.passages!.Records();
      }

      string path = Path.Combine(owner.storage.DirectoryFor(id), "passages.json");
      return File.Exists(path) ? JsonSerializer.Deserialize<Passage[]>(File.ReadAllText(path), JsonDefaults.Options) ?? [] : [];
    }

    public TrafficObservation[] ArchivedFrames(string id, long after, int limit)
    {
      _ = owner.Find(id);
      return owner.storage.ReadObservations(id, after, limit);
    }

    public string? SessionId => owner.current?.Id;
    public TrafficObservation[] Frames => owner.observations.ToArray();
    public Scenario? CurrentScenario => owner.scenario;
  }
}
