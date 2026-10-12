using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.City;

public sealed record CityRunCommand(int Steps = 1, Pacing? Pacing = null);
