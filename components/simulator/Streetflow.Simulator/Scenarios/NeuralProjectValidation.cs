using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Streetflow.Simulator.Generation;

namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private const int MaximumNeuralHiddenSize = 256;
  private const int MaximumNeuralTurns = 128;
  private const int MaximumNeuralRoads = 100000;
  private const int MaximumNeuralPeriods = 1440;
  private const int MaximumNeuralMovements = 10000;
  private const long MaximumNeuralModelBytes = 64L * 1024 * 1024;

  private static void ValidateNeuralSettings(NeuralModel model, string file)
  {
    Require(model.FormatVersion == 3 && model.Architecture == "local_turn_gru_v1", $"{file}: expected format_version 3 and architecture local_turn_gru_v1");
    Require(!string.IsNullOrWhiteSpace(model.ModelFile), $"{file}: model_file is required");
    Require(IsSha256(model.NetworkSha256) && IsSha256(model.DatasetSha256), $"{file}: network_sha256 and dataset_sha256 must be lowercase SHA-256 digests");
    Require(model.VehicleType is not null, $"{file}: vehicle_type is required");
    Require(ValidId(model.VehicleType.Id) && VehicleClasses.Contains(model.VehicleType.VehicleClass) && model.VehicleType.Weight == 1, $"{file}: vehicle_type must name a supported vehicle class with weight 1");
    Require(model.HiddenSize > 0 && model.HiddenSize <= MaximumNeuralHiddenSize, $"{file}: hidden_size must be 1..{MaximumNeuralHiddenSize}");
    Require(model.MaxTurns > 0 && model.MaxTurns <= MaximumNeuralTurns, $"{file}: max_turns must be 1..{MaximumNeuralTurns}");
    Require(model.MaximumRouteMovements > 0 && model.MaximumRouteMovements <= MaximumNeuralMovements, $"{file}: maximum_route_movements must be 1..{MaximumNeuralMovements}");
    Require(model.Roads is not null && model.Periods is not null, $"{file}: roads and periods are required");
    Require(model.Roads.Length > 0 && model.Roads.Length <= MaximumNeuralRoads, $"{file}: unsupported road count");
    Require(model.Periods.Length > 0 && model.Periods.Length <= MaximumNeuralPeriods, $"{file}: unsupported period count");
    HashSet<string> roads = new(StringComparer.Ordinal);
    bool hasExit = false;
    foreach (NeuralRoad road in model.Roads)
    {
      Require(road is not null, $"{file}: null road");
      Require(ValidId(road.EdgeId) && roads.Add(road.EdgeId), $"{file}: invalid or duplicate road ID");
      Require(road.NextEdgeIds is not null, $"{file}: next_edge_ids is required");
      Require(road.NextEdgeIds.Length <= model.MaxTurns, $"{file}: road '{road.EdgeId}' exceeds max_turns");
      hasExit = hasExit || road.CanExit;
    }

    Require(hasExit, $"{file}: at least one road must allow exit");
    int end = 0;
    foreach (NeuralPeriod period in model.Periods)
    {
      Require(period is not null, $"{file}: null period");
      Require(period.StartSeconds == end && period.EndSeconds > end && period.EndSeconds <= SecondsPerDay, $"{file}: periods must be contiguous and cover the day");
      Require(period.Arrivals is not null, $"{file}: period arrivals cannot be null");
      HashSet<string> origins = new(StringComparer.Ordinal);
      double total = 0;
      foreach (NeuralArrival arrival in period.Arrivals)
      {
        Require(arrival is not null, $"{file}: null arrival");
        Require(arrival.StartEdge is not null && roads.Contains(arrival.StartEdge) && origins.Add(arrival.StartEdge), $"{file}: unknown or duplicate arrival start_edge");
        Require(double.IsFinite(arrival.VehiclesPerHour) && arrival.VehiclesPerHour > 0, $"{file}: vehicles_per_hour must be finite and positive; omit inactive origins");
        total += arrival.VehiclesPerHour;
      }

      Require(double.IsFinite(total), $"{file}: total arrival rate exceeds numeric range");
      end = period.EndSeconds;
    }

    Require(end == SecondsPerDay, $"{file}: periods must cover 0..86400");
  }

  private static void ValidateNeuralNetwork(string directory, ProjectManifest manifest, NeuralModel model, ProjectEdge[] edges, ProjectConnection[] connections)
  {
    string file = manifest.NeuralModelFile!;
    using (FileStream network = File.OpenRead(ProjectPaths.Resolve(directory, manifest.Network.File)))
    {
      string hash = Convert.ToHexStringLower(SHA256.HashData(network));
      Require(hash == model.NetworkSha256, $"{file}: neural model was trained against a different network revision");
    }

    NeuralRouteGraph graph = new(edges, connections, model.VehicleType.VehicleClass);
    Require(graph.EdgeIds.Length == model.Roads.Length, $"{file}: road vocabulary must cover every usable ordinary road");
    int maxTurns = 1;
    for (int index = 0; index < model.Roads.Length; index++)
    {
      NeuralRoad road = model.Roads[index];
      Require(road.EdgeId == graph.EdgeIds[index], $"{file}: road vocabulary differs from the map's ordered road IDs");
      string[] next = graph.Next(road.EdgeId);
      Require(next.Length == road.NextEdgeIds.Length, $"{file}: turn vocabulary differs for road '{road.EdgeId}'");
      for (int turn = 0; turn < next.Length; turn++)
      {
        Require(next[turn] == road.NextEdgeIds[turn], $"{file}: turn index {turn} differs for road '{road.EdgeId}'");
      }

      maxTurns = Math.Max(maxTurns, next.Length);
    }

    Require(model.MaxTurns == maxTurns, $"{file}: max_turns differs from the map");
    ValidateNeuralOrigins(model, file);
    string modelPath = ProjectPaths.Resolve(directory, model.ModelFile);
    Require(new FileInfo(modelPath).Length <= MaximumNeuralModelBytes, $"{file}: ONNX model exceeds {MaximumNeuralModelBytes} bytes");
    try
    {
      using NeuralRouteNetwork network = new(modelPath, model);
    }
    catch (OnnxRuntimeException exception)
    {
      throw ProjectPaths.Invalid($"{file}: invalid ONNX model: {exception.Message}");
    }
  }

  private static void ValidateNeuralOrigins(NeuralModel model, string file)
  {
    Dictionary<string, List<string>> previous = new(StringComparer.Ordinal);
    HashSet<string> reachable = new(StringComparer.Ordinal);
    Queue<string> pending = new();
    foreach (NeuralRoad road in model.Roads)
    {
      previous.Add(road.EdgeId, []);
      if (road.CanExit && reachable.Add(road.EdgeId))
      {
        pending.Enqueue(road.EdgeId);
      }
    }

    foreach (NeuralRoad road in model.Roads)
    {
      foreach (string next in road.NextEdgeIds)
      {
        previous[next].Add(road.EdgeId);
      }
    }

    while (pending.TryDequeue(out string? edge))
    {
      foreach (string from in previous[edge])
      {
        if (reachable.Add(from))
        {
          pending.Enqueue(from);
        }
      }
    }

    foreach (NeuralPeriod period in model.Periods)
    {
      foreach (NeuralArrival arrival in period.Arrivals)
      {
        Require(reachable.Contains(arrival.StartEdge), $"{file}: origin '{arrival.StartEdge}' cannot reach any allowed exit");
      }
    }
  }

  private static bool IsSha256(string? value)
  {
    if (value is null)
    {
      return false;
    }

    if (value.Length != 64)
    {
      return false;
    }

    foreach (char character in value)
    {
      if (!char.IsAsciiHexDigitLower(character))
      {
        return false;
      }
    }

    return true;
  }
}
