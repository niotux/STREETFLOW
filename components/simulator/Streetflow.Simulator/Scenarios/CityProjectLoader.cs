using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public static class CityProjectLoader
{
  private const int SecondsPerDay = 86400;
  private const int HashBufferSize = 65536;
  private const int MaximumProjectIdLength = 128;
  private const double MinimumStepSeconds = 0.05;
  private const double MaximumStepSeconds = 5;
  private static readonly JsonSerializerOptions StrictJson = new(JsonDefaults.Options)
  {
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false,
    PropertyNameCaseInsensitive = false
  };
  private static readonly HashSet<string> VehicleClasses = new(StringComparer.Ordinal)
  {
    "ignoring",
    "private",
    "emergency",
    "authority",
    "army",
    "vip",
    "passenger",
    "hov",
    "taxi",
    "bus",
    "coach",
    "delivery",
    "truck",
    "trailer",
    "motorcycle",
    "moped",
    "bicycle",
    "evehicle",
    "tram",
    "rail_urban",
    "rail",
    "rail_electric",
    "rail_fast",
    "ship",
    "custom1",
    "custom2",
    "container",
    "cable_car",
    "subway",
    "aircraft",
    "wheelchair",
    "scooter",
    "drone"
  };
  public static CityProject Load(string directory)
  {
    directory = Path.GetFullPath(directory);
    ProjectManifest manifest = ReadJson<ProjectManifest>(directory, "experiment.json");
    ValidateManifest(manifest);
    ProjectDemand demand = ReadJson<ProjectDemand>(directory, manifest.DemandFile);
    ValidateSettings(demand, manifest.DemandFile);
    XElement net = ReadXml(directory, manifest.Network!.File, "net");
    foreach (XElement element in net.Descendants())
    {
      bool containsDemand = element.Name.LocalName is "vehicle" or "trip" or "flow" or "route" or "include";
      Require(!containsDemand, $"{manifest.Network.File}: network must not contain authored demand or external includes");
    }

    ProjectEdge[] edges = ReadEdges(net, manifest.Network.File);
    JunctionMap[] junctions = ReadJunctions(net, manifest.Network.File);
    ValidateEdgeJunctions(edges, junctions, manifest.Network.File);
    ProjectConnection[] connections = ReadConnections(net, edges, manifest.Network.File);
    HashSet<string> signals = new(StringComparer.Ordinal);
    ValidateSignals(net, manifest.Network.File, signals);
    foreach (string additional in manifest.AdditionalFiles!)
    {
      XElement extra = ReadXml(directory, additional, "additional");
      foreach (XElement element in extra.Elements())
      {
        Require(element.Name == "tlLogic", $"{additional}: additional_files currently support signal programs only");
      }

      ValidateSignals(extra, additional, signals);
    }

    foreach (XElement connection in net.Elements("connection"))
    {
      string? signal = (string?)connection.Attribute("tl");
      if (signal is not null)
      {
        Require(signals.Contains(signal), $"{manifest.Network.File}: connection references unknown signal '{connection.Attribute("tl")}'");
      }
    }

    ValidateProfiles(demand, edges, manifest.DemandFile);
    if (manifest.DetectorsFile is not null)
    {
      JsonElement detectors = ReadJson<JsonElement>(directory, manifest.DetectorsFile);
      Require(detectors.ValueKind == JsonValueKind.Array && detectors.GetArrayLength() == 0, $"{manifest.DetectorsFile}: detectors must be an empty reserved array");
    }

    ProjectTerminal[] terminals = [];
    if (manifest.TerminalsFile is not null)
    {
      terminals = ReadJson<ProjectTerminal[]>(directory, manifest.TerminalsFile);
      ValidateTerminals(terminals, edges, demand, manifest.TerminalsFile);
    }

    if (demand.Generation.EndpointSelection == "terminals")
    {
      Require(terminals.Length > 0, $"{manifest.DemandFile}: endpoint_selection 'terminals' requires nonempty terminals_file");
    }

    string revision = CalculateRevision(directory, manifest);
    List<string> signalIds = new(signals);
    signalIds.Sort(StringComparer.Ordinal);
    return new CityProject
    {
      Directory = directory,
      Manifest = manifest,
      Demand = demand,
      Edges = edges,
      Junctions = junctions,
      Connections = connections,
      SignalIds = signalIds.ToArray(),
      Revision = revision,
      Terminals = terminals
    };
  }

  private static void ValidateManifest(ProjectManifest manifest)
  {
    Require(manifest.FormatVersion == 1, "experiment.json: unsupported format_version; expected 1");
    Require(ValidId(manifest.Id), "experiment.json: id must contain 1–128 non-whitespace characters");
    Require(manifest.Id.Length <= MaximumProjectIdLength, "experiment.json: id must contain 1–128 non-whitespace characters");
    Require(!string.IsNullOrWhiteSpace(manifest.Name), "experiment.json: name is required");
    Require(manifest.Network is not null, "experiment.json: network is required");
    Require(manifest.Defaults is not null && manifest.AdditionalFiles is not null, "experiment.json: defaults and additional_files cannot be null");
    ProjectDefaults defaults = manifest.Defaults!;
    Require(double.IsFinite(defaults.StartTimeSeconds) && defaults.StartTimeSeconds >= 0 && defaults.StartTimeSeconds < SecondsPerDay, "experiment.json: defaults.start_time_seconds must be finite and in [0, 86400)");
    Require(double.IsFinite(defaults.StepSeconds) && defaults.StepSeconds >= MinimumStepSeconds && defaults.StepSeconds <= MaximumStepSeconds, "experiment.json: defaults.step_seconds must be from 0.05 to 5 seconds");
  }

  private static void ValidateEdgeJunctions(ProjectEdge[] edges, JunctionMap[] junctions, string file)
  {
    HashSet<string> ids = new(StringComparer.Ordinal);
    foreach (JunctionMap junction in junctions)
    {
      ids.Add(junction.Id);
    }

    foreach (ProjectEdge edge in edges)
    {
      if (edge.Ordinary)
      {
        Require(ids.Contains(edge.From!) && ids.Contains(edge.To!), $"{file}: edge '{edge.Id}' refers to an unknown junction");
      }
    }
  }

  private static string CalculateRevision(string directory, ProjectManifest manifest)
  {
    List<string> references = ["experiment.json", manifest.Network.File, manifest.DemandFile];
    references.AddRange(manifest.AdditionalFiles);
    if (manifest.DetectorsFile is not null)
    {
      references.Add(manifest.DetectorsFile);
    }

    if (manifest.TerminalsFile is not null)
    {
      references.Add(manifest.TerminalsFile);
    }

    HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
    foreach (string reference in references)
    {
      Require(normalized.Add(ProjectPaths.Normalize(reference)), "experiment.json: referenced project files must be distinct");
    }

    references.Sort(StringComparer.Ordinal);
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    byte[] buffer = new byte[HashBufferSize];
    foreach (string reference in references)
    {
      hash.AppendData(Encoding.UTF8.GetBytes(ProjectPaths.Normalize(reference) + "\0"));
      using FileStream stream = File.OpenRead(ProjectPaths.Resolve(directory, reference));
      int length;
      while ((length = stream.Read(buffer)) > 0)
      {
        hash.AppendData(buffer.AsSpan(0, length));
      }

      hash.AppendData(new byte[] { 0 });
    }

    return Convert.ToHexStringLower(hash.GetHashAndReset());
  }

  private static void ValidateTerminals(ProjectTerminal[] terminals, ProjectEdge[] edges, ProjectDemand demand, string file)
  {
    Dictionary<string, ProjectLane> ordinaryLanes = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      if (!edge.Ordinary)
      {
        continue;
      }

      foreach (ProjectLane lane in edge.Lanes)
      {
        ordinaryLanes.Add(lane.Map.Id, lane);
      }
    }

    HashSet<string> ids = new(StringComparer.Ordinal);
    HashSet<string> laneRoles = new(StringComparer.Ordinal);
    bool hasEntry = false;
    bool hasExit = false;
    foreach (ProjectTerminal terminal in terminals)
    {
      Require(terminal is not null, $"{file}: terminal cannot be null");
      Require(ValidId(terminal!.Id) && ids.Add(terminal.Id), $"{file}: terminal id must be valid and unique");
      bool isEntry = terminal.Role == "entry";
      bool isExit = terminal.Role == "exit";
      Require(isEntry || isExit, $"{file}: terminal '{terminal.Id}' role must be entry or exit");
      Require(!string.IsNullOrWhiteSpace(terminal.LaneId), $"{file}: terminal '{terminal.Id}' lane_id is required");
      Require(ordinaryLanes.TryGetValue(terminal.LaneId, out ProjectLane? lane), $"{file}: terminal '{terminal.Id}' must reference an ordinary lane");
      Require(laneRoles.Add(terminal.LaneId + "\0" + terminal.Role), $"{file}: duplicate terminal lane and role");
      Require(IsTerminalLaneUsable(lane!, demand), $"{file}: terminal '{terminal.Id}' lane must permit a configured vehicle class with positive weight");
      bool validPosition = double.IsFinite(terminal.PositionM) && terminal.PositionM >= 0 && terminal.PositionM <= lane!.Map.Length;
      Require(validPosition, $"{file}: terminal '{terminal.Id}' position_m must be finite and within its lane");
      hasEntry |= isEntry;
      hasExit |= isExit;
    }

    Require(terminals.Length == 0 || (hasEntry && hasExit), $"{file}: terminals must include both entry and exit roles");
  }

  private static bool IsTerminalLaneUsable(ProjectLane lane, ProjectDemand demand)
  {
    foreach (ProjectVehicleType type in demand.VehicleTypes)
    {
      if (type.Weight > 0 && lane.Permits(type.VehicleClass))
      {
        return true;
      }
    }

    return false;
  }

  private static bool Permits(ProjectEdge edge, string vehicleClass)
  {
    if (!edge.Ordinary)
    {
      return false;
    }

    foreach (ProjectLane lane in edge.Lanes)
    {
      if (lane.Permits(vehicleClass))
      {
        return true;
      }
    }

    return false;
  }

  private static bool HasUsableEdge(ProjectEdge[] edges, string vehicleClass)
  {
    foreach (ProjectEdge edge in edges)
    {
      if (Permits(edge, vehicleClass))
      {
        return true;
      }
    }

    return false;
  }

  private static void ValidatePermissions(string[]? permissions, string file, string laneId)
  {
    if (permissions is null)
    {
      return;
    }

    foreach (string permission in permissions)
    {
      Require(permission == "all" || permission == "pedestrian" || VehicleClasses.Contains(permission), $"{file}: lane '{laneId}' has an unknown vehicle permission");
    }
  }

  private static void ValidateSettings(ProjectDemand demand, string file)
  {
    Require(demand.FormatVersion == 1, $"{file}: unsupported format_version; expected 1");
    Require(demand.Profiles is not null && demand.Generation is not null && demand.Routing is not null && demand.VehicleTypes is not null, $"{file}: profiles, generation, routing and vehicle_types are required and cannot be null");
    TrafficGenerationSettings generation = demand.Generation!;
    Require(generation.ArrivalModel == "poisson", $"{file}: use arrival_model 'poisson'");
    Require(generation.EndpointSelection == "boundary_and_internal" || generation.EndpointSelection == "terminals", $"{file}: endpoint_selection must be 'boundary_and_internal' or 'terminals'");
    Require(double.IsFinite(generation.MeanDeparturesPerHour) && generation.MeanDeparturesPerHour >= 0, $"{file}: mean_departures_per_hour must be finite and nonnegative");
    bool distancesAreFinite = double.IsFinite(generation.MinTripDistanceM) && double.IsFinite(generation.MaxTripDistanceM);
    bool distancesAreOrdered = generation.MinTripDistanceM > 0 && generation.MaxTripDistanceM >= generation.MinTripDistanceM;
    Require(distancesAreFinite && distancesAreOrdered, $"{file}: trip distances must be finite, positive and ordered");
    Require(generation.CandidateLimit > 0, $"{file}: candidate_limit must be positive");
    TrafficRoutingSettings routing = demand.Routing!;
    bool alternativeCountIsValid = routing.MaxAlternatives > 0 && routing.MaxAlternatives <= generation.CandidateLimit;
    bool detourRatioIsValid = double.IsFinite(routing.MaxDetourRatio) && routing.MaxDetourRatio >= 1;
    Require(routing.Cost == "free_flow_travel_time" && alternativeCountIsValid && detourRatioIsValid, $"{file}: use free_flow_travel_time, 1..candidate_limit alternatives and a finite detour ratio >= 1");
    Require(demand.VehicleTypes!.Length > 0, $"{file}: vehicle_types must not be empty");
    HashSet<string> typeIds = new(StringComparer.Ordinal);
    double totalWeight = 0;
    foreach (ProjectVehicleType type in demand.VehicleTypes)
    {
      Require(type is not null, $"{file}: null vehicle type");
      Require(ValidId(type!.Id) && typeIds.Add(type.Id), $"{file}: invalid or duplicate vehicle type '{type.Id}'");
      Require(type.VehicleClass is not null && VehicleClasses.Contains(type.VehicleClass), $"{file}: unsupported vehicle_class '{type.VehicleClass}'");
      Require(double.IsFinite(type.Weight) && type.Weight >= 0, $"{file}: vehicle type '{type.Id}' weight must be finite and nonnegative");
      totalWeight += type.Weight;
    }

    Require(double.IsFinite(totalWeight) && totalWeight > 0, $"{file}: vehicle type weights need a finite positive total");
  }

  private static void ValidateProfiles(ProjectDemand demand, ProjectEdge[] edges, string file)
  {
    HashSet<string> usable = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      foreach (ProjectVehicleType type in demand.VehicleTypes)
      {
        if (Permits(edge, type.VehicleClass))
        {
          usable.Add(edge.Id);
          break;
        }
      }
    }

    Require(usable.Count > 0, $"{file}: network has no ordinary edges usable by configured vehicle classes");
    foreach (ProjectVehicleType type in demand.VehicleTypes)
    {
      if (type.Weight > 0)
      {
        Require(HasUsableEdge(edges, type.VehicleClass), $"{file}: vehicle type '{type.Id}' has no usable ordinary road edge");
      }
    }

    HashSet<string> found = new(StringComparer.Ordinal);
    double dailyMean = 0;
    foreach (EdgeTrafficProfile profile in demand.Profiles)
    {
      Require(profile is not null, $"{file}: null edge profile");
      Require(profile!.Edge is not null && usable.Contains(profile.Edge), $"{file}: unknown or unusable ordinary edge '{profile.Edge}'");
      Require(found.Add(profile.Edge!), $"{file}: duplicate profile for edge '{profile.Edge}'");
      Require(profile.Intervals is not null, $"{file}: edge '{profile.Edge}' requires daily intervals");
      Require(profile.Intervals!.Length > 0, $"{file}: edge '{profile.Edge}' requires daily intervals");
      dailyMean = ValidateIntervals(profile, file, dailyMean);
    }

    string? missing = null;
    foreach (string edge in usable)
    {
      if (!found.Contains(edge) && (missing is null || StringComparer.Ordinal.Compare(edge, missing) < 0))
      {
        missing = edge;
      }
    }

    Require(missing is null, $"{file}: missing traffic profile for edge '{missing}'");
    Require(double.IsFinite(dailyMean), $"{file}: summed daily traffic weight exceeds finite numeric range");
    Require(demand.Generation.MeanDeparturesPerHour == 0 || dailyMean > 0, $"{file}: positive mean_departures_per_hour is invalid with all-zero daily weights");
  }

  private static double ValidateIntervals(EdgeTrafficProfile profile, string file, double totalWeight)
  {
    double end = 0;
    foreach (TrafficInterval interval in profile.Intervals!)
    {
      Require(interval is not null, $"{file}: edge '{profile.Edge}' has a null interval");
      bool boundsAreFinite = double.IsFinite(interval!.StartSeconds) && double.IsFinite(interval.EndSeconds);
      bool boundsAreContiguous = interval.StartSeconds == end && interval.EndSeconds > end;
      bool endIsWithinDay = interval.EndSeconds <= SecondsPerDay;
      Require(boundsAreFinite && boundsAreContiguous && endIsWithinDay, $"{file}: edge '{profile.Edge}' intervals must be contiguous, ordered and within 0–86400 seconds (expected start {end})");
      Require(double.IsFinite(interval.Weight) && interval.Weight >= 0, $"{file}: edge '{profile.Edge}' weight must be finite and nonnegative");
      totalWeight += interval.Weight * ((interval.EndSeconds - interval.StartSeconds) / SecondsPerDay);
      end = interval.EndSeconds;
    }

    Require(end == SecondsPerDay, $"{file}: edge '{profile.Edge}' intervals must cover the full day through 86400 seconds");
    return totalWeight;
  }

  private static ProjectEdge[] ReadEdges(XElement net, string file)
  {
    HashSet<string> edgeIds = new(StringComparer.Ordinal), laneIds = new(StringComparer.Ordinal);
    List<ProjectEdge> edges = [];
    foreach (XElement edge in net.Elements("edge"))
    {
      string id = Required(edge, "id", file);
      Require(edgeIds.Add(id), $"{file}: duplicate edge '{id}'");
      string function = (string?)edge.Attribute("function") ?? "normal";
      Require(function is "normal" or "internal" or "connector" or "crossing" or "walkingarea", $"{file}: edge '{id}' has unknown function '{function}'");
      bool ordinary = function == "normal" && !id.StartsWith(':');
      List<ProjectLane> lanes = [];
      HashSet<int> indices = [];
      foreach (XElement lane in edge.Elements("lane"))
      {
        string laneId = Required(lane, "id", file);
        Require(laneIds.Add(laneId), $"{file}: duplicate lane '{laneId}'");
        int index = Integer(lane, "index", file);
        Require(index >= 0 && indices.Add(index), $"{file}: edge '{id}' has invalid/duplicate lane index");
        double length = Number(lane, "length", file), speed = Number(lane, "speed", file);
        Require(length > 0 && speed > 0, $"{file}: lane '{laneId}' length and speed must be positive");
        Point[] shape = Shape(lane, file);
        Require(shape.Length >= 2, $"{file}: lane '{laneId}' needs a readable polyline shape");
        string[]? allow = lane.Attribute("allow") is null ? null : Tokens((string)lane.Attribute("allow")!);
        string[] disallow = Tokens((string?)lane.Attribute("disallow") ?? "");
        Require(lane.Attribute("allow") is null || lane.Attribute("disallow") is null, $"{file}: lane '{laneId}' must use allow or disallow, not both");
        ValidatePermissions(allow, file, laneId);
        ValidatePermissions(disallow, file, laneId);
        lanes.Add(new(new(laneId, id, length, speed, shape), index, allow, disallow));
      }

      Require(lanes.Count > 0, $"{file}: edge '{id}' has no lanes");
      for (int index = 0; index < lanes.Count; index++)
      {
        Require(indices.Contains(index), $"{file}: edge '{id}' lane indices must start at zero and be contiguous");
      }

      lanes.Sort((left, right) => left.Index.CompareTo(right.Index));
      string? from = ordinary ? Required(edge, "from", file) : null;
      string? to = ordinary ? Required(edge, "to", file) : null;
      edges.Add(new ProjectEdge(id, (string?)edge.Attribute("name"), ordinary, from, to, lanes.ToArray()));
    }

    Require(edges.Count > 0, $"{file}: network contains no edges");
    return edges.ToArray();
  }

  private static JunctionMap[] ReadJunctions(XElement net, string file)
  {
    HashSet<string> ids = new(StringComparer.Ordinal);
    List<JunctionMap> junctions = [];
    foreach (XElement junction in net.Elements("junction"))
    {
      string id = Required(junction, "id", file);
      Require(ids.Add(id), $"{file}: duplicate junction '{id}'");
      junctions.Add(new(id, Required(junction, "type", file), Number(junction, "x", file), Number(junction, "y", file), Shape(junction, file)));
    }

    return junctions.ToArray();
  }

  private static ProjectConnection[] ReadConnections(XElement net, ProjectEdge[] edges, string file)
  {
    Dictionary<string, ProjectEdge> byId = new(StringComparer.Ordinal);
    HashSet<string> laneIds = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      byId.Add(edge.Id, edge);
      foreach (ProjectLane lane in edge.Lanes)
      {
        laneIds.Add(lane.Map.Id);
      }
    }

    List<ProjectConnection> result = [];
    foreach (XElement connection in net.Elements("connection"))
    {
      string from = Required(connection, "from", file), to = Required(connection, "to", file);
      int fromLane = Integer(connection, "fromLane", file), toLane = Integer(connection, "toLane", file);
      string? via = (string?)connection.Attribute("via");
      bool lanesAreValid = HasLane(byId, from, fromLane) && HasLane(byId, to, toLane);
      Require(lanesAreValid, $"{file}: invalid connection '{from}' lane {fromLane} -> '{to}' lane {toLane}");
      Require(via is null || laneIds.Contains(via), $"{file}: connection '{from}' -> '{to}' has unknown via lane '{via}'");
      result.Add(new(from, to, fromLane, toLane, via));
    }

    return result.ToArray();
  }

  private static bool HasLane(Dictionary<string, ProjectEdge> edges, string id, int index)
  {
    if (!edges.TryGetValue(id, out ProjectEdge? edge))
    {
      return false;
    }

    return index >= 0 && index < edge.Lanes.Length;
  }

  private static void ValidateSignals(XElement xml, string file, HashSet<string> signals)
  {
    HashSet<string> programs = new(StringComparer.Ordinal);
    foreach (XElement logic in xml.Elements("tlLogic"))
    {
      string id = Required(logic, "id", file);
      string program = Required(logic, "programID", file);
      Require(programs.Add(id + "\0" + program), $"{file}: duplicate signal program '{id}/{program}'");
      Require(logic.Element("phase") is not null, $"{file}: signal '{id}' requires phases");
      foreach (XElement phase in logic.Elements("phase"))
      {
        Require(Number(phase, "duration", file) >= 0, $"{file}: signal '{id}' has negative phase duration");
        _ = Required(phase, "state", file);
      }

      signals.Add(id);
    }
  }

  private static T ReadJson<T>(string directory, string file)
  {
    string path = ProjectPaths.Resolve(directory, file);
    try
    {
      using FileStream stream = File.OpenRead(path);
      return JsonSerializer.Deserialize<T>(stream, StrictJson) ?? throw ProjectPaths.Invalid($"{file}: empty JSON document");
    }
    catch (JsonException ex)
    {
      throw ProjectPaths.Invalid($"{file}: invalid JSON at {ex.Path ?? "$"}: {ex.Message}");
    }
  }

  private static XElement ReadXml(string directory, string file, string root)
  {
    string path = ProjectPaths.Resolve(directory, file);
    try
    {
      using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
      XElement? element = XDocument.Load(reader).Root;
      Require(element?.Name == root, $"{file}: expected XML root <{root}>");
      return element!;
    }
    catch (XmlException ex)
    {
      throw ProjectPaths.Invalid($"{file}: unreadable XML (line {ex.LineNumber}): {ex.Message}");
    }
  }

  private static string Required(XElement element, string attribute, string file)
  {
    string? value = (string?)element.Attribute(attribute);
    Require(!string.IsNullOrWhiteSpace(value), $"{file}: <{element.Name}> requires '{attribute}'");
    return value!;
  }

  private static double Number(XElement element, string attribute, string file)
  {
    string text = Required(element, attribute, file);
    Require(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value), $"{file}: <{element.Name}> '{attribute}' must be finite numeric data");
    return value;
  }

  private static int Integer(XElement element, string attribute, string file)
  {
    Require(int.TryParse(Required(element, attribute, file), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value), $"{file}: <{element.Name}> '{attribute}' must be an integer");
    return value;
  }

  private static Point[] Shape(XElement element, string file)
  {
    List<Point> shape = [];
    foreach (string token in Tokens((string?)element.Attribute("shape") ?? ""))
    {
      shape.Add(ParsePoint(token, element, file));
    }

    return shape.ToArray();
  }

  private static Point ParsePoint(string token, XElement element, string file)
  {
    string[] coordinates = token.Split(',');
    string shapeError = $"{file}: invalid shape on '{element.Attribute("id")}'";
    Require(coordinates.Length == 2 || coordinates.Length == 3, shapeError);
    Require(TryCoordinate(coordinates[0], out double x), shapeError);
    Require(TryCoordinate(coordinates[1], out double y), shapeError);
    if (coordinates.Length == 3)
    {
      Require(TryCoordinate(coordinates[2], out _), $"{file}: invalid elevation in shape on '{element.Attribute("id")}'");
    }

    return new Point(x, y);
  }

  private static bool TryCoordinate(string text, out double value)
  {
    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
  }

  private static string[] Tokens(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
  private static bool ValidId(string? id)
  {
    if (string.IsNullOrWhiteSpace(id))
    {
      return false;
    }

    foreach (char character in id)
    {
      if (char.IsWhiteSpace(character) || char.IsControl(character))
      {
        return false;
      }
    }

    return true;
  }

  private static void Require(bool condition, string message)
  {
    if (!condition)
    {
      throw ProjectPaths.Invalid(message);
    }
  }
}
