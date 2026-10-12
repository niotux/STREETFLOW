using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Generation;

internal sealed record TerminalEndpoint(ProjectTerminal Terminal, ProjectLane Lane, int EdgeIndex);
