using System.Text.Json;

namespace Streetflow.Logistics;

public sealed class ConnectionStore
{
  private readonly string file;
  private readonly SemaphoreSlim gate = new(1, 1);
  private TlcConnection connection;

  public ConnectionStore(LogisticsOptions options)
  {
    file = options.ConnectionFile;
    connection = Normalize(new TlcConnection(options.TlcApiBaseUrl));
    if (File.Exists(file))
    {
      TlcConnection? saved = JsonSerializer.Deserialize<TlcConnection>(File.ReadAllText(file), JsonDefaults.Options);
      if (saved is null)
      {
        throw new InvalidOperationException("The saved tlcapi connection is empty");
      }

      connection = Normalize(saved);
    }
  }

  public TlcConnection Get() => Volatile.Read(ref connection);

  public async Task<TlcConnection> SaveAsync(TlcConnection request, CancellationToken token)
  {
    TlcConnection next = Normalize(request);
    await gate.WaitAsync(token);
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      string temporary = file + ".tmp";
      await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, JsonDefaults.Options), token);
      File.Move(temporary, file, true);
      Volatile.Write(ref connection, next);
      return next;
    }
    finally
    {
      gate.Release();
    }
  }

  private static TlcConnection Normalize(TlcConnection request)
  {
    bool valid = Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out Uri? uri);
    if (!valid || uri is null)
    {
      throw new ApiException(400, "invalid_connection", "base_url must be an absolute HTTP or HTTPS server URL, including its port");
    }

    bool validScheme = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    bool serverOnly = uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;
    if (!validScheme || !serverOnly)
    {
      throw new ApiException(400, "invalid_connection", "Use a server URL such as http://localhost:8080, without an API path, query or credentials");
    }

    return new TlcConnection(uri.GetLeftPart(UriPartial.Authority));
  }
}
