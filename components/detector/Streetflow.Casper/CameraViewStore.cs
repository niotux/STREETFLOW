using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Streetflow.Casper;

public sealed class CameraViewStore
{
  private const int MaximumPreviewBytes = 256 * 1024;
  private readonly object gate = new();
  private readonly CasperConfiguration config;
  private readonly string calibrationDirectory;
  private readonly Dictionary<string, CameraViewCalibration> views = new(StringComparer.Ordinal);
  private readonly Dictionary<string, CameraPreview> previews = new(StringComparer.Ordinal);

  public CameraViewStore(CasperConfiguration config, string configurationPath, string calibrationDirectory)
  {
    this.config = config;
    this.calibrationDirectory = calibrationDirectory;
    foreach (CameraMapping camera in config.Cameras)
    {
      if (camera.ViewCalibrationFile is null)
      {
        continue;
      }
      string initialPath = Path.GetFullPath(camera.ViewCalibrationFile, Path.GetDirectoryName(configurationPath)!);
      string path = SavedPath(camera.Id);
      CameraViewCalibration view = JsonSerializer.Deserialize<CameraViewCalibration>(File.ReadAllText(File.Exists(path) ? path : initialPath), CasperConfiguration.Json)
        ?? throw new InvalidOperationException("Empty camera view calibration");
      Validate(camera, view, null);
      views.Add(camera.Id, view);
    }
  }

  public string Revision(string id)
  {
    lock (gate)
    {
      return views.TryGetValue(id, out CameraViewCalibration? view) ? view.DetectorConfigRevision : config.DetectorConfigRevision;
    }
  }

  public CameraViewCalibration Get(string id, string? token)
  {
    CameraMapping camera = Authenticate(id, token);
    lock (gate)
    {
      if (!views.TryGetValue(camera.Id, out CameraViewCalibration? view))
      {
        throw new RequestError(404, "no_calibration", "This camera has no configured view calibration");
      }
      return view;
    }
  }

  public CameraViewCalibration Save(string id, string? token, CameraViewCalibration view)
  {
    CameraMapping camera = Authenticate(id, token);
    lock (gate)
    {
      if (!views.TryGetValue(id, out CameraViewCalibration? previous))
      {
        throw new RequestError(404, "no_calibration", "This camera has no configured view calibration");
      }
      Validate(camera, view, previous);
      Directory.CreateDirectory(calibrationDirectory);
      string path = SavedPath(id);
      string temporary = path + ".tmp";
      File.WriteAllText(temporary, JsonSerializer.Serialize(view, CasperConfiguration.Json));
      File.Move(temporary, path, true);
      views[id] = view;
      previews.Remove(id);
      return view;
    }
  }

  public void PutPreview(string id, string? token, string? revision, string? status, DateTimeOffset observedAt, byte[] image)
  {
    Authenticate(id, token);
    if (image.Length < 4 || image.Length > MaximumPreviewBytes || image[0] != 0xff || image[1] != 0xd8
      || image[^2] != 0xff || image[^1] != 0xd9)
    {
      throw new RequestError(400, "invalid_preview", "Preview must be a JPEG of at most 256 KiB");
    }
    if (revision != Revision(id) || status is not ("ok" or "partial")
      || observedAt.Offset != TimeSpan.Zero || observedAt > DateTimeOffset.UtcNow.AddSeconds(2))
    {
      throw new RequestError(409, "changed_configuration", "Preview revision or timestamp is invalid");
    }
    lock (gate)
    {
      if (previews.TryGetValue(id, out CameraPreview? previous) && observedAt <= previous.ObservedAt)
      {
        return;
      }
      previews[id] = new CameraPreview(image, observedAt, revision!, status);
    }
  }

  public CameraPreview? Preview(string id)
  {
    RequireCamera(id);
    lock (gate)
    {
      return previews.GetValueOrDefault(id);
    }
  }

  public CameraStatus[] Cameras(DateTimeOffset now, ObservationStore observations)
  {
    lock (gate)
    {
      List<CameraStatus> result = [];
      foreach (CameraMapping camera in config.Cameras)
      {
        CameraPreview? preview = previews.GetValueOrDefault(camera.Id);
        double? age = preview is null ? null : Math.Max(0, (now - preview.ObservedAt).TotalSeconds);
        result.Add(new CameraStatus(camera.Id, camera.LaneIds, Revision(camera.Id), preview?.ObservedAt,
          age, age is null || age > config.StaleAfterSeconds ? "unavailable" : preview!.Status,
          observations.CameraStatus(camera.Id, now)));
      }
      return result.ToArray();
    }
  }

