using System.Text.Json.Nodes;

namespace Streetflow.Simulator.Tests;

internal sealed class PhysicalDevicesFixture : IDisposable
{
  public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "streetflow-devices", Guid.NewGuid().ToString("N"));
  public string Id { get; }

  public PhysicalDevicesFixture(string source = "tiny-graph-devices")
  {
    Id = source;
    Directory.CreateDirectory(Path);
    string root = System.IO.Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", source);
    foreach (string file in Directory.GetFiles(root))
    {
      File.Copy(file, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(file)));
    }
  }

  public void Change(string file, Action<JsonNode> change)
  {
    string path = System.IO.Path.Combine(Path, file);
    JsonNode document = JsonNode.Parse(File.ReadAllText(path))!;
    change(document);
    File.WriteAllText(path, document.ToJsonString());
  }

  public void Dispose() => Directory.Delete(Path, true);
}
