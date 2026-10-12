namespace Streetflow.Simulator.Scenarios;

public sealed class ScenarioManifest
{
  public string Id { get; init; }
  public string Revision { get; init; }
  public string Name { get; init; }
  public string NetworkRevision { get; init; }
  public string DetectorConfigRevision { get; init; }
  public string EvidenceLevel { get; init; }
  public string NetworkFile { get; init; }
  public string RoutesFile { get; init; }
  public string Attribution { get; init; }
  public double DurationSeconds { get; init; }
  public double StepSeconds { get; init; }
  public double ObservationWindowSeconds { get; init; }
  public double QueueSpeedMps { get; init; }
  public double QueueDurationSeconds { get; init; }

  public ScenarioManifest(string id, string revision, string name, string networkRevision, string detectorConfigRevision, string evidenceLevel, string networkFile, string routesFile, string attribution, double durationSeconds = 300, double stepSeconds = 0.5, double observationWindowSeconds = 5, double queueSpeedMps = 0.1, double queueDurationSeconds = 1)
  {
    Id = id;
    Revision = revision;
    Name = name;
    NetworkRevision = networkRevision;
    DetectorConfigRevision = detectorConfigRevision;
    EvidenceLevel = evidenceLevel;
    NetworkFile = networkFile;
    RoutesFile = routesFile;
    Attribution = attribution;
    DurationSeconds = durationSeconds;
    StepSeconds = stepSeconds;
    ObservationWindowSeconds = observationWindowSeconds;
    QueueSpeedMps = queueSpeedMps;
    QueueDurationSeconds = queueDurationSeconds;
  }

  public ScenarioManifest WithFiles(string networkFile, string routesFile)
  {
    return new ScenarioManifest(Id, Revision, Name, NetworkRevision, DetectorConfigRevision, EvidenceLevel, networkFile, routesFile, Attribution, DurationSeconds, StepSeconds, ObservationWindowSeconds, QueueSpeedMps, QueueDurationSeconds);
  }
}
