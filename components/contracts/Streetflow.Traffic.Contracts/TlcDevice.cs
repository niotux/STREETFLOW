namespace Streetflow.Traffic.Contracts;

public sealed record TlcDevice(string Id, string Label, double X, double Y, string[] LaneIds);
