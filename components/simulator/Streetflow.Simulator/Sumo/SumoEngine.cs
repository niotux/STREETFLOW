using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.City;
using Streetflow.Simulator.Generation;

namespace Streetflow.Simulator.Sumo;

public sealed class SumoEngine : IAsyncDisposable
{
  private const int ConnectTimeoutSeconds = 1;
  private const int ConnectRetryMilliseconds = 50;
  private const int CloseTimeoutSeconds = 2;
  private const int ExitTimeoutSeconds = 3;
  private Process? process;
  private TraCiConnection? connection;
  private Task? stderrCopy;
  private Task? stdoutCopy;
  private StreamWriter? log;
  private string[] signalIds = [];
  private CountingLine[] countingLines = [];
  public int? ProcessId
  {
    get
    {
      if (process is null)
      {
        return null;
      }

      if (process.HasExited)
      {
        return null;
      }

      return process.Id;
    }
  }

  public async Task<string> StartAsync(Scenario scenario, RunConfiguration config, string directory, SimulatorOptions options, CancellationToken token)
  {
    int port = FindAvailablePort();
    countingLines = CreateCountingLines(scenario);
    string additionalPath = WriteDetectors(config, directory);
    ProcessStartInfo info = CreateStartInfo(config, directory, options.SumoBinary, port, additionalPath);
    signalIds = ReadSignalIds(directory);
    return await LaunchAsync(info, directory, port, options.EngineTimeoutSeconds, token);
  }

  public async Task<string> StartCityAsync(CityProject project, CityRunConfiguration configuration, string directory, SimulatorOptions options, CancellationToken token)
  {
    int port = FindAvailablePort();
    RunConfiguration config = new(project.Manifest.Id, project.Revision, configuration.Seed, configuration.StepSeconds, 1, 0);
    ProcessStartInfo info = CreateStartInfo(config, directory, options.SumoBinary, port, null, city: true);
    // Project references are validated and copied into the technical run package.
    int networkIndex = info.ArgumentList.IndexOf("--net-file") + 1;
    info.ArgumentList[networkIndex] = ProjectPaths.Resolve(project.Directory, project.Manifest.Network.File);
    if (project.Manifest.AdditionalFiles.Length > 0)
    {
      List<string> paths = [];
      foreach (string file in project.Manifest.AdditionalFiles)
      {
        paths.Add(ProjectPaths.Resolve(project.Directory, file));
      }

      AddArgument(info, "--additional-files", string.Join(",", paths));
    }

    AddArgument(info, "--time-to-teleport", configuration.MaximumBacklogSeconds.ToString(CultureInfo.InvariantCulture));
    AddArgument(info, "--max-num-vehicles", configuration.MaximumLiveVehicles.ToString(CultureInfo.InvariantCulture));
    signalIds = project.SignalIds;
    countingLines = [];
    string version = await LaunchAsync(info, directory, port, options.EngineTimeoutSeconds, token);
    connection!.MaximumDynamicRoutes = configuration.TransformerLimits?.MaximumDynamicRoutes ?? options.MaximumDynamicRoutes;
    return version;
  }

  private async Task<string> LaunchAsync(ProcessStartInfo info, string directory, int port, double timeoutSeconds, CancellationToken token)
  {
    process = Process.Start(info) ?? throw new InvalidOperationException("SUMO did not start");
    log = new(Path.Combine(directory, "engine.log"))
    {
      AutoFlush = true
    };
    // Only stderr writes this file; stdout is drained independently to avoid pipe deadlock.
    stderrCopy = CopyLogAsync(process.StandardError, log);
    stdoutCopy = process.StandardOutput.ReadToEndAsync(token);
    connection = await ConnectAsync(port, timeoutSeconds, token);
    return await connection.VersionAsync(token);
  }

  public Task AddVehiclesAsync(GeneratedArrival[] arrivals, double simulationTime, CancellationToken token) => connection!.AddVehiclesAsync(arrivals, simulationTime, token);

  public Task<SignalState[]> GetSignalsAsync(CancellationToken token) => connection!.GetSignalsAsync(signalIds, token);

  public Task SetSignalStateAsync(string signalId, string state, CancellationToken token) => connection!.SetSignalStateAsync(signalId, state, token);

  public Task<EngineSnapshot> StepAsync(CancellationToken token)
  {
    return connection!.StepAsync(signalIds, countingLines, token);
  }

  public Task<string[]> RouteAsync(string id, CancellationToken token)
  {
    return connection!.RouteAsync(id, token);
  }

  private static int FindAvailablePort()
  {
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
  }

  private static CountingLine[] CreateCountingLines(Scenario scenario)
  {
    HashSet<CountingLinePosition> uniquePositions = new();
    foreach (Camera camera in scenario.Cameras)
    {
      foreach (CameraLane cameraLane in camera.Lanes)
      {
        uniquePositions.Add(new CountingLinePosition(cameraLane.LaneId, cameraLane.CountPosition));
      }
    }

    foreach (Checkpoint checkpoint in scenario.Checkpoints.Checkpoints)
    {
      uniquePositions.Add(new CountingLinePosition(checkpoint.LaneId, checkpoint.Position));
    }

    List<CountingLinePosition> orderedPositions = new(uniquePositions);
    orderedPositions.Sort(CompareCountingLinePositions);
    CountingLine[] lines = new CountingLine[orderedPositions.Count];
    for (int index = 0; index < orderedPositions.Count; index++)
    {
      CountingLinePosition position = orderedPositions[index];
      lines[index] = new CountingLine($"streetflow-line-{index}", position.LaneId, position.Position);
    }

    return lines;
  }

