namespace Streetflow.Simulator.Sumo;

internal readonly struct TraCiQuery
{
  public TraCiDomain Domain { get; }
  public TraCiVariable Variable { get; }
  public string Id { get; }

  public TraCiQuery(TraCiDomain domain, TraCiVariable variable, string id)
  {
    Domain = domain;
    Variable = variable;
    Id = id;
  }
}
