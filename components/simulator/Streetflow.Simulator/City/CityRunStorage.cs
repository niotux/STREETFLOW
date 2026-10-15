using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Streetflow.Simulator.Generation;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;

namespace Streetflow.Simulator.City;

public sealed class CityRunStorage(SimulatorOptions options)
{
  public string Root => Path.Combine(options.DataRoot, "city-runs");
  public string DirectoryFor(string id) => Path.Combine(Root, id);

  public CityProject CopyProject(CityProject project, string id)
  {
    string package = Path.Combine(DirectoryFor(id), "package");
    Directory.CreateDirectory(package);
    CopyFile(project.Directory, package, "experiment.json");
    CopyFile(project.Directory, package, project.Manifest.Network.File);
    if (project.Manifest.DemandFile is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.DemandFile);
    }

    if (project.Manifest.JourneyModelFile is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.JourneyModelFile);
      CopyFile(project.Directory, package, project.TransformerModel!.WeightsFile);
    }
    if (project.NeuralModel is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.NeuralModelFile!);
      CopyFile(project.Directory, package, project.NeuralModel.ModelFile);
    }
    if (project.Manifest.TrafficLightsFile is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.TrafficLightsFile);
    }

    if (project.Manifest.DetectorsFile is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.DetectorsFile);
    }

    if (project.Manifest.TerminalsFile is not null)
    {
      CopyFile(project.Directory, package, project.Manifest.TerminalsFile);
    }

    foreach (string file in project.Manifest.AdditionalFiles)
    {
      CopyFile(project.Directory, package, file);
    }

    return CityProjectLoader.Load(package);
  }

  private static void CopyFile(string source, string destination, string file)
  {
    string path = ProjectPaths.Resolve(destination, file, false);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.Copy(ProjectPaths.Resolve(source, file), path);
  }

  public void WriteRoutes(string id, CityProject project, RouteCandidate[] candidates)
  {
    XElement routes = new("routes");
    foreach (ProjectVehicleType type in project.VehicleTypes)
    {
      routes.Add(new XElement("vType", new XAttribute("id", type.Id), new XAttribute("vClass", type.VehicleClass)));
    }

    foreach (RouteCandidate candidate in candidates)
    {
      routes.Add(new XElement("route", new XAttribute("id", candidate.Id), new XAttribute("edges", string.Join(" ", candidate.Edges))));
    }

    routes.Save(Path.Combine(DirectoryFor(id), "routes.rou.xml"));
  }

  public async Task SaveAsync(CityRunStatus status)
  {
    string directory = DirectoryFor(status.Id);
    Directory.CreateDirectory(directory);
    string temporary = Path.Combine(directory, "status.tmp");
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(status, JsonDefaults.Options));
    File.Move(temporary, Path.Combine(directory, "status.json"), overwrite: true);
  }

  public CityRunStatus[] Load()
  {
    Directory.CreateDirectory(Root);
    List<CityRunStatus> statuses = [];
    foreach (string directory in Directory.EnumerateDirectories(Root))
    {
      string path = Path.Combine(directory, "status.json");
      if (File.Exists(path))
      {
        CityRunStatus? status = JsonSerializer.Deserialize<CityRunStatus>(File.ReadAllText(path), JsonDefaults.Options);
        if (status is not null)
        {
          statuses.Add(status);
        }
      }
    }

    return statuses.ToArray();
  }
}
