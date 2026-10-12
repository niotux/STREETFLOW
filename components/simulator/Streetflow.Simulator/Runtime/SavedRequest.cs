namespace Streetflow.Simulator.Runtime;

internal sealed class SavedRequest
{
  public string Key { get; }
  public string Fingerprint { get; }
  public RunStatus Result { get; }

  public SavedRequest(string key, string fingerprint, RunStatus result)
  {
    Key = key;
    Fingerprint = fingerprint;
    Result = result;
  }
}
