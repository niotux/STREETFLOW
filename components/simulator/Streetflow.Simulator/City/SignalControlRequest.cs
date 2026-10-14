namespace Streetflow.Simulator.City;

public sealed record SignalControlRequest(string Action, int? LinkIndex = null);
