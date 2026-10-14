using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Generation;
using System.Globalization;

namespace Streetflow.Simulator.Sumo;

// The small, supported TraCI surface used by this service. All calls have one owner.
// Protocol: https://sumo.dlr.de/docs/TraCI/Protocol.html
public sealed class TraCiConnection : IDisposable
{
  private readonly TcpClient client;
  private readonly TimeSpan timeout;
  private readonly NetworkStream stream;
  private readonly Dictionary<string, VehicleState> vehicles = new(StringComparer.Ordinal);
  private readonly Dictionary<string, string> dynamicRoutes = new(StringComparer.Ordinal);
  public int MaximumDynamicRoutes { private get; set; }
  private HashSet<LoopEntryKey> lastLoopEntries = [];
  private static readonly TraCiVariable[] VehicleVariables = [TraCiVariable.LaneId, TraCiVariable.LanePosition, TraCiVariable.Position, TraCiVariable.Speed, TraCiVariable.Angle, TraCiVariable.RoadId];
  public TraCiConnection(TcpClient client, TimeSpan timeout)
  {
    this.client = client;
    this.timeout = timeout;
    stream = client.GetStream();
  }

  public async Task<string> VersionAsync(CancellationToken token)
  {
    WireReader reader = await SendAsync([new TraCiCommand((byte)TraCiCommandId.GetVersion, [])], token);
    ReadStatus(reader, (byte)TraCiCommandId.GetVersion);
    WireReader response = reader.Command();
    if (response.Byte() != (byte)TraCiCommandId.GetVersion)
    {
      throw new InvalidDataException("Invalid version response");
    }

    _ = response.Int();
    return response.String();
  }

  public async Task<EngineSnapshot> StepAsync(string[] signalIds, CountingLine[] lines, CancellationToken token)
  {
    WireReader reader = await SendAsync([new TraCiCommand((byte)TraCiCommandId.SimulationStep, Encode(w => w.Double(TraCiProtocol.NextSimulationStepTarget)))], token);
    ReadStatus(reader, (byte)TraCiCommandId.SimulationStep);
    int subscriptionCount = reader.Int();
    for (int index = 0; index < subscriptionCount; index++)
    {
      ReadVehicleSubscription(reader.Command());
    }

    SimulationValues simulation = await GetSimulationValuesAsync(token);
    string[] active = simulation.ActiveVehicles;
    RemoveInactiveVehicles(active);
    await SubscribeNewVehiclesAsync(active, token);
    SignalState[] signals = await GetSignalsAsync(signalIds, token);
    LineCrossing[] crossings = await GetCrossingsAsync(lines, token);
    VehicleState[] activeVehicles = GetActiveVehicles(active);
    return new EngineSnapshot(simulation.Time, activeVehicles, signals, simulation.Departed, simulation.Arrived, simulation.Teleports, simulation.ExpectedVehicles, crossings);
  }

  private void RemoveInactiveVehicles(string[] active)
  {
    HashSet<string> activeSet = new(StringComparer.Ordinal);
    foreach (string vehicleId in active)
    {
      activeSet.Add(vehicleId);
    }

    List<string> inactiveVehicleIds = new();
    foreach (string vehicleId in vehicles.Keys)
    {
      if (!activeSet.Contains(vehicleId))
      {
        inactiveVehicleIds.Add(vehicleId);
      }
    }

    foreach (string vehicleId in inactiveVehicleIds)
    {
      vehicles.Remove(vehicleId);
    }
  }

  private async Task SubscribeNewVehiclesAsync(string[] active, CancellationToken token)
  {
    for (int offset = 0; offset < active.Length; offset += TraCiProtocol.QueryBatchSize)
    {
      int count = Math.Min(TraCiProtocol.QueryBatchSize, active.Length - offset);
      TraCiCommand[] commands = new TraCiCommand[count];
      int commandCount = 0;
      for (int index = 0; index < count; index++)
      {
        string vehicleId = active[offset + index];
        if (vehicles.ContainsKey(vehicleId))
        {
          continue;
        }

        commands[commandCount] = CreateVehicleSubscriptionCommand(vehicleId);
        commandCount++;
      }

      if (commandCount == 0)
      {
        continue;
      }

      if (commandCount != commands.Length)
      {
        Array.Resize(ref commands, commandCount);
      }

      WireReader subscriptions = await SendAsync(commands, token);
      for (int index = 0; index < commandCount; index++)
      {
        ReadStatus(subscriptions, (byte)TraCiCommandId.SubscribeVehicle);
        ReadVehicleSubscription(subscriptions.Command());
      }
    }
  }

