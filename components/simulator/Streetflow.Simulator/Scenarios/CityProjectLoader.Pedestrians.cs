using System.Xml.Linq;

namespace Streetflow.Simulator.Scenarios;

public static partial class CityProjectLoader
{
  private static void ValidatePedestrianFlow(XElement flow, ProjectEdge[] edges, HashSet<string> ids, string file)
  {
    RequireAttributes(flow, file, "id", "begin", "end", "period");
    string id = Required(flow, "id", file);
    Require(ValidId(id) && ids.Add(id), $"{file}: pedestrian flow IDs must be valid and unique");
    double begin = Number(flow, "begin", file);
    double end = Number(flow, "end", file);
    double period = Number(flow, "period", file);
    Require(begin >= 0 && end > begin && period > 0, $"{file}: personFlow '{id}' requires 0 <= begin < end and positive period");

    XElement? walk = null;
    foreach (XElement child in flow.Elements())
    {
      Require(child.Name == "walk" && walk is null, $"{file}: personFlow '{id}' requires one walk");
      walk = child;
    }

    Require(walk is not null, $"{file}: personFlow '{id}' requires one walk");
    RequireAttributes(walk!, file, "from", "to");
    string from = Required(walk!, "from", file);
    string to = Required(walk!, "to", file);
    Require(PedestrianEdgeExists(edges, from), $"{file}: personFlow '{id}' starts on a road without pedestrian access: '{from}'");
    Require(PedestrianEdgeExists(edges, to), $"{file}: personFlow '{id}' ends on a road without pedestrian access: '{to}'");
  }

  private static bool PedestrianEdgeExists(ProjectEdge[] edges, string id)
  {
    foreach (ProjectEdge edge in edges)
    {
      if (!edge.Ordinary || edge.Id != id)
      {
        continue;
      }

      foreach (ProjectLane lane in edge.Lanes)
      {
        if (lane.Permits("pedestrian"))
        {
          return true;
        }
      }
    }

    return false;
  }

  private static void RequireAttributes(XElement element, string file, params string[] allowed)
  {
    foreach (XAttribute attribute in element.Attributes())
    {
      bool found = false;
      foreach (string name in allowed)
      {
        if (attribute.Name.LocalName == name)
        {
          found = true;
          break;
        }
      }

      Require(found, $"{file}: <{element.Name}> does not support '{attribute.Name}'");
    }
  }
}
