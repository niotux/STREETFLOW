namespace Streetflow.Simulator.Scenarios;

// One entry in the projects folder: a project folder or a project ZIP, with its project or the reason it is unavailable.
public sealed record ProjectSource(string Name, string Kind, ProjectInfo? Project, string? Error)
{
  public const string FolderKind = "folder";
  public const string ZipKind = "zip";
}
