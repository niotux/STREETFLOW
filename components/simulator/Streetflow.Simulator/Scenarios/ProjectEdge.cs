namespace Streetflow.Simulator.Scenarios;

public sealed record ProjectEdge(string Id, string? Name, bool Ordinary, string? From, string? To, ProjectLane[] Lanes);
