using System.Text.Json;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Streetflow.Simulator.Runtime;
using Streetflow.Traffic.Contracts;

namespace Streetflow.Simulator.Persistence;
// Files are the complete run archive. PostgreSQL indexes metadata, observations and trips.
// Writes are awaited between simulation steps: bounded memory and explicit storage failures.
public sealed class RunStore(SimulatorOptions options) : IDisposable
{
  private const int TripBatchSize = 500;
  private const string InitializeSql = """
    CREATE TABLE IF NOT EXISTS simulator_runs (
      id text PRIMARY KEY, data jsonb NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());
    CREATE TABLE IF NOT EXISTS simulator_observations (
      run_id text NOT NULL REFERENCES simulator_runs(id), sequence bigint NOT NULL, data jsonb NOT NULL,
      PRIMARY KEY(run_id, sequence));
    CREATE TABLE IF NOT EXISTS simulator_trips (
      run_id text NOT NULL REFERENCES simulator_runs(id), vehicle_id text NOT NULL, data jsonb NOT NULL,
      PRIMARY KEY(run_id, vehicle_id));
    """;
  private const string SaveStatusSql = """
    INSERT INTO simulator_runs(id,data) VALUES ($1,$2)
    ON CONFLICT(id) DO UPDATE SET data=excluded.data, updated_at=now()
    """;
  private const string AppendObservationSql = "INSERT INTO simulator_observations(run_id,sequence,data) VALUES ($1,$2,$3) ON CONFLICT DO NOTHING";
  private const string SaveTripSql = "INSERT INTO simulator_trips(run_id,vehicle_id,data) VALUES ($1,$2,$3) ON CONFLICT(run_id,vehicle_id) DO UPDATE SET data=excluded.data";
  private readonly NpgsqlDataSource? database = string.IsNullOrWhiteSpace(options.ConnectionString) ? null : NpgsqlDataSource.Create(options.ConnectionString);
  public string Root { get; } = Path.GetFullPath(options.DataRoot);
  public bool UsesPostgres => database is not null;

  public string DirectoryFor(string id)
  {
    return Path.Combine(Root, id);
  }

  public async Task InitializeAsync(CancellationToken token)
  {
    Directory.CreateDirectory(Root);
    if (database is null)
    {
      return;
    }

    await using NpgsqlCommand command = database.CreateCommand(InitializeSql);
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task SaveStatusAsync(RunStatus status, CancellationToken token)
  {
    string directory = DirectoryFor(status.Id);
    Directory.CreateDirectory(directory);
    string json = JsonSerializer.Serialize(status, JsonDefaults.Options);
    string temp = Path.Combine(directory, "status.json.tmp");
    await File.WriteAllTextAsync(temp, json, token);
    File.Move(temp, Path.Combine(directory, "status.json"), overwrite: true);
    if (database is null)
    {
      return;
    }

    await using NpgsqlCommand command = database.CreateCommand(SaveStatusSql);
    command.Parameters.AddWithValue(status.Id);
    command.Parameters.Add(new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = json });
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task AppendObservationsAsync(string id, TrafficObservation[] frames, CancellationToken token)
  {
    if (frames.Length == 0)
    {
      return;
    }

    StringBuilder lines = new();
    foreach (TrafficObservation frame in frames)
    {
      lines.Append(JsonSerializer.Serialize(frame, CompactOptions));
      lines.Append('\n');
    }

    await File.AppendAllTextAsync(Path.Combine(DirectoryFor(id), "observations.jsonl"), lines.ToString(), token);
    if (database is null)
    {
      return;
    }

    await using NpgsqlBatch batch = database.CreateBatch();
    foreach (TrafficObservation frame in frames)
    {
      NpgsqlBatchCommand command = new(AppendObservationSql);
      command.Parameters.AddWithValue(id);
      command.Parameters.AddWithValue(frame.Sequence);
      command.Parameters.Add(new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(frame, CompactOptions) });
      batch.BatchCommands.Add(command);
    }

    await batch.ExecuteNonQueryAsync(token);
  }

  public async Task SaveTripsAsync(string id, TripRecord[] trips, CancellationToken token)
  {
    await File.WriteAllTextAsync(Path.Combine(DirectoryFor(id), "trips.json"), JsonSerializer.Serialize(trips, JsonDefaults.Options), token);
    if (database is null)
    {
      return;
    }

    if (trips.Length == 0)
    {
      return;
    }

    int offset = 0;
    while (offset < trips.Length)
    {
      await using NpgsqlBatch batch = database.CreateBatch();
      int batchEnd = Math.Min(offset + TripBatchSize, trips.Length);
      for (int index = offset; index < batchEnd; index++)
      {
        TripRecord trip = trips[index];
        NpgsqlBatchCommand command = new(SaveTripSql);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(trip.Id);
        command.Parameters.Add(new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(trip, CompactOptions) });
        batch.BatchCommands.Add(command);
      }

      await batch.ExecuteNonQueryAsync(token);
      offset = batchEnd;
    }
  }

  public RunStatus[] LoadStatuses()
  {
    List<RunStatus> statuses = new();
    // Project packages share this storage volume; their files are not run records.
    foreach (string directory in Directory.EnumerateDirectories(Root))
    {
      if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
      {
        continue;
      }

      string file = Path.Combine(directory, "status.json");
      if (!File.Exists(file))
      {
        continue;
      }

      RunStatus? status = JsonSerializer.Deserialize<RunStatus>(File.ReadAllText(file), JsonDefaults.Options);
      if (status is not null)
      {
        statuses.Add(status);
      }
    }

    return statuses.ToArray();
  }

  public TripRecord[] LoadTrips(string id)
  {
    string path = Path.Combine(DirectoryFor(id), "trips.json");
    if (!File.Exists(path))
    {
      return [];
    }

    return JsonSerializer.Deserialize<TripRecord[]>(File.ReadAllText(path), JsonDefaults.Options) ?? [];
  }

  public TrafficObservation[] ReadObservations(string id, long after, int limit)
  {
    string path = Path.Combine(DirectoryFor(id), "observations.jsonl");
    if (!File.Exists(path))
    {
      return [];
    }

    List<TrafficObservation> frames = new();
    foreach (string line in File.ReadLines(path))
    {
      TrafficObservation? frame = JsonSerializer.Deserialize<TrafficObservation>(line, JsonDefaults.Options);
      if (frame is null)
      {
        continue;
      }

      if (frame.Sequence <= after)
      {
        continue;
      }

      frames.Add(frame);
      if (frames.Count == limit)
      {
        break;
      }
    }

    return frames.ToArray();
  }

  public Task AppendEventAsync<T>(string id, T entry, CancellationToken token)
  {
    string path = Path.Combine(DirectoryFor(id), "events.jsonl");
    string line = JsonSerializer.Serialize(entry, CompactOptions) + "\n";
    return File.AppendAllTextAsync(path, line, token);
  }

  public static readonly JsonSerializerOptions CompactOptions = new(JsonDefaults.Options)
  {
    WriteIndented = false
  };
  public void Dispose()
  {
    if (database is not null)
    {
      database.Dispose();
    }
  }
}
