namespace Streetflow.Simulator.Runtime;

internal sealed record RunCommandFingerprint(string Command, int Steps, Pacing? Pacing);
