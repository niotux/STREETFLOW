using Streetflow.Frontend;

const long MaximumProjectBytes = 256L * 1024 * 1024;
WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
  Args = args,
  WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});
FrontendOptions options = new()
{
  SimulatorBaseUrl = "http://localhost:8080",
  LogisticsBaseUrl = "http://localhost:8081"
};
builder.Configuration.GetSection("Frontend").Bind(options);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = MaximumProjectBytes);
builder.Services.AddSingleton(options);
builder.Services.AddHttpClient<ApiProxy>(client => client.Timeout = Timeout.InfiniteTimeSpan);
WebApplication app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Text("ok"));
app.MapGet("/legacy-inspector.html", () => Results.Redirect("/balthasar/legacy-inspector.html"));
app.Map("/api/v1/{**path}", (HttpContext context, ApiProxy proxy) => proxy.SimulatorAsync(context));
app.Map("/melchior-api/{**path}", (HttpContext context, ApiProxy proxy) => proxy.LogisticsAsync(context));
app.Map("/casper-api/{**path}", (HttpContext context, ApiProxy proxy) => proxy.CasperAsync(context));
await app.RunAsync();
