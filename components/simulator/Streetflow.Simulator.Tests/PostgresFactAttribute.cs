namespace Streetflow.Simulator.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
  public PostgresFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STREETFLOW_TEST_POSTGRES")))
    {
      Skip = "Set STREETFLOW_TEST_POSTGRES to run the PostgreSQL integration test";
    }
  }
}
