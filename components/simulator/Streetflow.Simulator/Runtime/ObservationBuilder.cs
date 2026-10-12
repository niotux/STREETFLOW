using Streetflow.Simulator.Scenarios;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Runtime;

public sealed class ObservationBuilder
{
  private const double TimeComparisonTolerance = 1e-8;
  private readonly Scenario scenario;
  private readonly Camera[] cameras;
  private readonly string sessionId;
  private readonly double windowSeconds;
  private Dictionary<string, VehicleState> previous = new(StringComparer.Ordinal);
  private Dictionary<string, VehicleState> current = new(StringComparer.Ordinal);
  private readonly Dictionary<string, double> stoppedSince = new(StringComparer.Ordinal);
  private readonly List<LineCrossing> crossings = [];
  private double previousTime;
  private double windowStart;
  private long sequence;
  public ObservationBuilder(Scenario scenario, string sessionId, double windowSeconds)
  {
    this.scenario = scenario;
    cameras = (Camera[])scenario.Cameras.Clone();
    SortCameras();
    this.sessionId = sessionId;
    this.windowSeconds = windowSeconds;
  }

  private void SortCameras()
  {
    for (int index = 1; index < cameras.Length; index++)
    {
      Camera camera = cameras[index];
      int position = index;
      while (position > 0 && StringComparer.Ordinal.Compare(cameras[position - 1].Id, camera.Id) > 0)
      {
        cameras[position] = cameras[position - 1];
        position--;
      }

      cameras[position] = camera;
    }
  }

  public TrafficObservation[] Advance(EngineSnapshot snapshot)
  {
    UpdateVehicleState(snapshot);
    double windowEnd = windowStart + windowSeconds;
    bool observationWindowIsComplete = snapshot.Time + TimeComparisonTolerance >= windowEnd;
    if (!observationWindowIsComplete)
    {
      return [];
    }

    TrafficObservation[] frames = new TrafficObservation[cameras.Length];
    for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
    {
      Camera camera = cameras[cameraIndex];
      LaneObservation[] laneObservations = new LaneObservation[camera.Lanes.Length];
      for (int laneIndex = 0; laneIndex < camera.Lanes.Length; laneIndex++)
      {
        laneObservations[laneIndex] = BuildLaneObservation(snapshot, camera.Lanes[laneIndex], windowEnd);
      }

      sequence++;
      ObservationWindow window = new(windowStart, windowEnd);
      frames[cameraIndex] = new TrafficObservation(sessionId, sequence, scenario.Manifest.NetworkRevision, scenario.Manifest.DetectorConfigRevision, window, camera.IntersectionId, camera.Id, laneObservations);
    }

    crossings.RemoveAll(crossing => crossing.Time < windowEnd);
    windowStart = windowEnd;
    return frames;
  }

  private void UpdateVehicleState(EngineSnapshot snapshot)
  {
    current.Clear();
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      current.Add(vehicle.Id, vehicle);
      if (vehicle.SpeedMps <= scenario.Manifest.QueueSpeedMps)
      {
        stoppedSince.TryAdd(vehicle.Id, snapshot.Time);
      }
      else
      {
        stoppedSince.Remove(vehicle.Id);
      }

      if (snapshot.Crossings is null)
      {
        AddInterpolatedCrossings(vehicle, snapshot.Time);
      }
    }

    if (snapshot.Crossings is not null)
    {
      foreach (LineCrossing crossing in snapshot.Crossings)
      {
        crossings.Add(crossing);
      }
    }

    List<string> vehiclesToRemove = [];
    foreach (string vehicleId in stoppedSince.Keys)
    {
      if (!current.ContainsKey(vehicleId))
      {
        vehiclesToRemove.Add(vehicleId);
      }
    }

    foreach (string vehicleId in vehiclesToRemove)
    {
      stoppedSince.Remove(vehicleId);
    }

    Dictionary<string, VehicleState> oldPrevious = previous;
    previous = current;
    current = oldPrevious;
    previousTime = snapshot.Time;
  }

  private void AddInterpolatedCrossings(VehicleState vehicle, double time)
  {
    if (!previous.TryGetValue(vehicle.Id, out VehicleState? oldVehicle))
    {
      return;
    }

    if (oldVehicle.LaneId != vehicle.LaneId || vehicle.LanePosition <= oldVehicle.LanePosition)
    {
      return;
    }

    HashSet<CountingLinePosition> countedLines = [];
    foreach (Camera camera in cameras)
    {
      foreach (CameraLane lane in camera.Lanes)
      {
        if (lane.LaneId != vehicle.LaneId || oldVehicle.LanePosition >= lane.CountPosition || vehicle.LanePosition < lane.CountPosition)
        {
          continue;
        }

        if (!countedLines.Add(new CountingLinePosition(lane.LaneId, lane.CountPosition)))
        {
          continue;
        }

        double fraction = (lane.CountPosition - oldVehicle.LanePosition) / (vehicle.LanePosition - oldVehicle.LanePosition);
        double crossingTime = previousTime + fraction * (time - previousTime);
        crossings.Add(new LineCrossing(vehicle.Id, lane.LaneId, lane.CountPosition, crossingTime));
      }
    }
  }

  private LaneObservation BuildLaneObservation(EngineSnapshot snapshot, CameraLane lane, double windowEnd)
  {
    int visibleCount = 0;
    int queuedCount = 0;
    double speedTotal = 0;
    int crossingCount = 0;
    foreach (VehicleState vehicle in snapshot.Vehicles)
    {
      if (vehicle.LaneId != lane.LaneId || vehicle.LanePosition < lane.StartPosition || vehicle.LanePosition > lane.EndPosition)
      {
        continue;
      }

      visibleCount++;
      speedTotal += vehicle.SpeedMps;
      bool hasStopTime = stoppedSince.TryGetValue(vehicle.Id, out double stoppedAt);
      bool hasWaitedLongEnough = hasStopTime && snapshot.Time - stoppedAt + TimeComparisonTolerance >= scenario.Manifest.QueueDurationSeconds;
      if (hasWaitedLongEnough)
      {
        queuedCount++;
      }
    }

    foreach (LineCrossing crossing in crossings)
    {
      if (crossing.LaneId == lane.LaneId && crossing.Position == lane.CountPosition && crossing.Time >= windowStart && crossing.Time < windowEnd)
      {
        crossingCount++;
      }
    }

    double? meanSpeed = visibleCount == 0 ? null : speedTotal / visibleCount;
    return new LaneObservation(lane.ApproachId, lane.LaneId, visibleCount, crossingCount, queuedCount, meanSpeed);
  }
}
