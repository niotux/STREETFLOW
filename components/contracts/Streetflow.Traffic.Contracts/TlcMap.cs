namespace Streetflow.Traffic.Contracts;

public sealed record TlcMap(string WorldId, string Revision, string? Attribution, string CoordinateSystem, TlcLane[] Lanes, TlcDevice[] Devices);
