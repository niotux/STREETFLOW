using Streetflow.Simulator.Runtime;

namespace Streetflow.Simulator.Api;

public sealed record ScenarioValidationResponse(bool Valid, RunConfiguration Configuration);