  private static TraCiCommand CreateVehicleSubscriptionCommand(string vehicleId)
  {
    byte[] body = Encode(writer =>
    {
      writer.Double(TraCiProtocol.VehicleSubscriptionStartTime);
      writer.Double(TraCiProtocol.VehicleSubscriptionEndTime);
      writer.String(vehicleId);
      writer.Byte((byte)VehicleVariables.Length);
      foreach (TraCiVariable variable in VehicleVariables)
      {
        writer.Byte((byte)variable);
      }
    });
    return new TraCiCommand((byte)TraCiCommandId.SubscribeVehicle, body);
  }

  public async Task<SignalState[]> GetSignalsAsync(string[] signalIds, CancellationToken token)
  {
    SignalState[] signals = new SignalState[signalIds.Length];
    for (int offset = 0; offset < signalIds.Length; offset += TraCiProtocol.QueryBatchSize)
    {
      int count = Math.Min(TraCiProtocol.QueryBatchSize, signalIds.Length - offset);
      TraCiQuery[] stateQueries = new TraCiQuery[count];
      for (int index = 0; index < count; index++)
      {
        stateQueries[index] = new TraCiQuery(TraCiDomain.TrafficLight, TraCiVariable.TrafficLightState, signalIds[offset + index]);
      }

      string[] states = await GetAsync(stateQueries, static response => response.ReadStringValue(), token);
      for (int index = 0; index < count; index++)
      {
        signals[offset + index] = new SignalState(signalIds[offset + index], states[index]);
      }
    }

    return signals;
  }

  public async Task SetSignalStateAsync(string signalId, string state, CancellationToken token)
  {
    byte[] body = Encode(writer =>
    {
      writer.Byte((byte)TraCiVariable.TrafficLightState);
      writer.String(signalId);
      WriteTypedString(writer, state);
    });
    WireReader response = await SendAsync([new TraCiCommand((byte)TraCiCommandId.SetTrafficLight, body)], token);
    ReadStatus(response, (byte)TraCiCommandId.SetTrafficLight);
  }

  private async Task<LineCrossing[]> GetCrossingsAsync(CountingLine[] lines, CancellationToken token)
  {
    List<LineCrossing> crossings = new();
    HashSet<LoopEntryKey> seen = new();
    for (int offset = 0; offset < lines.Length; offset += TraCiProtocol.QueryBatchSize)
    {
      int batchSize = Math.Min(TraCiProtocol.QueryBatchSize, lines.Length - offset);
      TraCiQuery[] detectorQueries = new TraCiQuery[batchSize];
      for (int index = 0; index < batchSize; index++)
      {
        detectorQueries[index] = new TraCiQuery(TraCiDomain.InductionLoop, TraCiVariable.InductionLoopVehicleData, lines[offset + index].Id);
      }

      LoopEntry[][] records = await GetAsync(detectorQueries, static response => response.ReadLoopEntries(), token);
      for (int index = 0; index < batchSize; index++)
      {
        CountingLine line = lines[offset + index];
        foreach (LoopEntry entry in records[index])
        {
          LoopEntryKey key = new(line.Id, entry.VehicleId, entry.EntryTime);
          if (seen.Add(key) && !lastLoopEntries.Contains(key))
          {
            crossings.Add(new LineCrossing(entry.VehicleId, line.LaneId, line.Position, entry.EntryTime));
          }
        }
      }
    }

    lastLoopEntries = seen;
    return crossings.ToArray();
  }

  private VehicleState[] GetActiveVehicles(string[] active)
  {
    VehicleState[] activeVehicles = new VehicleState[active.Length];
    for (int index = 0; index < active.Length; index++)
    {
      activeVehicles[index] = vehicles[active[index]];
    }

    return activeVehicles;
  }

  public async Task<string[]> RouteAsync(string id, CancellationToken token)
  {
    TraCiQuery[] queries = [new TraCiQuery(TraCiDomain.Vehicle, TraCiVariable.VehicleRoute, id)];
    string[][] values = await GetAsync(queries, static response => response.ReadStringListValue(), token);
    return values[0];
  }

