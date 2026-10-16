namespace Streetflow.Traffic.Contracts;

public sealed record TacoDevice(string Id, string Label, double X, double Y, string[] LaneIds);
