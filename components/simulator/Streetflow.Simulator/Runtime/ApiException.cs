namespace Streetflow.Simulator.Runtime;

public sealed class ApiException(int statusCode, string code, string message) : Exception(message)
{
  public int StatusCode { get; } = statusCode;
  public string Code { get; } = code;
}
