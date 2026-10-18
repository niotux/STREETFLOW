namespace Streetflow.Frontend;

public sealed class FrontendOptions
{
  public string SimulatorBaseUrl { get; set; } = "";
  public string LogisticsBaseUrl { get; set; } = "";
  public string CasperBaseUrl { get; set; } = "http://localhost:8082";
  public string DemoCameraToken { get; set; } = "";
}
