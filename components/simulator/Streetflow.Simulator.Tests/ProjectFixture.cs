using System.Text.Json.Nodes;

namespace Streetflow.Simulator.Tests;

internal sealed class ProjectFixture : IDisposable
{
  public string Path { get; }

  // A parent folder places the fixture directly in a projects folder, where tests edit it in place.
  public ProjectFixture(string? parent = null)
  {
    Path = System.IO.Path.Combine(parent ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "streetflow-project-tests"), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path);
    foreach (string file in Directory.GetFiles(System.IO.Path.Combine(ProjectLoadingTests.Workspace, "experiments", "drafts", "tiny-graph")))
    {
      File.Copy(file, System.IO.Path.Combine(Path, System.IO.Path.GetFileName(file)));
    }
  }

  public void ChangeDemand(Action<JsonNode> mutation) => Change("demand.json", mutation);
  public void ChangeManifest(Action<JsonNode> mutation) => Change("experiment.json", mutation);
  private void Change(string file, Action<JsonNode> mutation)
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
