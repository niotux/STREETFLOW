namespace Streetflow.Simulator.Runtime;

public sealed class SimulatorOptions
{
  public string ScenarioRoot { get; set; } = null!;
  public string DataRoot { get; set; } = null!;
  public string SumoBinary { get; set; } = null!;
  public string NetconvertBinary { get; set; } = null!;
  public string DuarouterBinary { get; set; } = null!;
  public string? ConnectionString { get; set; }
  public int EngineTimeoutSeconds { get; set; }
  public int ObservationRetention { get; set; }
  public long MaximumProjectBytes { get; set; }
  public int MaximumProjectFiles { get; set; }
  public int MaximumLiveVehicles { get; set; }
  public int MaximumPendingEntries { get; set; }
  public int GenerationBatchSize { get; set; }
  public int MaximumTransformerStates { get; set; }
  public int MaximumTransformerRouteMovements { get; set; }
  public int MaximumTransformerCachedContexts { get; set; }
  public int MaximumDynamicRoutes { get; set; }
  public int MaximumNeuralRouteMovements { get; set; }
  public double MaximumBacklogSeconds { get; set; }
}
