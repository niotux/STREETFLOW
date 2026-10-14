using System.Text.Json.Nodes;

namespace Streetflow.Simulator.Tests;

internal sealed class TransformerProjectFixture : IDisposable
{
  internal const string ProjectId = "four-way-transformer-routes";
  public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "streetflow-transformer-tests", Guid.NewGuid().ToString("N"));
  public static string Source => System.IO.Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", ProjectId);

  public TransformerProjectFixture()
  {
    Directory.CreateDirectory(Path);
    foreach (string file in Directory.GetFiles(Source))
    {
      File.Copy(file, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(file)));
    }
  }

  public void Change(string file, Action<JsonNode> mutation)
  {
    string path = System.IO.Path.Combine(Path, file);
    JsonNode node = JsonNode.Parse(File.ReadAllText(path))!;
    mutation(node);
    File.WriteAllText(path, node.ToJsonString());
  }

  public void Dispose()
  {
    if (Directory.Exists(Path))
    {
      Directory.Delete(Path, true);
    }
  }
}
