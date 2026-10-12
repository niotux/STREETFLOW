using System.Xml.Linq;
using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Scenarios;

public static class DemandGenerator
{
  // Preserve explicit routes/departure times; change volume reproducibly.
  public static void Resolve(string inputPath, string outputPath, double multiplier, int seed)
  {
    if (multiplier == 1)
    {
      File.Copy(inputPath, outputPath);
      return;
    }

    XDocument document = XDocument.Load(inputPath);
    XElement root = document.Root!;
    foreach (XElement element in root.Elements())
    {
      if (element.Name.LocalName is "flow" or "trip" or "personFlow")
      {
        throw new ApiException(400, "unsupported_demand_override", "Volume overrides currently require explicit vehicle routes; use native flow definitions without an override");
      }
    }

    List<XElement> originals = new();
    foreach (XElement vehicle in root.Elements("vehicle"))
    {
      originals.Add(vehicle);
    }

    int repetitions = (int)Math.Floor(multiplier);
    double fraction = multiplier - repetitions;
    Random random = new Random(seed);
    foreach (XElement vehicle in originals)
    {
      int count = repetitions + (random.NextDouble() < fraction ? 1 : 0);
      for (int index = 0; index < count; index++)
      {
        XElement copy = new XElement(vehicle);
        copy.SetAttributeValue("id", (string)vehicle.Attribute("id")! + "-copy-" + index);
        vehicle.AddBeforeSelf(copy);
      }

      vehicle.Remove();
    }

    document.Save(outputPath);
  }
}
