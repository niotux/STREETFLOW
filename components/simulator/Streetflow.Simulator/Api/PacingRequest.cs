namespace Streetflow.Simulator.Api;

public sealed record PacingRequest(string RequestId, string Mode = "realtime", double Multiplier = 1);
