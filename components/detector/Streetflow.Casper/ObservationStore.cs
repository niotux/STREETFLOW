using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Casper;

public sealed class RequestError(int status, string code, string message) : Exception(message)
{
  public int Status { get; } = status;
  public string Code { get; } = code;
}

// Bounded latest-state storage: one frame and at most 16 retired session IDs per configured camera.
public sealed class ObservationStore(CasperConfiguration config)
{
  private sealed class CameraState
  {
    public CameraObservation? Frame;
    public Queue<string> Retired { get; } = new();
  }
  private readonly object gate = new();
  private readonly Dictionary<string, CameraState> states = config.Cameras.ToDictionary(x => x.Id, _ => new CameraState());
  private readonly Dictionary<string, CameraMapping> camerasById = config.Cameras.ToDictionary(x => x.Id, StringComparer.Ordinal);
  private readonly Dictionary<string, LaneMapping> mappingsByLane = config.LaneMappings.ToDictionary(x => x.LaneId, StringComparer.Ordinal);
  private readonly Dictionary<string, CameraMapping> cameraByLane = config.Cameras
    .SelectMany(camera => camera.LaneIds.Select(lane => (lane, camera)))
    .ToDictionary(pair => pair.lane, pair => pair.camera, StringComparer.Ordinal);
  private readonly Dictionary<string, string[]> devicesByLane = config.Devices
    .SelectMany(device => device.LaneIds.Select(lane => (lane, device.Id)))
    .GroupBy(pair => pair.lane).ToDictionary(group => group.Key,
      group => group.Select(pair => pair.Id).Distinct().ToArray(), StringComparer.Ordinal);
  private long sequence;
  public string SessionId { get; } = Guid.NewGuid().ToString("N");

