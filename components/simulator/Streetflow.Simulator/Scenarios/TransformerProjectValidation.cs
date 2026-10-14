using System.Text.Json;

namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private static void ValidateTransformerManifestFields(string directory)
  {
    JsonElement document = ReadJson<JsonElement>(directory, "experiment.json");
    Require(!document.TryGetProperty("demand_file", out _), "experiment.json: Transformer Routes must omit demand_file");
    Require(document.TryGetProperty("defaults", out JsonElement defaults), "experiment.json: Transformer Routes requires defaults");
    Require(defaults.ValueKind == JsonValueKind.Object, "experiment.json: defaults must be an object");
    Require(defaults.TryGetProperty("start_time_seconds", out _) && defaults.TryGetProperty("step_seconds", out _), "experiment.json: defaults requires start_time_seconds and step_seconds");
  }

  private static void ValidateTransformerSettings(TransformerModel model, string file)
  {
    Require(model.FormatVersion == 2, $"{file}: unsupported format_version; expected 2");
    Require(model.Architecture == "contextual_journey_v1", $"{file}: architecture must be contextual_journey_v1");
    Require(model.ArrivalModel == "nonhomogeneous_poisson", $"{file}: arrival_model must be nonhomogeneous_poisson");
    Require(model.RouteTime == "planned_departure", $"{file}: route_time must be planned_departure");
    Require(!string.IsNullOrWhiteSpace(model.WeightsFile), $"{file}: weights_file is required");
    Require(model.Axes is not null, $"{file}: axes cannot be null");
    TransformerAxes axes = model.Axes!;
    Require(axes.Period is not null && axes.Origin is not null && axes.Destination is not null && axes.Category is not null && axes.VehicleType is not null && axes.Movement is not null, $"{file}: every axis is required and cannot be null");
    Require(axes.Period!.Length > 0 && axes.Origin!.Length > 0 && axes.Destination!.Length > 0 && axes.Category!.Length > 0 && axes.VehicleType!.Length > 0 && axes.Movement!.Length > 0, $"{file}: every axis must be nonempty");
    double end = 0;
    for (int index = 0; index < axes.Period.Length; index++)
    {
      TransformerPeriod period = axes.Period[index];
      Require(period is not null, $"{file}: period[{index}] cannot be null");
      bool finite = double.IsFinite(period!.StartSeconds) && double.IsFinite(period.EndSeconds);
      Require(finite && period.StartSeconds == end && period.EndSeconds > end && period.EndSeconds <= SecondsPerDay, $"{file}: period[{index}] must start at {end} and end later within 86400 seconds");
      end = period.EndSeconds;
    }

    Require(end == SecondsPerDay, $"{file}: period axis must cover the full day through 86400 seconds");
    ValidateTransformerIds(axes.Origin, file, "origin");
    ValidateTransformerIds(axes.Category, file, "category");
    HashSet<string> destinationIds = new(StringComparer.Ordinal);
    foreach (TransformerDestination destination in axes.Destination)
    {
      Require(destination is not null, $"{file}: destination cannot be null");
      Require(ValidTransformerId(destination!.Id) && destinationIds.Add(destination.Id), $"{file}: destination id '{destination.Id}' must be valid and unique");
      Require(destination.TerminalIds is not null, $"{file}: destination '{destination.Id}' requires terminal_ids");
      ValidateTransformerIds(destination.TerminalIds!, file, $"destination '{destination.Id}' terminal_ids");
      ValidateTransformerLabel(destination.Label, file, $"destination '{destination.Id}'");
    }

    HashSet<string> vehicleIds = new(StringComparer.Ordinal);
    foreach (TransformerVehicleType type in axes.VehicleType)
    {
      Require(type is not null, $"{file}: vehicle_type cannot be null");
      Require(ValidTransformerId(type!.Id) && vehicleIds.Add(type.Id), $"{file}: vehicle_type id '{type.Id}' must be valid and unique");
      Require(type.VehicleClass is not null && VehicleClasses.Contains(type.VehicleClass), $"{file}: unsupported vehicle_class '{type.VehicleClass}'");
      ValidateTransformerLabel(type.Label, file, $"vehicle_type '{type.Id}'");
    }

    HashSet<string> movementIds = new(StringComparer.Ordinal);
    foreach (TransformerMovement movement in axes.Movement)
    {
      Require(movement is not null, $"{file}: movement cannot be null");
      Require(ValidTransformerId(movement!.IncomingEdgeId) && ValidTransformerId(movement.OutgoingEdgeId), $"{file}: movement requires valid incoming_edge_id and outgoing_edge_id");
      Require(movementIds.Add(MovementKey(movement.IncomingEdgeId, movement.OutgoingEdgeId)), $"{file}: duplicate movement '{movement.IncomingEdgeId}' -> '{movement.OutgoingEdgeId}'");
    }

    ValidateTransformerEvidence(model, file);
  }

  private static void ValidateTransformerEvidence(TransformerModel model, string file)
  {
    Require(model.Evidence is not null, $"{file}: evidence is required");
    Require(model.Evidence!.Length > 0, $"{file}: evidence must be nonempty");
    HashSet<string> covered = new(StringComparer.Ordinal);
    foreach (TransformerEvidence evidence in model.Evidence)
    {
      Require(evidence is not null, $"{file}: evidence cannot be null");
      Require(evidence!.TensorNames is not null, $"{file}: evidence requires tensor_names");
      ValidateTransformerIds(evidence.TensorNames!, file, "evidence tensor_names");
      bool knownKind = evidence.Kind == "measurement" || evidence.Kind == "derived_estimate" || evidence.Kind == "user_observation" || evidence.Kind == "authored_assumption";
      Require(knownKind, $"{file}: unsupported evidence kind '{evidence.Kind}'");
      Require(!string.IsNullOrWhiteSpace(evidence.Note), $"{file}: evidence requires a nonempty note");
      ValidateTransformerLabel(evidence.Source, file, "evidence source");
      foreach (string name in evidence.TensorNames!)
      {
        Require(name == "arrival_rates" || name == "journey_logits" || name == "movement_logits", $"{file}: evidence references unknown tensor '{name}'");
        covered.Add(name);
      }
    }

    Require(covered.Count == 3, $"{file}: evidence must cover arrival_rates, journey_logits and movement_logits");
  }

  private static void ValidateTransformerWeights(TransformerModel model, TransformerWeights weights)
  {
    string file = model.WeightsFile;
    Require(weights.FormatVersion == 2, $"{file}: unsupported format_version; expected 2");
    Require(weights.Tensors is not null, $"{file}: tensors cannot be null");
    TransformerAxes axes = model.Axes;
    int periods = axes.Period.Length;
    int origins = axes.Origin.Length;
    int destinations = axes.Destination.Length;
    int categories = axes.Category.Length;
    int vehicles = axes.VehicleType.Length;
    ValidateTransformerTensor(weights.Tensors!.ArrivalRates, file, "arrival_rates", ["period", "origin"], [periods, origins], true);
    ValidateTransformerTensor(weights.Tensors.JourneyLogits, file, "journey_logits", ["period", "origin", "destination", "category", "vehicle_type"], [periods, origins, destinations, categories, vehicles], false);
    ValidateTransformerTensor(weights.Tensors.MovementLogits, file, "movement_logits", ["period", "origin", "destination", "category", "vehicle_type", "movement"], [periods, origins, destinations, categories, vehicles, axes.Movement.Length], false);
    for (int period = 0; period < periods; period++)
    {
      double totalRate = 0;
      for (int origin = 0; origin < origins; origin++)
      {
        totalRate += weights.Tensors.ArrivalRates.Values[period * origins + origin];
      }

      Require(double.IsFinite(totalRate), $"{file}: arrival_rates period[{period}] exceeds finite numeric range");
    }
  }

  private static void ValidateTransformerTensor(TransformerTensor? tensor, string file, string name, string[] order, int[] shape, bool nonnegative)
  {
    Require(tensor is not null, $"{file}: tensor '{name}' cannot be null");
    Require(tensor!.Dtype == "float64", $"{file}: tensor '{name}' dtype must be float64");
    Require(tensor.AxisOrder is not null && tensor.Shape is not null && tensor.Values is not null, $"{file}: tensor '{name}' requires axis_order, shape and values");
    Require(tensor.AxisOrder!.Length == order.Length && tensor.Shape!.Length == shape.Length, $"{file}: tensor '{name}' has an invalid rank");
    long count = 1;
    for (int index = 0; index < order.Length; index++)
    {
      Require(tensor.AxisOrder[index] == order[index], $"{file}: tensor '{name}' axis_order[{index}] must be '{order[index]}'");
      Require(tensor.Shape[index] == shape[index], $"{file}: tensor '{name}' shape[{index}] must be {shape[index]}");
      Require(count <= int.MaxValue / shape[index], $"{file}: tensor '{name}' exceeds supported array size");
      count *= shape[index];
    }

    Require(tensor.Values!.Length == count, $"{file}: tensor '{name}' requires {count} values, found {tensor.Values.Length}");
    for (int index = 0; index < tensor.Values.Length; index++)
    {
      double value = tensor.Values[index];
      Require(double.IsFinite(value) && (!nonnegative || value >= 0), $"{file}: tensor '{name}' values[{index}] must be finite{(nonnegative ? " and nonnegative" : "")}");
    }
  }

  private static ProjectVehicleType[] ReadTransformerVehicleTypes(TransformerModel model)
  {
    ProjectVehicleType[] types = new ProjectVehicleType[model.Axes.VehicleType.Length];
    for (int index = 0; index < types.Length; index++)
    {
      TransformerVehicleType type = model.Axes.VehicleType[index];
      types[index] = new ProjectVehicleType { Id = type.Id, VehicleClass = type.VehicleClass, Weight = 1 };
    }

    return types;
  }

  private static void ValidateTransformerIds(string[] ids, string file, string axis)
  {
    Require(ids.Length > 0, $"{file}: {axis} must be nonempty");
    HashSet<string> unique = new(StringComparer.Ordinal);
    foreach (string id in ids)
    {
      Require(ValidTransformerId(id) && unique.Add(id), $"{file}: {axis} id '{id}' must be valid and unique");
    }
  }

  private static void ValidateTransformerLabel(string? label, string file, string item)
  {
    Require(label is null || !string.IsNullOrWhiteSpace(label), $"{file}: {item} label or source must be nonempty when present");
  }

  private static bool ValidTransformerId(string? id) => ValidId(id) && id!.Length <= MaximumProjectIdLength;
  private static string MovementKey(string incoming, string outgoing) => incoming + "\0" + outgoing;
}
