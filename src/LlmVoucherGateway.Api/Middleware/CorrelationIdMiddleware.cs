using Serilog.Context;

namespace LlmVoucherGateway.Api.Middleware;

/// <summary>
/// Gives every log line written during a request the same id, and hands that id to the
/// caller, so "it failed around three o'clock" can become one request's lines.
/// </summary>
/// <remarks>
/// The id is the framework's own request identifier rather than a header the caller
/// sends: an id a client chooses is text a client can put in the log.
/// </remarks>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The response header carrying the id, named as OpenAI's own service names it.
    /// </summary>
    public const string HeaderName = "X-Request-Id";

    /// <summary>
    /// The log property carrying the id. The console output template in appsettings.json
    /// refers to it by this name.
    /// </summary>
    public const string PropertyName = "CorrelationId";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        string correlationId = httpContext.TraceIdentifier;

        httpContext.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty(PropertyName, correlationId))
        {
            await next(httpContext);
        }
    }
}
