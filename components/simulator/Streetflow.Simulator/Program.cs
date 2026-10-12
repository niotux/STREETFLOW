using System.Text.Json;
using System.Text.Json.Serialization;
using Streetflow.Simulator.Api;
using Streetflow.Simulator.Persistence;
using Streetflow.Simulator.Runtime;
using Streetflow.Simulator.Scenarios;
using Streetflow.Simulator.City;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
SimulatorOptions options = new()
{
  ScenarioRoot = "scenarios",
  DataRoot = ".data/simulator",
  SumoBinary = "sumo",
  NetconvertBinary = "netconvert",
  DuarouterBinary = "duarouter",
  EngineTimeoutSeconds = 15,
  ObservationRetention = 2000,
  MaximumProjectBytes = 256L * 1024 * 1024,
  MaximumProjectFiles = 4096,
  MaximumLiveVehicles = 5000,
  MaximumPendingEntries = 512,
  GenerationBatchSize = 64,
  MaximumBacklogSeconds = 300
};
builder.Configuration.GetSection("Simulator").Bind(options);
string workspace = FindWorkspace(builder.Environment.ContentRootPath);
options.ScenarioRoot = Path.GetFullPath(options.ScenarioRoot, workspace);
options.DataRoot = Path.GetFullPath(options.DataRoot, workspace);
if (HasArgument(args, "prepare-scenarios"))
{
  await ScenarioPreparation.PrepareAsync(workspace, options, CancellationToken.None);
  return;
}

if (HasArgument(args, "import-osm"))
{
  string inputPath = GetArgument(args, "--input");
  string outputPath = Path.GetFullPath(GetArgument(args, "--output"));
  string scenarioId = GetArgument(args, "--id");
  string scenarioName = scenarioId + " with synthetic demand";
  await ScenarioPreparation.ImportOsmAsync(inputPath, outputPath, scenarioId, scenarioName, options, CancellationToken.None);
  return;
}

RegisterServices(builder, options);
WebApplication app = builder.Build();
ConfigurePipeline(app);
await app.Services.GetRequiredService<RunCoordinator>().InitializeAsync(CancellationToken.None);
_ = app.Services.GetRequiredService<ProjectCatalog>();
await app.Services.GetRequiredService<CityRunCoordinator>().InitializeAsync();
await app.RunAsync();
static string FindWorkspace(string startPath)
{
  DirectoryInfo? directory = new(startPath);
  while (directory is not null)
  {
    string solutionPath = Path.Combine(directory.FullName, "Streetflow.slnx");
    if (File.Exists(solutionPath))
    {
      return directory.FullName;
    }

    directory = directory.Parent;
  }

  return startPath;
}

static bool HasArgument(string[] arguments, string name)
{
  foreach (string argument in arguments)
  {
    if (string.Equals(argument, name, StringComparison.Ordinal))
    {
      return true;
    }
  }

  return false;
}

static string GetArgument(string[] arguments, string name)
{
  for (int index = 0; index < arguments.Length; index++)
  {
    if (arguments[index] == name && index + 1 < arguments.Length)
    {
      return arguments[index + 1];
    }
  }

  throw new ArgumentException($"Missing {name}");
}

static void RegisterServices(WebApplicationBuilder builder, SimulatorOptions options)
{
  builder.Services.AddSingleton(options);
  builder.Services.AddSingleton<ScenarioCatalog>();
  builder.Services.AddSingleton<ProjectCatalog>();
  builder.Services.AddSingleton<RunStore>();
  builder.Services.AddSingleton<RunCoordinator>();
  builder.Services.AddSingleton<EngineOwnership>();
  builder.Services.AddSingleton<CityRunStorage>();
  builder.Services.AddSingleton<CityRunCoordinator>();
  builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<RunCoordinator>());
  builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<CityRunCoordinator>());
  builder.Services.ConfigureHttpJsonOptions(json =>
  {
    json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
  });
  builder.Services.AddOpenApi();
  builder.Services.Configure<HostOptions>(host =>
  {
    host.ShutdownTimeout = TimeSpan.FromSeconds(30);
  });
}

static void ConfigurePipeline(WebApplication app)
{
  app.Use(async (context, next) =>
  {
    try
    {
      await next(context);
    }
    catch (ApiException exception)
    {
      context.Response.StatusCode = exception.StatusCode;
      await context.Response.WriteAsJsonAsync(new ApiError(exception.Code, exception.Message));
    }
    catch (BadHttpRequestException exception)
    {
      context.Response.StatusCode = exception.StatusCode;
      await context.Response.WriteAsJsonAsync(new ApiError("invalid_request", exception.Message));
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
      app.Logger.LogError(exception, "API request failed");
      context.Response.StatusCode = StatusCodes.Status500InternalServerError;
      await context.Response.WriteAsJsonAsync(new ApiError("internal_error", "The request failed; inspect the service logs"));
    }
  });
  app.UseDefaultFiles();
  app.UseStaticFiles();
  app.MapOpenApi();
  app.MapSimulatorApi();
  app.MapProjectApi();
  app.MapCityRunApi();
}

public partial class Program;
