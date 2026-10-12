using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public static class ScenarioPreparation
{
  private const int TinyVehicleCount = 32;
  private const int ImportedVehicleCount = 96;
  private const int MaximumRouteAttempts = 2000;
  private const int MaximumRouteSteps = 12;
  private const int MinimumRouteEdges = 3;
  private const int MaximumCameraJunctions = 12;
  private const double MinimumCameraLaneLength = 30;
  private const int FixtureSeed = 42;
  public static async Task PrepareAsync(string workspace, SimulatorOptions options, CancellationToken token)
  {
    Directory.CreateDirectory(options.ScenarioRoot);
    await PrepareTinyAsync(options, token);
    string iasi = Path.Combine(options.ScenarioRoot, "iasi-central");
    Directory.CreateDirectory(iasi);
    string source = Path.Combine(workspace, "legacy", "simulator", "examples", "iasi", "source.osm.gz");
    string osm = Path.Combine(iasi, "source.osm");
    await using (GZipStream input = new GZipStream(File.OpenRead(source), CompressionMode.Decompress))
    {
      await using (FileStream output = File.Create(osm))
      {
        await input.CopyToAsync(output, token);
      }
    }

    await ImportOsmAsync(osm, iasi, "iasi-central", "Central Iași with synthetic demand", options, token);
    Console.WriteLine($"Prepared tiny-intersection and iasi-central under {options.ScenarioRoot}");
  }

  private static async Task PrepareTinyAsync(SimulatorOptions options, CancellationToken token)
  {
    string tiny = Path.Combine(options.ScenarioRoot, "tiny-intersection");
    Directory.CreateDirectory(tiny);
    XElement nodes = new("nodes");
    nodes.Add(new XElement("node", new XAttribute("id", "center"), new XAttribute("x", 0), new XAttribute("y", 0), new XAttribute("type", "traffic_light")));
    nodes.Add(CreateNode("west", -200, 0));
    nodes.Add(CreateNode("east", 200, 0));
    nodes.Add(CreateNode("north", 0, 200));
    nodes.Add(CreateNode("south", 0, -200));
    nodes.Save(Path.Combine(tiny, "nodes.nod.xml"));
    XElement edges = new XElement("edges");
    string[] directions = ["west", "east", "north", "south"];
    foreach (string direction in directions)
    {
      edges.Add(CreateEdge(direction + "_in", direction, "center"));
      edges.Add(CreateEdge(direction + "_out", "center", direction));
    }

    edges.Save(Path.Combine(tiny, "edges.edg.xml"));
    string[] conversionArguments = ["--node-files", Path.Combine(tiny, "nodes.nod.xml"), "--edge-files", Path.Combine(tiny, "edges.edg.xml"), "--output-file", Path.Combine(tiny, "network.net.xml"), "--no-turnarounds", "true"];
    await ToolAsync(options.NetconvertBinary, conversionArguments, Path.Combine(tiny, "conversion.log"), token);
    XElement routes = new XElement("routes", VehicleType());
    string[] movements = ["west_in east_out", "east_in west_out", "north_in south_out", "south_in north_out"];
    for (int index = 0; index < TinyVehicleCount; index++)
    {
      routes.Add(Vehicle($"car-{index:D3}", index * 2, movements[index % movements.Length]));
    }

    routes.Save(Path.Combine(tiny, "routes.rou.xml"));
    CameraLane[] tinyCameraLanes = new CameraLane[directions.Length];
    for (int index = 0; index < directions.Length; index++)
    {
      string direction = directions[index];
      tinyCameraLanes[index] = new CameraLane(direction, direction + "_in_0", 100, 180, 150);
    }

    Camera[] cameras = [new Camera("center-camera", "center", tinyCameraLanes)];
    ScenarioManifest manifest = new("tiny-intersection", "1", "Synthetic four-way signalized intersection", "tiny-1", "tiny-cameras-1", "synthetic", "network.net.xml", "routes.rou.xml", "Synthetic fixture", 180);
    await PackageAsync(tiny, manifest, cameras, token);
    Checkpoint[] checkpoints = [new("west-approach", "west_in_0", 150), new("east-exit", "east_out_0", 50)];
    CheckpointPair[] pairs = [new("west-to-east", "west-approach", "east-exit")];
    CheckpointConfiguration checkpointConfiguration = new(checkpoints, pairs);
    string checkpointJson = JsonSerializer.Serialize(checkpointConfiguration, JsonDefaults.Options);
    await File.WriteAllTextAsync(Path.Combine(tiny, "checkpoints.json"), checkpointJson, token);
  }

  public static async Task ImportOsmAsync(string osm, string output, string id, string name, SimulatorOptions options, CancellationToken token)
  {
    Directory.CreateDirectory(output);
    string[] conversionArguments = ["--osm-files", Path.GetFullPath(osm), "--output-file", Path.Combine(output, "network.net.xml"), "--keep-edges.by-vclass", "passenger", "--geometry.remove", "true", "--tls.guess", "true", "--no-turnarounds", "true"];
    await ToolAsync(options.NetconvertBinary, conversionArguments, Path.Combine(output, "conversion.log"), token);
    XElement net = XDocument.Load(Path.Combine(output, "network.net.xml")).Root!;
    Dictionary<string, string[]> adjacency = ReadAdjacency(net);
    WriteImportedRoutes(output, adjacency);
    Camera[] cameras = ReadImportedCameras(net);
    string attribution = "© OpenStreetMap contributors; https://www.openstreetmap.org/copyright. Demand and signal timings are synthetic.";
    ScenarioManifest manifest = new(id, "1", name, id + "-osm-1", id + "-cameras-1", "synthetic", "network.net.xml", "routes.rou.xml", attribution, 300);
    await PackageAsync(output, manifest, cameras, token);
  }

  private static Dictionary<string, string[]> ReadAdjacency(XElement net)
  {
    HashSet<string> roadEdges = new(StringComparer.Ordinal);
    foreach (XElement edge in net.Elements("edge"))
    {
      if (edge.Attribute("function") is null)
      {
        roadEdges.Add((string)edge.Attribute("id")!);
      }
    }

    Dictionary<string, List<string>> adjacencyLists = new(StringComparer.Ordinal);
    foreach (XElement connection in net.Elements("connection"))
    {
      string from = (string)connection.Attribute("from")!;
      string to = (string)connection.Attribute("to")!;
      if (!roadEdges.Contains(from) || !roadEdges.Contains(to))
      {
        continue;
      }

      if (!adjacencyLists.TryGetValue(from, out List<string>? destinations))
      {
        destinations = new List<string>();
        adjacencyLists.Add(from, destinations);
      }

      destinations.Add(to);
    }

    Dictionary<string, string[]> adjacency = new(StringComparer.Ordinal);
    foreach (KeyValuePair<string, List<string>> entry in adjacencyLists)
    {
      HashSet<string> uniqueDestinations = new(entry.Value, StringComparer.Ordinal);
      List<string> orderedDestinations = new(uniqueDestinations);
      orderedDestinations.Sort(StringComparer.Ordinal);
      adjacency.Add(entry.Key, orderedDestinations.ToArray());
    }

    return adjacency;
  }

  private static void WriteImportedRoutes(string output, Dictionary<string, string[]> adjacency)
  {
    Random random = new Random(FixtureSeed);
    List<string> candidatesList = new(adjacency.Keys);
    candidatesList.Sort(StringComparer.Ordinal);
    string[] candidates = candidatesList.ToArray();
    XElement routes = new XElement("routes", VehicleType());
    int produced = 0;
    for (int attempt = 0; attempt < MaximumRouteAttempts && produced < ImportedVehicleCount && candidates.Length > 0; attempt++)
    {
      List<string> route = new()
      {
        candidates[random.Next(candidates.Length)]
      };
      for (int step = 0; step < MaximumRouteSteps; step++)
      {
        List<string> availableEdges = new();
        if (adjacency.TryGetValue(route[^1], out string[]? connectedEdges))
        {
          foreach (string edge in connectedEdges)
          {
            if (!route.Contains(edge))
            {
              availableEdges.Add(edge);
            }
          }
        }

        if (availableEdges.Count == 0)
        {
          break;
        }

        route.Add(availableEdges[random.Next(availableEdges.Count)]);
      }

      if (route.Count < MinimumRouteEdges)
      {
        continue;
      }

      routes.Add(Vehicle($"iasi-car-{produced:D3}", produced * 1.5, string.Join(' ', route)));
      produced++;
    }

    if (produced == 0)
    {
      throw new InvalidDataException("Imported network has no connected routes for this synthetic fixture");
    }

    routes.Save(Path.Combine(output, "routes.rou.xml"));
  }

  private static Camera[] ReadImportedCameras(XElement net)
  {
    List<Camera> cameraList = new();
    int trafficLightCount = 0;
    foreach (XElement junction in net.Elements("junction"))
    {
      string? junctionType = (string?)junction.Attribute("type");
      if (junctionType is null)
      {
        continue;
      }

      if (!junctionType.StartsWith("traffic_light", StringComparison.Ordinal))
      {
        continue;
      }

      if (trafficLightCount >= MaximumCameraJunctions)
      {
        break;
      }

      trafficLightCount++;
      HashSet<string> incoming = new(StringComparer.Ordinal);
      string incomingText = (string?)junction.Attribute("incLanes") ?? "";
      foreach (string laneId in incomingText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        incoming.Add(laneId);
      }

      List<CameraLane> regionList = new();
      foreach (XElement edge in net.Elements("edge"))
      {
        foreach (XElement laneElement in edge.Elements("lane"))
        {
          string laneId = (string)laneElement.Attribute("id")!;
          if (!incoming.Contains(laneId))
          {
            continue;
          }

          double length = double.Parse((string)laneElement.Attribute("length")!, CultureInfo.InvariantCulture);
          if (length >= MinimumCameraLaneLength)
          {
            regionList.Add(new CameraLane(laneId, laneId, Math.Max(0, length - 80), length, length - 20));
          }
        }
      }

      if (regionList.Count == 0)
      {
        continue;
      }

      string junctionId = (string)junction.Attribute("id")!;
      cameraList.Add(new Camera("camera-" + junctionId, junctionId, regionList.ToArray()));
    }

    Camera[] cameras = cameraList.ToArray();
    return cameras;
  }

  private static XElement CreateNode(string id, double x, double y)
  {
    return new XElement("node", new XAttribute("id", id), new XAttribute("x", x), new XAttribute("y", y));
  }

  private static XElement CreateEdge(string id, string from, string to)
  {
    return new XElement("edge", new XAttribute("id", id), new XAttribute("from", from), new XAttribute("to", to), new XAttribute("numLanes", 1), new XAttribute("speed", 13.9));
  }

  private static XElement VehicleType()
  {
    XElement vehicleType = new("vType", new XAttribute("id", "passenger"), new XAttribute("vClass", "passenger"));
    vehicleType.Add(new XAttribute("accel", 2.6), new XAttribute("decel", 4.5), new XAttribute("sigma", 0.5), new XAttribute("length", 5), new XAttribute("minGap", 2.5));
    return vehicleType;
  }

  private static XElement Vehicle(string id, double departure, string edges)
  {
    XElement vehicle = new("vehicle", new XAttribute("id", id), new XAttribute("type", "passenger"));
    vehicle.Add(new XAttribute("depart", departure.ToString(CultureInfo.InvariantCulture)), new XAttribute("departLane", "best"));
    vehicle.Add(new XElement("route", new XAttribute("edges", edges)));
    return vehicle;
  }

  private static async Task PackageAsync(string directory, ScenarioManifest manifest, Camera[] cameras, CancellationToken token)
  {
    await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, JsonDefaults.Options), token);
    await File.WriteAllTextAsync(Path.Combine(directory, "cameras.json"), JsonSerializer.Serialize(cameras, JsonDefaults.Options), token);
    _ = ScenarioCatalog.Load(Path.Combine(directory, "manifest.json"));
  }

  private static async Task ToolAsync(string binary, string[] args, string logPath, CancellationToken token)
  {
    ProcessStartInfo info = new ProcessStartInfo(binary)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    };
    foreach (var arg in args)
    {
      info.ArgumentList.Add(arg);
    }

    using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {binary}");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
    Task<string> stderr = process.StandardError.ReadToEndAsync(token);
    try
    {
      await process.WaitForExitAsync(token);
    }
    catch
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }

      throw;
    }

    await File.WriteAllTextAsync(logPath, await stdout + "\n" + await stderr, token);
    if (process.ExitCode != 0)
    {
      throw new InvalidOperationException($"{binary} failed ({process.ExitCode}); inspect {logPath}");
    }
  }
}
