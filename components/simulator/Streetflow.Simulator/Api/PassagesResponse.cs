using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record PassagesResponse(int Total, int Offset, Passage[] Passages);
