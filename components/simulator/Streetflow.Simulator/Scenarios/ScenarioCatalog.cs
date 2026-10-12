using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public sealed class ScenarioCatalog
{
  private const int MaximumRequestIdLength = 128;
  private const double MinimumStepSeconds = 0.05;
  private const double MaximumStepSeconds = 5;
  private const double ConfigurationTolerance = 1e-8;
  private const double MaximumPacingMultiplier = 100;
  private readonly Dictionary<string, Scenario> scenarios = new(StringComparer.Ordinal);
  public ScenarioCatalog(SimulatorOptions options)
  {
    var root = Path.GetFullPath(options.ScenarioRoot);
    if (!Directory.Exists(root))
    {
      throw new InvalidOperationException($"Scenario directory does not exist: {root}");
    }

    foreach (var file in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
    {
      Scenario scenario = Load(file);
      if (!scenarios.TryAdd(scenario.Manifest.Id, scenario))
      {
        throw new InvalidOperationException($"Duplicate scenario ID: {scenario.Manifest.Id}");
      }
    }
  }

  public List<ScenarioInfo> List()
  {
    List<ScenarioInfo> result = new(scenarios.Count);
    foreach (Scenario scenario in scenarios.Values)
    {
      result.Add(scenario.Info);
    }

    result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Manifest.Id, right.Manifest.Id));
    return result;
  }

  public Scenario Get(string id)
  {
    return (string.IsNullOrWhiteSpace(id) ? null : scenarios.GetValueOrDefault(id)) ?? throw new ApiException(404, "unknown_scenario", $"Unknown scenario: {id}");
  }

  public static Scenario Load(string manifestPath)
  {
    string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
    ScenarioManifest manifest = JsonSerializer.Deserialize<ScenarioManifest>(File.ReadAllText(manifestPath), JsonDefaults.Options) ?? throw new InvalidDataException("Empty scenario manifest");
    string netPath = ResolveFile(directory, manifest.NetworkFile);
    bool identityIsPresent = !string.IsNullOrWhiteSpace(manifest.Id) && !string.IsNullOrWhiteSpace(manifest.Revision);
    bool queueSpeedIsValid = double.IsFinite(manifest.QueueSpeedMps) && manifest.QueueSpeedMps >= 0;
    bool queueDurationIsValid = double.IsFinite(manifest.QueueDurationSeconds) && manifest.QueueDurationSeconds >= 0;
    if (!identityIsPresent || !queueSpeedIsValid || !queueDurationIsValid)
    {
      throw new InvalidDataException("Scenario identity and nonnegative queue rules are required");
    }

    string routesPath = ResolveFile(directory, manifest.RoutesFile);
    XElement net = XDocument.Load(netPath).Root ?? throw new InvalidDataException("Missing network root");
    LaneMap[] lanes = ReadLanes(net);
    JunctionMap[] junctions = ReadJunctions(net);
    Camera[] cameras = ReadCameras(directory, junctions, lanes);
    ScheduledTrip[] trips = ReadTrips(routesPath);
    CheckpointConfiguration checkpoints = ReadCheckpoints(directory, lanes);
    return new Scenario
    {
      Directory = directory,
      Manifest = manifest,
      Lanes = lanes,
      Junctions = junctions,
      Cameras = cameras,
      Trips = trips,
      Checkpoints = checkpoints
    };
  }

  private static LaneMap[] ReadLanes(XElement net)
  {
    List<LaneMap> laneList = new();
    foreach (XElement edge in net.Elements("edge"))
    {
      string edgeId = Required(edge, "id");
      foreach (XElement lane in edge.Elements("lane"))
      {
        laneList.Add(new LaneMap(Required(lane, "id"), edgeId, Number(lane, "length"), Number(lane, "speed"), Shape(lane)));
      }
    }

    LaneMap[] lanes = laneList.ToArray();
    return lanes;
  }

  private static JunctionMap[] ReadJunctions(XElement net)
  {
    List<JunctionMap> junctionList = new();
    foreach (XElement junction in net.Elements("junction"))
    {
      junctionList.Add(new JunctionMap(Required(junction, "id"), Required(junction, "type"), Number(junction, "x"), Number(junction, "y"), Shape(junction)));
    }

    JunctionMap[] junctions = junctionList.ToArray();
    return junctions;
  }

  private static Camera[] ReadCameras(string directory, JunctionMap[] junctions, LaneMap[] lanes)
  {
    string cameraPath = Path.Combine(directory, "cameras.json");
    Camera[] cameras = [];
    if (File.Exists(cameraPath))
    {
      cameras = JsonSerializer.Deserialize<Camera[]>(File.ReadAllText(cameraPath), JsonDefaults.Options) ?? [];
    }

    Array.Sort(cameras, (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
    HashSet<string> cameraIds = new(StringComparer.Ordinal);
    foreach (Camera camera in cameras)
    {
      if (!cameraIds.Add(camera.Id))
      {
        throw new InvalidDataException("Duplicate camera IDs");
      }
    }

    foreach (Camera camera in cameras)
    {
      if (!ContainsJunction(junctions, camera.IntersectionId))
      {
        throw new InvalidDataException($"Unknown camera junction: {camera.IntersectionId}");
      }

      HashSet<string> cameraLaneIds = new(StringComparer.Ordinal);
      foreach (CameraLane cameraLane in camera.Lanes)
      {
        if (!cameraLaneIds.Add(cameraLane.LaneId))
        {
          throw new InvalidDataException("Duplicate camera lane");
        }
      }

      foreach (CameraLane region in camera.Lanes)
      {
        LaneMap? lane = FindLane(lanes, region.LaneId);
        if (lane is null)
        {
          throw new InvalidDataException($"Unknown camera lane: {region.LaneId}");
        }

        bool positionsAreFinite = double.IsFinite(region.StartPosition) && double.IsFinite(region.EndPosition) && double.IsFinite(region.CountPosition);
        bool regionIsWithinLane = region.StartPosition >= 0 && region.EndPosition <= lane.Length;
        bool regionHasPositiveLength = region.StartPosition < region.EndPosition;
        bool countPositionIsWithinRegion = region.CountPosition >= region.StartPosition && region.CountPosition <= region.EndPosition;
        if (!positionsAreFinite || !regionIsWithinLane || !regionHasPositiveLength || !countPositionIsWithinRegion)
        {
          throw new InvalidDataException($"Invalid camera region: {region.LaneId}");
        }
      }
    }

    return cameras;
  }

  private static ScheduledTrip[] ReadTrips(string routesPath)
  {
    XElement routes = XDocument.Load(routesPath).Root ?? throw new InvalidDataException("Missing routes root");
    // Explicit vehicles make demand and accounting inspectable. Native flows/trips are still passed to SUMO.
    Dictionary<string, string[]> namedRoutes = new(StringComparer.Ordinal);
    foreach (XElement route in routes.Elements("route"))
    {
      namedRoutes.Add(Required(route, "id"), Required(route, "edges").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    List<ScheduledTrip> tripList = new();
    foreach (XElement vehicle in routes.Elements("vehicle"))
    {
      string[] routeEdges = [];
      XElement? inlineRoute = vehicle.Element("route");
      if (inlineRoute is not null)
      {
        routeEdges = Required(inlineRoute, "edges").Split(' ', StringSplitOptions.RemoveEmptyEntries);
      }
      else
      {
        string routeId = (string?)vehicle.Attribute("route") ?? "";
        if (namedRoutes.TryGetValue(routeId, out string[]? namedRoute))
        {
          routeEdges = namedRoute;
        }
      }

      tripList.Add(new ScheduledTrip(Required(vehicle, "id"), Number(vehicle, "depart"), routeEdges));
    }

    ScheduledTrip[] trips = tripList.ToArray();
    HashSet<string> tripIds = new(StringComparer.Ordinal);
    foreach (ScheduledTrip trip in trips)
    {
      if (!tripIds.Add(trip.Id))
      {
        throw new InvalidDataException("Duplicate vehicle IDs");
      }
    }

    return trips;
  }

  private static CheckpointConfiguration ReadCheckpoints(string directory, LaneMap[] lanes)
  {
    string checkpointPath = Path.Combine(directory, "checkpoints.json");
    CheckpointConfiguration checkpoints = new([], []);
    if (File.Exists(checkpointPath))
    {
      checkpoints = JsonSerializer.Deserialize<CheckpointConfiguration>(File.ReadAllText(checkpointPath), JsonDefaults.Options) ?? new CheckpointConfiguration([], []);
    }

    HashSet<string> checkpointIds = new(StringComparer.Ordinal);
    foreach (Checkpoint checkpoint in checkpoints.Checkpoints)
    {
      if (!checkpointIds.Add(checkpoint.Id))
      {
        throw new InvalidDataException("Duplicate checkpoint or pair IDs");
      }
    }

    HashSet<string> pairIds = new(StringComparer.Ordinal);
    foreach (CheckpointPair pair in checkpoints.Pairs)
    {
      if (!pairIds.Add(pair.Id))
      {
        throw new InvalidDataException("Duplicate checkpoint or pair IDs");
      }
    }

    foreach (Checkpoint checkpoint in checkpoints.Checkpoints)
    {
      LaneMap? lane = FindLane(lanes, checkpoint.LaneId);
      if (lane is null)
      {
        throw new InvalidDataException($"Invalid checkpoint: {checkpoint.Id}");
      }

      bool positionIsFiniteAndNonnegative = double.IsFinite(checkpoint.Position) && checkpoint.Position >= 0;
      bool positionIsWithinLane = checkpoint.Position <= lane.Length;
      if (!positionIsFiniteAndNonnegative || !positionIsWithinLane)
      {
        throw new InvalidDataException($"Invalid checkpoint: {checkpoint.Id}");
      }
    }

    foreach (CheckpointPair pair in checkpoints.Pairs)
    {
      bool checkpointsAreDifferent = pair.From != pair.To;
      bool fromCheckpointExists = ContainsCheckpoint(checkpoints.Checkpoints, pair.From);
      bool toCheckpointExists = ContainsCheckpoint(checkpoints.Checkpoints, pair.To);
      if (!checkpointsAreDifferent || !fromCheckpointExists || !toCheckpointExists)
      {
        throw new InvalidDataException($"Invalid checkpoint pair: {pair.Id}");
      }
    }

    return checkpoints;
  }

  public static RunConfiguration Validate(Scenario scenario, CreateRunRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > MaximumRequestIdLength)
    {
      throw new ApiException(400, "invalid_request_id", "request_id is required and must be at most 128 characters");
    }

    if (request.Seed < 0)
    {
      throw new ApiException(400, "invalid_seed", "seed must be nonnegative");
    }

    if (request.ScenarioRevision is not null && request.ScenarioRevision != scenario.Manifest.Revision)
    {
      throw new ApiException(409, "revision_mismatch", "Requested scenario revision is not registered");
    }

    ScenarioManifest manifest = scenario.Manifest;
    double stepSeconds = request.Overrides?.StepSeconds ?? manifest.StepSeconds;
    double observationWindowSeconds = request.Overrides?.ObservationWindowSeconds ?? manifest.ObservationWindowSeconds;
    double durationSeconds = request.Overrides?.DurationSeconds ?? manifest.DurationSeconds;
    double demandMultiplier = request.Overrides?.DemandMultiplier ?? 1;
    RunConfiguration config = new(manifest.Id, manifest.Revision, request.Seed, stepSeconds, observationWindowSeconds, durationSeconds, demandMultiplier);
    bool stepIsFinite = double.IsFinite(config.StepSeconds);
    bool stepIsInRange = config.StepSeconds >= MinimumStepSeconds && config.StepSeconds <= MaximumStepSeconds;
    bool durationIsFiniteAndPositive = double.IsFinite(config.DurationSeconds) && config.DurationSeconds > 0;
    bool observationWindowIsFinite = double.IsFinite(config.ObservationWindowSeconds);
    bool observationWindowIsAtLeastOneStep = config.ObservationWindowSeconds >= config.StepSeconds;
    bool observationWindowMatchesStep = IsMultipleOfStep(config.ObservationWindowSeconds, config.StepSeconds);
    bool durationMatchesStep = IsMultipleOfStep(config.DurationSeconds, config.StepSeconds);
    if (!stepIsFinite || !stepIsInRange || !durationIsFiniteAndPositive || !observationWindowIsFinite || !observationWindowIsAtLeastOneStep || !observationWindowMatchesStep || !durationMatchesStep)
    {
      throw new ApiException(400, "invalid_configuration", "Use a step from 0.05 to 5 seconds; positive duration and observation window must be multiples of the step");
    }

    if (!double.IsFinite(config.DemandMultiplier) || config.DemandMultiplier < 0 || config.DemandMultiplier * scenario.Trips.Length > int.MaxValue)
    {
      throw new ApiException(400, "invalid_demand", "demand_multiplier must be finite and nonnegative; generated explicit trips must fit the engine's integer accounting");
    }

    ValidatePacing(request.Overrides?.Pacing ?? new());
    return config;
  }

  public static void ValidatePacing(Pacing pacing)
  {
    bool modeIsSupported = pacing.Mode is "realtime" or "unpaced";
    bool multiplierIsFinite = double.IsFinite(pacing.Multiplier);
    bool multiplierIsInRange = pacing.Multiplier > 0 && pacing.Multiplier <= MaximumPacingMultiplier;
    if (!modeIsSupported || !multiplierIsFinite || !multiplierIsInRange)
    {
      throw new ApiException(400, "invalid_pacing", "mode must be realtime or unpaced; multiplier must be greater than 0 and at most 100");
    }
  }

  private static bool IsMultipleOfStep(double value, double stepSeconds)
  {
    if (stepSeconds <= 0)
    {
      return false;
    }

    double stepCount = value / stepSeconds;
    double nearestStepCount = Math.Round(stepCount);
    return Math.Abs(stepCount - nearestStepCount) <= ConfigurationTolerance;
  }

  private static string ResolveFile(string directory, string relative)
  {
    var full = Path.GetFullPath(Path.Combine(directory, relative));
    if (Path.IsPathRooted(relative) || !full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(full))
    {
      throw new InvalidDataException($"Scenario file must exist within its package: {relative}");
    }

    return full;
  }

  private static string Required(XElement element, string attr)
  {
    return (string?)element.Attribute(attr) ?? throw new InvalidDataException($"Missing {attr}");
  }

  private static double Number(XElement element, string attr)
  {
    return double.Parse(Required(element, attr), CultureInfo.InvariantCulture);
  }

  private static Point[] Shape(XElement element)
  {
    string shape = (string?)element.Attribute("shape") ?? "";
    string[] coordinates = shape.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    Point[] points = new Point[coordinates.Length];
    for (int index = 0; index < coordinates.Length; index++)
    {
      string[] coordinate = coordinates[index].Split(',');
      double x = double.Parse(coordinate[0], CultureInfo.InvariantCulture);
      double y = double.Parse(coordinate[1], CultureInfo.InvariantCulture);
      points[index] = new Point(x, y);
    }

    return points;
  }

  private static bool ContainsJunction(JunctionMap[] junctions, string junctionId)
  {
    foreach (JunctionMap junction in junctions)
    {
      if (junction.Id == junctionId)
      {
        return true;
      }
    }

    return false;
  }

  private static LaneMap? FindLane(LaneMap[] lanes, string laneId)
  {
    foreach (LaneMap lane in lanes)
    {
      if (lane.Id == laneId)
      {
        return lane;
      }
    }

    return null;
  }

  private static bool ContainsCheckpoint(Checkpoint[] checkpoints, string checkpointId)
  {
    foreach (Checkpoint checkpoint in checkpoints)
    {
      if (checkpoint.Id == checkpointId)
      {
        return true;
      }
    }

    return false;
  }
}
