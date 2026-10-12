using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.Api;

public sealed record TrafficCatalogResponse(string SchemaVersion, string? SessionId, string? NetworkRevision, string? DetectorConfigRevision, Camera[] Cameras, string[] SupportedFields, string TimeBasis, string ObservationMethod);
