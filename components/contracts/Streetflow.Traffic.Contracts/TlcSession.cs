namespace Streetflow.Traffic.Contracts;

public sealed record TlcSession(string? Id, string? WorldId, string? Revision, string? ProjectId, string State);