  public async Task CloseAsync(CancellationToken token)
  {
    WireReader response = await SendAsync([new TraCiCommand((byte)TraCiCommandId.Close, [])], token);
    ReadStatus(response, (byte)TraCiCommandId.Close);
  }

  public async Task AddVehiclesAsync(GeneratedArrival[] arrivals, double simulationTime, CancellationToken token)
  {
    const byte SetVehicleCommand = 0xc4;
    const byte AddVehicleVariable = 0x85;
    const int AddVehicleFieldCount = 14;
    List<TraCiCommand> commands = [];
    for (int index = 0; index < arrivals.Length; index++)
    {
      GeneratedArrival arrival = arrivals[index];
      string routeId = ResolveRoute(arrival.Route, commands);
      byte[] body = Encode(writer =>
      {
        writer.Byte(AddVehicleVariable);
        writer.String(arrival.Id);
        writer.Byte((byte)TraCiValueType.Compound);
        writer.Int(AddVehicleFieldCount);
        WriteTypedString(writer, routeId);
        WriteTypedString(writer, arrival.Route.VehicleTypeId);
        WriteTypedString(writer, Math.Max(arrival.DepartureSeconds, simulationTime).ToString("R", CultureInfo.InvariantCulture));
        WriteTypedString(writer, arrival.Route.DepartureLaneIndex?.ToString(CultureInfo.InvariantCulture) ?? "best");
        WriteTypedString(writer, arrival.Route.DeparturePositionM?.ToString("R", CultureInfo.InvariantCulture) ?? "base");
        WriteTypedString(writer, "0");
        WriteTypedString(writer, arrival.Route.ArrivalLaneIndex?.ToString(CultureInfo.InvariantCulture) ?? "current");
        WriteTypedString(writer, arrival.Route.ArrivalPositionM?.ToString("R", CultureInfo.InvariantCulture) ?? "max");
        WriteTypedString(writer, "current");
        WriteTypedString(writer, "");
        WriteTypedString(writer, "");
        WriteTypedString(writer, "");
        writer.Byte((byte)TraCiValueType.Integer);
        writer.Int(0);
        writer.Byte((byte)TraCiValueType.Integer);
        writer.Int(0);
      });
      commands.Add(new TraCiCommand(SetVehicleCommand, body));
    }

    if (commands.Count == 0)
    {
      return;
    }

    WireReader response = await SendAsync(commands.ToArray(), token);
    foreach (TraCiCommand command in commands)
    {
      ReadStatus(response, command.Id);
    }
  }

  private string ResolveRoute(RouteCandidate route, List<TraCiCommand> commands)
  {
    if (!route.IsDynamic)
    {
      return route.Id;
    }

    string key = string.Join("\0", route.Edges);
    if (dynamicRoutes.TryGetValue(key, out string? existing))
    {
      return existing;
    }

    if (dynamicRoutes.Count >= MaximumDynamicRoutes)
    {
      throw new ApiException(400, "generation_error", $"Dynamic SUMO routes exceed the configured limit of {MaximumDynamicRoutes}; traffic was not redirected");
    }

    const byte AddRouteVariable = 0x80;
    byte[] body = Encode(writer =>
    {
      writer.Byte(AddRouteVariable);
      writer.String(route.Id);
      writer.Byte((byte)TraCiValueType.StringList);
      writer.Int(route.Edges.Length);
      foreach (string edge in route.Edges)
      {
        writer.String(edge);
      }
    });
    commands.Add(new TraCiCommand((byte)TraCiCommandId.SetRoute, body));
    dynamicRoutes.Add(key, route.Id);
    return route.Id;
  }

  private static void WriteTypedString(WireWriter writer, string value)
  {
    writer.Byte((byte)TraCiValueType.String);
    writer.String(value);
  }

