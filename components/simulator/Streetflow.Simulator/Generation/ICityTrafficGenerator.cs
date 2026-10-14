namespace Streetflow.Simulator.Generation;

public interface ICityTrafficGenerator
{
  RouteCandidate[] Candidates { get; }
  double NextDepartureSeconds { get; }
  bool Ended { get; }
  double RatePerSecond(double elapsed);
  GeneratedArrival[] GenerateUntil(double elapsed, int capacity);
}
