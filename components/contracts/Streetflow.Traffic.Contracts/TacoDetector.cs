namespace Streetflow.Traffic.Contracts;

public sealed record TacoDetector(string Id, string Label, double X, double Y, string[] LaneIds, string? DeviceId);