  private async Task<SimulationValues> GetSimulationValuesAsync(CancellationToken token)
  {
    TraCiQuery[] queries = CreateSimulationQueries();
    TraCiCommand[] commands = CreateGetCommands(queries);
    WireReader reader = await SendAsync(commands, token);
    SimulationValues values = new();
    foreach (TraCiQuery query in queries)
    {
      WireReader response = ReadGetResponse(reader, query);
      switch (query.Variable)
      {
        case TraCiVariable.SimulationTime:
          values.Time = response.ReadDoubleValue();
          break;
        case TraCiVariable.DepartedVehicleIds:
          values.Departed = response.ReadStringListValue();
          break;
        case TraCiVariable.ArrivedVehicleIds:
          values.Arrived = response.ReadStringListValue();
          break;
        case TraCiVariable.TeleportingVehicleIds:
          values.Teleports = response.ReadStringListValue();
          break;
        case TraCiVariable.ExpectedVehicleCount:
          values.ExpectedVehicles = response.ReadIntegerValue();
          break;
        case TraCiVariable.ActiveVehicleIds:
          values.ActiveVehicles = response.ReadStringListValue();
          break;
        default:
          throw new InvalidDataException($"Unsupported simulation variable: 0x{(byte)query.Variable:x2}");
      }
    }

    return values;
  }

  private static TraCiQuery[] CreateSimulationQueries()
  {
    TraCiQuery time = SimulationQuery(TraCiVariable.SimulationTime);
    TraCiQuery departed = SimulationQuery(TraCiVariable.DepartedVehicleIds);
    TraCiQuery arrived = SimulationQuery(TraCiVariable.ArrivedVehicleIds);
    TraCiQuery teleports = SimulationQuery(TraCiVariable.TeleportingVehicleIds);
    TraCiQuery expectedVehicles = SimulationQuery(TraCiVariable.ExpectedVehicleCount);
    TraCiQuery activeVehicles = new(TraCiDomain.Vehicle, TraCiVariable.ActiveVehicleIds, "");
    return [time, departed, arrived, teleports, expectedVehicles, activeVehicles];
  }

  private static TraCiQuery SimulationQuery(TraCiVariable variable)
  {
    return new TraCiQuery(TraCiDomain.Simulation, variable, "");
  }

  private async Task<TValue[]> GetAsync<TValue>(TraCiQuery[] queries, Func<WireReader, TValue> readValue, CancellationToken token)
  {
    TraCiCommand[] commands = CreateGetCommands(queries);
    WireReader reader = await SendAsync(commands, token);
    TValue[] values = new TValue[queries.Length];
    for (int index = 0; index < queries.Length; index++)
    {
      TraCiQuery query = queries[index];
      WireReader response = ReadGetResponse(reader, query);
      values[index] = readValue(response);
    }

    return values;
  }

  private static TraCiCommand[] CreateGetCommands(TraCiQuery[] queries)
  {
    TraCiCommand[] commands = new TraCiCommand[queries.Length];
    for (int index = 0; index < queries.Length; index++)
    {
      TraCiQuery query = queries[index];
      byte[] body = Encode(writer =>
      {
        writer.Byte((byte)query.Variable);
        writer.String(query.Id);
      });
      commands[index] = new TraCiCommand((byte)query.Domain, body);
    }

    return commands;
  }

  private static WireReader ReadGetResponse(WireReader reader, TraCiQuery query)
  {
    ReadStatus(reader, (byte)query.Domain);
    WireReader response = reader.Command();
    bool responseMatchesQuery = response.Byte() == (byte)query.Domain + TraCiProtocol.ValueResponseOffset && response.Byte() == (byte)query.Variable && response.String() == query.Id;
    if (!responseMatchesQuery)
    {
      throw new InvalidDataException("Mismatched TraCI response");
    }

    return response;
  }

