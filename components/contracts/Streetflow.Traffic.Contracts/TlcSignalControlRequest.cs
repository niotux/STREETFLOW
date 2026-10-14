namespace Streetflow.Traffic.Contracts;

public sealed record TlcSignalControlRequest(string Action, int? LinkIndex = null);
