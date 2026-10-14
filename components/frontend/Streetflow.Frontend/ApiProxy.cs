namespace Streetflow.Frontend;

public sealed class ApiProxy
{
  private readonly HttpClient client;
  private readonly FrontendOptions options;

  public ApiProxy(HttpClient client, FrontendOptions options)
  {
    this.client = client;
    this.options = options;
  }

  public Task SimulatorAsync(HttpContext context) => ForwardAsync(context, options.SimulatorBaseUrl, context.Request.Path.Value!);

  public Task LogisticsAsync(HttpContext context)
  {
    string path = "/api/v1" + context.Request.Path.Value!["/melchior-api".Length..];
    return ForwardAsync(context, options.LogisticsBaseUrl, path);
  }

  private async Task ForwardAsync(HttpContext context, string baseUrl, string path)
  {
    string address = baseUrl.TrimEnd('/') + path + context.Request.QueryString;
    using HttpRequestMessage request = new(new HttpMethod(context.Request.Method), address);
    bool hasBody = context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding");
    if (hasBody)
    {
      request.Content = new StreamContent(context.Request.Body);
      if (context.Request.ContentType is not null)
      {
        request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
      }
    }

    try
    {
      using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
      context.Response.StatusCode = (int)response.StatusCode;
      context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
      context.Response.ContentLength = response.Content.Headers.ContentLength;
      if (response.Headers.Location is not null)
      {
        context.Response.Headers.Location = response.Headers.Location.ToString();
      }

      await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (HttpRequestException) when (!context.Response.HasStarted)
    {
      context.Response.StatusCode = StatusCodes.Status502BadGateway;
      context.Response.ContentLength = null;
      await context.Response.WriteAsJsonAsync(new ApiError("service_unavailable", "Cannot reach the component server. Check that it is running and the frontend address is configured."));
    }
  }
}