  private void ReadVehicleSubscription(WireReader response)
  {
    if (response.Byte() != TraCiProtocol.VehicleSubscriptionResponse)
    {
      throw new InvalidDataException("Unsupported subscription domain");
    }

    string id = response.String();
    int count = response.Byte();
    string laneId = "";
    string edgeId = "";
    double lanePosition = 0;
    double speed = 0;
    double angle = 0;
    Point? position = null;
    bool hasLane = false;
    bool hasEdge = false;
    bool hasLanePosition = false;
    bool hasSpeed = false;
    bool hasAngle = false;
    for (int index = 0; index < count; index++)
    {
      TraCiVariable variable = (TraCiVariable)response.Byte();
      byte status = response.Byte();
      if (status != TraCiProtocol.SuccessStatus)
      {
        throw new InvalidDataException($"Subscription error for vehicle '{id}', variable 0x{(byte)variable:x2}");
      }

      switch (variable)
      {
        case TraCiVariable.Position:
          position = response.ReadPositionValue();
          break;
        case TraCiVariable.LaneId:
          laneId = response.ReadStringValue();
          hasLane = true;
          break;
        case TraCiVariable.LanePosition:
          lanePosition = response.ReadDoubleValue();
          hasLanePosition = true;
          break;
        case TraCiVariable.Speed:
          speed = response.ReadDoubleValue();
          hasSpeed = true;
          break;
        case TraCiVariable.Angle:
          angle = response.ReadDoubleValue();
          hasAngle = true;
          break;
        case TraCiVariable.RoadId:
          edgeId = response.ReadStringValue();
          hasEdge = true;
          break;
        default:
          throw new InvalidDataException($"Unsupported vehicle variable: 0x{(byte)variable:x2}");
      }
    }

    bool subscriptionIsComplete = hasLane && hasEdge && hasLanePosition && hasSpeed && hasAngle && position is not null;
    if (!subscriptionIsComplete)
    {
      throw new InvalidDataException($"Incomplete vehicle subscription for '{id}'");
    }

    Point vehiclePosition = position ?? throw new InvalidDataException($"Missing position for vehicle '{id}'");
    vehicles[id] = new(id, laneId, lanePosition, vehiclePosition.X, vehiclePosition.Y, speed, angle, edgeId);
  }

  private async Task<WireReader> SendAsync(TraCiCommand[] commands, CancellationToken token)
  {
    using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
    deadline.CancelAfter(timeout);
    byte[] payload = EncodeCommands(commands);
    await WritePacketAsync(payload, deadline.Token);
    return await ReadPacketAsync(deadline.Token);
  }

  private static byte[] EncodeCommands(TraCiCommand[] commands)
  {
    return Encode(writer =>
    {
      foreach (TraCiCommand command in commands)
      {
        if (command.Body.Length + TraCiProtocol.ShortCommandHeaderLength <= TraCiProtocol.MaximumShortCommandLength)
        {
          writer.Byte((byte)(command.Body.Length + TraCiProtocol.ShortCommandHeaderLength));
        }
        else
        {
          writer.Byte(TraCiProtocol.ExtendedCommandLengthMarker);
          writer.Int(command.Body.Length + TraCiProtocol.LongCommandHeaderLength);
        }

        writer.Byte(command.Id);
        writer.Bytes(command.Body);
      }
    });
  }

  private async Task WritePacketAsync(byte[] payload, CancellationToken token)
  {
    int packetLength = payload.Length + TraCiProtocol.PacketHeaderLength;
    byte[] packet = ArrayPool<byte>.Shared.Rent(packetLength);
    try
    {
      BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, TraCiProtocol.PacketHeaderLength), packetLength);
      payload.CopyTo(packet, TraCiProtocol.PacketHeaderLength);
      await stream.WriteAsync(packet.AsMemory(0, packetLength), token);
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(packet);
    }
  }

  private async Task<WireReader> ReadPacketAsync(CancellationToken token)
  {
    byte[] header = ArrayPool<byte>.Shared.Rent(TraCiProtocol.PacketHeaderLength);
    int length;
    try
    {
      await stream.ReadExactlyAsync(header.AsMemory(0, TraCiProtocol.PacketHeaderLength), token);
      length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, TraCiProtocol.PacketHeaderLength)) - TraCiProtocol.PacketHeaderLength;
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(header);
    }

    // This bounds a single wire message, not the network or vehicle population.
    if (length < 0 || length > TraCiProtocol.MaximumMessageLength)
    {
      throw new InvalidDataException("Invalid TraCI message length");
    }

    byte[] response = new byte[length];
    await stream.ReadExactlyAsync(response, token);
    return new(response);
  }

  private static void ReadStatus(WireReader reader, byte expected)
  {
    WireReader status = reader.Command();
    if (status.Byte() != expected)
    {
      throw new InvalidDataException("Unexpected TraCI status command");
    }

    byte code = status.Byte();
    string message = status.String();
    if (code != TraCiProtocol.SuccessStatus)
    {
      throw new InvalidOperationException($"SUMO rejected command 0x{expected:x2}: {message}");
    }
  }

  private static byte[] Encode(Action<WireWriter> write)
  {
    WireWriter writer = new();
    write(writer);
    return writer.ToArray();
  }

  public void Dispose()
  {
    client.Dispose();
  }
}
