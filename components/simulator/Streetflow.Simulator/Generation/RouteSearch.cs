namespace Streetflow.Simulator.Generation;

internal sealed record RouteSearch(double[] Costs, int[] Previous, int[]? TerminalPath = null);