  private static int CompareCountingLinePositions(CountingLinePosition left, CountingLinePosition right)
  {
    int laneComparison = StringComparer.Ordinal.Compare(left.LaneId, right.LaneId);
    if (laneComparison != 0)
    {
      return laneComparison;
    }

    return left.Position.CompareTo(right.Position);
  }

  private string WriteDetectors(RunConfiguration config, string directory)
  {
    XElement detectors = new("additional");
    foreach (CountingLine line in countingLines)
    {
      XElement detector = new("inductionLoop");
      detector.Add(new XAttribute("id", line.Id));
      detector.Add(new XAttribute("lane", line.LaneId));
      detector.Add(new XAttribute("pos", line.Position.ToString(CultureInfo.InvariantCulture)));
      detector.Add(new XAttribute("period", config.ObservationWindowSeconds));
      detector.Add(new XAttribute("file", Path.Combine(directory, "detector-output.xml")));
      detectors.Add(detector);
    }

    string additionalPath = Path.Combine(directory, "detectors.add.xml");
    detectors.Save(additionalPath);
    return additionalPath;
  }

  private static ProcessStartInfo CreateStartInfo(RunConfiguration config, string directory, string binary, int port, string? additionalPath, bool city = false)
  {
    ProcessStartInfo info = new(binary)
    {
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true
    };
    AddArgument(info, "--net-file", Path.Combine(directory, "network.net.xml"));
    AddArgument(info, "--route-files", Path.Combine(directory, "routes.rou.xml"));
    AddArgument(info, "--remote-port", port.ToString(CultureInfo.InvariantCulture));
    AddArgument(info, "--seed", config.Seed.ToString(CultureInfo.InvariantCulture));
    AddArgument(info, "--step-length", config.StepSeconds.ToString(CultureInfo.InvariantCulture));
    if (city)
    {
      AddArgument(info, "--tls.all-off", "true");
    }

    if (!city)
    {
      AddArgument(info, "--time-to-teleport", "-1");
      AddArgument(info, "--tripinfo-output", Path.Combine(directory, "tripinfo.xml"));
      AddArgument(info, "--tripinfo-output.write-unfinished", "true");
    }
    AddArgument(info, "--no-step-log", "true");
    AddArgument(info, "--duration-log.disable", "true");
    AddArgument(info, "--xml-validation", "never");
    AddArgument(info, "--xml-validation.net", "never");
    if (additionalPath is not null)
    {
      AddArgument(info, "--additional-files", additionalPath);
    }
    return info;
  }

  private static void AddArgument(ProcessStartInfo info, string name, string value)
  {
    info.ArgumentList.Add(name);
    info.ArgumentList.Add(value);
  }

  private async Task<TraCiConnection> ConnectAsync(int port, double timeoutSeconds, CancellationToken token)
  {
    DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (true)
    {
      token.ThrowIfCancellationRequested();
      if (process!.HasExited)
      {
        throw new InvalidOperationException($"SUMO exited during startup (code {process.ExitCode}); inspect engine.log");
      }

      using CancellationTokenSource connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
      connectDeadline.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));
      TcpClient client = new()
      {
        NoDelay = true
      };
      try
      {
        await client.ConnectAsync(IPAddress.Loopback, port, connectDeadline.Token);
        return new TraCiConnection(client, TimeSpan.FromSeconds(timeoutSeconds));
      }
      catch (Exception exception) when (exception is SocketException or OperationCanceledException)
      {
        client.Dispose();
        if (DateTime.UtcNow >= deadline)
        {
          throw new TimeoutException("Timed out connecting to SUMO");
        }

        await Task.Delay(ConnectRetryMilliseconds, token);
      }
    }
  }

  private static string[] ReadSignalIds(string directory)
  {
    // Joined controllers can have IDs different from junctions; use actual tlLogic IDs.
    XElement network = XDocument.Load(Path.Combine(directory, "network.net.xml")).Root!;
    List<string> ids = new();
    HashSet<string> distinctIds = new(StringComparer.Ordinal);
    foreach (XElement logic in network.Elements("tlLogic"))
    {
      string signalId = (string)logic.Attribute("id")!;
      if (distinctIds.Add(signalId))
      {
        ids.Add(signalId);
      }
    }

    return ids.ToArray();
  }

  private static async Task CopyLogAsync(StreamReader reader, StreamWriter writer)
  {
    while (true)
    {
      string? line = await reader.ReadLineAsync();
      if (line is null)
      {
        return;
      }

      await writer.WriteLineAsync(line);
    }
  }

  public async ValueTask DisposeAsync()
  {
    await CloseConnectionAsync();
    await StopProcessAsync();
    if (log is not null)
    {
      await log.DisposeAsync();
      log = null;
    }
  }

  private async Task CloseConnectionAsync()
  {
    if (connection is null)
    {
      return;
    }

    try
    {
      using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(CloseTimeoutSeconds));
      await connection.CloseAsync(timeout.Token);
    }
    catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or InvalidOperationException)
    {
    }

    connection.Dispose();
    connection = null;
  }

  private async Task StopProcessAsync()
  {
    if (process is null)
    {
      return;
    }

    try
    {
      using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(ExitTimeoutSeconds));
      await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }

      await process.WaitForExitAsync();
    }

    await DrainOutputAsync();
    process.Dispose();
    process = null;
  }

  private async Task DrainOutputAsync()
  {
    if (stderrCopy is not null)
    {
      await stderrCopy;
    }

    if (stdoutCopy is not null)
    {
      try
      {
        await stdoutCopy;
      }
      catch (OperationCanceledException)
      {
      }
    }
  }
}
