namespace Streetflow.Logistics;

public sealed class LogisticsOptions
{
  public string TlcApiBaseUrl { get; set; } = "";
  public string ConnectionFile { get; set; } = "";
  public int DiscoveryIntervalMilliseconds { get; set; }
  public int RequestTimeoutSeconds { get; set; }
}
