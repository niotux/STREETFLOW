namespace Streetflow.Traffic.Contracts;

public sealed record TacoSignalControlRequest(string Action, int? LinkIndex = null);
