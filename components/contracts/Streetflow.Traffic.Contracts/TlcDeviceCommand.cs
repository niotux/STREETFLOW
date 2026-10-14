namespace Streetflow.Traffic.Contracts;

public sealed record TlcDeviceCommand(string Action, string WorldId, string Revision);
