using System.Text.Json;
using System.Text.Json.Serialization;
using Streetflow.Logistics;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
LogisticsOptions options = new()
{
  TacoApiBaseUrl = "http://localhost:8080",
  ConnectionFile = ".data/logistics/connection.json",
  RequestTimeoutSeconds = 15,
  DiscoveryIntervalMilliseconds = 1500
};
builder.Configuration.GetSection("Logistics").Bind(options);
options.ConnectionFile = Path.GetFullPath(options.ConnectionFile, builder.Environment.ContentRootPath);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ConnectionStore>();
builder.Services.AddSingleton<TacoMapCache>();
builder.Services.AddHostedService<WorldDiscoveryService>();
builder.Services.AddHttpClient<TacoApiClient>(client => client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
builder.Services.ConfigureHttpJsonOptions(json =>
{
  json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
  json.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
builder.Services.AddOpenApi();
WebApplication app = builder.Build();
_ = app.Services.GetRequiredService<ConnectionStore>();
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
    app.Logger.LogError(exception, "Melchior request failed");
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ApiError("internal_error", "The request failed; inspect the Melchior logs"));
  }
});
app.MapOpenApi();
app.MapLogisticsApi();
await app.RunAsync();