  private CameraMapping Authenticate(string id, string? token)
  {
    CameraMapping camera = RequireCamera(id);
    if (!ObservationStore.TokenMatches(token, Environment.GetEnvironmentVariable(camera.TokenEnvironment)))
    {
      throw new RequestError(401, "unauthorized", "A valid camera token is required");
    }
    return camera;
  }

  private CameraMapping RequireCamera(string id)
  {
    foreach (CameraMapping camera in config.Cameras)
    {
      if (camera.Id == id)
      {
        return camera;
      }
    }
    throw new RequestError(404, "unknown_camera", "Unknown camera");
  }

  private string SavedPath(string id)
  {
    string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    return Path.Combine(calibrationDirectory, name + ".json");
  }

  private void Validate(CameraMapping camera, CameraViewCalibration view, CameraViewCalibration? previous)
  {
    static void Require(bool valid, string message)
    {
      if (!valid)
      {
        throw new RequestError(400, "invalid_calibration", message);
      }
    }
    Require(view.DetectorId == camera.Id && view.IntersectionId == camera.IntersectionId
      && view.NetworkRevision == config.Revision && view.TokenEnvironment == camera.TokenEnvironment,
      "Camera identity and network revision must match Casper");
    Require(!string.IsNullOrWhiteSpace(view.DetectorConfigRevision) && view.DetectorConfigRevision.Length <= 128
      && (previous is null || view.DetectorConfigRevision != previous.DetectorConfigRevision), "Use a new calibration revision");
    Require(double.IsFinite(view.WindowSeconds) && view.WindowSeconds > 0 && view.WindowSeconds <= config.MaximumWindowSeconds
      && double.IsFinite(view.QueueDurationSeconds) && view.QueueDurationSeconds >= 0
      && double.IsFinite(view.StationaryRadius) && view.StationaryRadius > 0
      && double.IsFinite(view.MaximumTrackingGapSeconds) && view.MaximumTrackingGapSeconds > 0, "Invalid timing or tracking settings");
    Require(view.TrackingIouThreshold is null || double.IsFinite(view.TrackingIouThreshold.Value)
      && view.TrackingIouThreshold > 0 && view.TrackingIouThreshold <= 1, "Invalid tracking overlap threshold");
    Require(view.MaximumDetections is null || view.MaximumDetections >= 1 && view.MaximumDetections <= 500,
      "Invalid detection limit");
    if (view.Lanes is null || view.Lanes.Length != camera.LaneIds.Length)
    {
      throw new RequestError(400, "invalid_calibration", "Configure every camera lane once");
    }
    HashSet<string> seen = new(StringComparer.Ordinal);
    foreach (LaneViewCalibration lane in view.Lanes)
    {
      if (lane is null)
      {
        throw new RequestError(400, "invalid_calibration", "Invalid lane");
      }
      Require(MatchesLane(camera, lane) && seen.Add(lane.LaneId),
        "Invalid lane or approach identity");
      Require(ValidPoints(lane.Polygon, 3, false) && (lane.CountingLine is null || ValidPoints(lane.CountingLine, 2, true)),
        "Invalid normalized polygon or counting line");
    }
  }

  private bool MatchesLane(CameraMapping camera, LaneViewCalibration lane)
  {
    bool owned = false;
    foreach (string laneId in camera.LaneIds)
    {
      if (laneId == lane.LaneId)
      {
        owned = true;
        break;
      }
    }
    if (!owned)
    {
      return false;
    }
    foreach (LaneMapping mapping in config.LaneMappings)
    {
      if (mapping.LaneId == lane.LaneId && mapping.ApproachId == lane.ApproachId)
      {
        return true;
      }
    }
    return false;
  }

  private static bool ValidPoints(double[][]? points, int minimum, bool exact)
  {
    if (points is null || points.Length < minimum || exact && points.Length != minimum)
    {
      return false;
    }
    foreach (double[] point in points)
    {
      if (point is null || point.Length != 2 || !double.IsFinite(point[0]) || !double.IsFinite(point[1])
        || point[0] < 0 || point[0] > 1 || point[1] < 0 || point[1] > 1)
      {
        return false;
      }
    }
    return !exact || points[0][0] != points[1][0] || points[0][1] != points[1][1];
  }
}