  public static bool TokenMatches(string? supplied, string? expected)
  {
    if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(expected)) return false;
    return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
      SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
  }

  public object Ingest(string id, string? token, CameraObservation frame, DateTimeOffset now)
  {
    if (!camerasById.TryGetValue(id, out CameraMapping? camera))
      throw new RequestError(404, "unknown_camera", "Unknown camera");
    if (!TokenMatches(token, Environment.GetEnvironmentVariable(camera.TokenEnvironment)))
      throw new RequestError(401, "unauthorized", "A valid camera token is required");
    Validate(camera, frame, now);
    lock (gate)
    {
      CameraState state = states[id];
      CameraObservation? old = state.Frame;
      if (old is not null)
      {
        if (old.SessionId == frame.SessionId && old.Sequence == frame.Sequence)
        {
          if (JsonSerializer.Serialize(old, CasperConfiguration.Json) != JsonSerializer.Serialize(frame, CasperConfiguration.Json))
            throw new RequestError(409, "sequence_conflict", "A sequence cannot identify different measurements");
          return new { accepted = true, duplicate = true, sequence };
        }
        bool sameSession = old.SessionId == frame.SessionId;
        if ((sameSession && frame.Sequence <= old.Sequence) || state.Retired.Contains(frame.SessionId)
          || frame.Window.End <= old.Window.End || frame.Window.Start < old.Window.End)
          throw new RequestError(409, "out_of_order", "Older, overlapping, or retired-session measurements cannot replace current data");
        if (!sameSession)
        {
          state.Retired.Enqueue(old.SessionId);
          if (state.Retired.Count > 16) state.Retired.Dequeue();
        }
      }
      state.Frame = frame;
      sequence++;
      return new { accepted = true, duplicate = false, sequence };
    }
  }

  private void Validate(CameraMapping camera, CameraObservation f, DateTimeOffset now)
  {
    static void Require(bool valid, string message)
    {
      if (!valid) throw new RequestError(400, "invalid_observation", message);
    }
    Require(f.SchemaVersion == "1" && f.Source == "camera" && f.TimeBasis == "utc", "Expected schema 1 UTC camera observations");
    Require(!string.IsNullOrWhiteSpace(f.SessionId) && f.SessionId.Length <= 128 && f.Sequence > 0, "Invalid producer session/sequence");
    if (f.NetworkRevision != config.Revision || f.DetectorConfigRevision != config.DetectorConfigRevision)
      throw new RequestError(409, "changed_configuration", "Camera network/calibration revisions do not match Casper");
    Require(f.DetectorId == camera.Id && f.IntersectionId == camera.IntersectionId, "Camera identity does not match configuration");
    Require(f.Window is not null && f.Window.Start.Offset == TimeSpan.Zero && f.Window.End.Offset == TimeSpan.Zero
      && f.Window.End > f.Window.Start && (f.Window.End - f.Window.Start).TotalSeconds <= config.MaximumWindowSeconds,
      "Invalid UTC measurement window");
    Require(f.Window!.End <= now.AddSeconds(2), "Camera clock is ahead; synchronize clocks");
    Require(f.Status is "ok" or "partial" or "unavailable", "Invalid frame status");
    Require(f.Lanes is not null && f.Lanes.All(x => x is not null) && f.Lanes.Length == camera.LaneIds.Length
      && f.Lanes.Select(x => x.LaneId).Distinct().Count() == f.Lanes.Length
      && f.Lanes.All(x => camera.LaneIds.Contains(x.LaneId)), "Send every configured lane exactly once");
    foreach (LaneObservation lane in f.Lanes!)
    {
      Require(lane.ApproachId == mappingsByLane[lane.LaneId].ApproachId, "Incorrect approach mapping");
      Require(lane.MeasurementStatus is "ok" or "partial" or "unavailable", "Invalid lane status");
      Require(lane.VisibleVehicleCount is null or >= 0 && lane.CrossedVehicleCount is null or >= 0
        && lane.QueuedVehicleCount is null or >= 0
        && (lane.MeanSpeedMps is null || double.IsFinite(lane.MeanSpeedMps.Value) && lane.MeanSpeedMps >= 0), "Invalid measurement values");
      Require(lane.QueuedVehicleCount is null || lane.VisibleVehicleCount is not null && lane.QueuedVehicleCount <= lane.VisibleVehicleCount, "Queue exceeds visible coverage");
      Require(f.Status != "unavailable" && lane.MeasurementStatus != "unavailable"
        || lane.VisibleVehicleCount is null && lane.CrossedVehicleCount is null && lane.QueuedVehicleCount is null && lane.MeanSpeedMps is null,
        "Unavailable measurements must be null");
    }
  }

  public TacoTraffic Traffic(string session, DateTimeOffset now)
  {
    RequireSession(session);
    lock (gate)
    {
      List<TacoLaneTraffic> lanes = [];
      foreach (LaneMapping mapping in config.LaneMappings)
      {
        CameraMapping camera = cameraByLane[mapping.LaneId];
        CameraObservation? frame = states[camera.Id].Frame;
        LaneObservation? measured = frame?.Lanes.Single(x => x.LaneId == mapping.LaneId);
        double? age = frame is null ? null : Math.Max(0, (now - frame.Window.End).TotalSeconds);
        bool unavailable = frame is null || age > config.StaleAfterSeconds || frame.Status == "unavailable" || measured!.MeasurementStatus == "unavailable";
        string status = unavailable ? "unavailable" : frame!.Status == "partial" ? "partial" : measured!.MeasurementStatus;
        lanes.Add(new(mapping.LaneId, mapping.StreetId, mapping.IntersectionId,
          devicesByLane.GetValueOrDefault(mapping.LaneId) ?? [],
          camera.Id, frame?.Window.End, age, unavailable ? null : measured!.VisibleVehicleCount,
          unavailable ? null : measured!.CrossedVehicleCount, unavailable ? null : measured!.QueuedVehicleCount,
          unavailable ? null : measured!.MeanSpeedMps, status, frame?.SyntheticDemo == true ? "synthetic_demo" : "camera_image_tracking"));
      }
      string overall = lanes.All(x => x.MeasurementStatus == "unavailable") ? "unavailable"
        : lanes.All(x => x.MeasurementStatus == "ok") ? "ok" : "partial";
      // Each lane keeps its actual observation time; this response does not invent a simultaneous sample.
      return new(SessionId, config.WorldId, config.Revision, "camera", "utc", sequence,
        lanes.Max(x => x.ObservedAt), null, overall, lanes.ToArray());
    }
  }

  public void RequireSession(string id)
  {
    if (id != SessionId) throw new RequestError(409, "changed_session", "Refresh the active Casper session");
  }
}
