namespace Streetflow.Traffic.Contracts;

public sealed record TacoMap(string WorldId, string Revision, string? Attribution, string CoordinateSystem, TacoLane[] Lanes, TacoDevice[] Devices);
