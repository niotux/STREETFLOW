namespace Streetflow.Simulator.Scenarios;

internal sealed record StoredProject(int FormatVersion, string Id, string Revision, DateTimeOffset ImportedAt);
