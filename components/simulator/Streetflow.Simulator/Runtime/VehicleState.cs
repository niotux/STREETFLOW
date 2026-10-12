namespace Streetflow.Simulator.Runtime;

public sealed record VehicleState(string Id, string LaneId, double LanePosition, double X, double Y, double SpeedMps, double Angle, string EdgeId);
