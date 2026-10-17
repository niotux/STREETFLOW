using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private static ProjectTrafficLights? ReadTrafficLights(string directory, ProjectManifest manifest, ProjectEdge[] edges, ProjectConnection[] connections)
  {
    if (manifest.TrafficLightsFile is null)
    {
      return null;
    }

    string file = manifest.TrafficLightsFile;
    ProjectTrafficLights lights = ReadJson<ProjectTrafficLights>(directory, file);
    Require(lights.FormatVersion == 1, $"{file}: format_version must be 1");
    Require(ValidId(lights.WorldId) && lights.WorldId.Length <= MaximumProjectIdLength, $"{file}: world_id must be valid");
    Require(lights.Devices is not null, $"{file}: devices cannot be null");
    Dictionary<string, ProjectLane> lanes = new(StringComparer.Ordinal);
    Dictionary<string, ProjectEdge> edgeLookup = new(StringComparer.Ordinal);
    foreach (ProjectEdge edge in edges)
    {
      edgeLookup.Add(edge.Id, edge);
      if (!edge.Ordinary)
      {
        continue;
      }

      foreach (ProjectLane lane in edge.Lanes)
      {
        lanes.Add(lane.Map.Id, lane);
      }
    }

    Dictionary<string, HashSet<string>> movements = ReadControlledLanes(edgeLookup, connections);
    HashSet<string> devices = new(StringComparer.Ordinal);
    HashSet<string> assigned = new(StringComparer.Ordinal);
    foreach (ProjectTrafficDevice device in lights.Devices)
    {
      Require(device is not null, $"{file}: device cannot be null");
      Require(ValidId(device.Id) && device.Id.Length <= MaximumProjectIdLength && devices.Add(device.Id), $"{file}: device id '{device.Id}' must be valid and unique");
      Require(!string.IsNullOrWhiteSpace(device.Label), $"{file}: device '{device.Id}' requires label");
      Require(double.IsFinite(device.X) && double.IsFinite(device.Y), $"{file}: device '{device.Id}' coordinates must be finite");
      Require(ValidId(device.SignalId), $"{file}: device '{device.Id}' requires signal_id");
      Require(device.LaneIds is not null, $"{file}: device '{device.Id}' requires lane_ids");
      Require(device.LaneIds.Length > 0, $"{file}: device '{device.Id}' requires nonempty lane_ids");
      Require(device.LinkIndices is not null, $"{file}: device '{device.Id}' requires link_indices");
      Require(device.LinkIndices.Length > 0, $"{file}: device '{device.Id}' requires nonempty link_indices");
      HashSet<string> covered = new(StringComparer.Ordinal);
      HashSet<int> links = [];
      foreach (int link in device.LinkIndices)
      {
        Require(link >= 0 && links.Add(link), $"{file}: device '{device.Id}' has invalid or duplicate link index");
        Require(assigned.Add(device.SignalId + "\0" + link), $"{file}: SUMO movement is assigned to more than one physical device");
        string key = device.SignalId + "\0" + link;
        Require(movements.TryGetValue(key, out HashSet<string>? incoming), $"{file}: device '{device.Id}' references unknown signal/link '{device.SignalId}:{link}'");
        foreach (string lane in incoming)
        {
          covered.Add(lane);
        }
      }

      HashSet<string> declared = new(StringComparer.Ordinal);
      foreach (string lane in device.LaneIds)
      {
        Require(lanes.ContainsKey(lane) && declared.Add(lane), $"{file}: device '{device.Id}' references unknown or duplicate lane '{lane}'");
      }

      Require(declared.SetEquals(covered), $"{file}: device '{device.Id}' lane_ids must match all incoming lanes of its mapped movements");
    }

    ValidateDevicePrograms(directory, manifest, lights);
    return lights;
  }

  private static Dictionary<string, HashSet<string>> ReadControlledLanes(Dictionary<string, ProjectEdge> edges, ProjectConnection[] connections)
  {
    Dictionary<string, HashSet<string>> movements = new(StringComparer.Ordinal);
    foreach (ProjectConnection connection in connections)
    {
      if (connection.SignalId is null || connection.LinkIndex is null)
      {
        continue;
      }

      string key = connection.SignalId + "\0" + connection.LinkIndex.Value;
      if (!movements.TryGetValue(key, out HashSet<string>? lanes))
      {
        lanes = new HashSet<string>(StringComparer.Ordinal);
        movements.Add(key, lanes);
      }

      lanes.Add(edges[connection.From].Lanes[connection.FromLane].Map.Id);
    }

    return movements;
  }

  private static void ValidateDevicePrograms(string directory, ProjectManifest manifest, ProjectTrafficLights lights)
  {
    List<string> files = [manifest.Network.File];
    files.AddRange(manifest.AdditionalFiles);
    foreach (string file in files)
    {
      XElement xml = ReadXml(directory, file, file == manifest.Network.File ? "net" : "additional");
      foreach (XElement logic in xml.Elements("tlLogic"))
      {
        string signal = Required(logic, "id", file);
        foreach (ProjectTrafficDevice device in lights.Devices)
        {
          if (device.SignalId != signal)
          {
            continue;
          }

          foreach (XElement phase in logic.Elements("phase"))
          {
            int length = Required(phase, "state", file).Length;
            foreach (int link in device.LinkIndices)
            {
              Require(link < length, $"{manifest.TrafficLightsFile}: device '{device.Id}' references a movement beyond the signal program's state length");
            }
          }
        }
      }
    }
  }

  private static string CalculateWorldRevision(string directory, ProjectManifest manifest, bool hasDetectors)
  {
    List<string> inputs = [manifest.Network.File];
    inputs.AddRange(manifest.AdditionalFiles);
    if (manifest.TrafficLightsFile is not null)
    {
      inputs.Add(manifest.TrafficLightsFile);
    }
    // An empty detectors file describes the same world as no detectors file.
    if (hasDetectors)
    {
      inputs.Add(manifest.DetectorsFile!);
    }

    string content = HashProjectFiles(directory, inputs) + "\0" + (manifest.Network.Attribution ?? "");
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
  }
}
