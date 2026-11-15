using LlmVoucherGateway.Api.Middleware;
using Serilog;

namespace LlmVoucherGateway.Api.Observability;

internal static class LoggingRegistration
{
    /// <summary>
    /// Makes Serilog the only logging provider, configured from appsettings.
    /// </summary>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        // Serilog replaces the default providers rather than sitting beside them, so each line
        // is written once and in one format. Configuration lives in appsettings.
        builder.Logging.ClearProviders();

        builder.Services.AddSerilog(
            (services, loggerConfiguration) =>
                loggerConfiguration
                    .ReadFrom.Configuration(builder.Configuration)
                    // Lets a sink registered in the container contribute alongside the configured
                    // ones, which is how the tests capture output to assert no secret reaches a log.
                    .ReadFrom.Services(services),
            // Each host keeps its own logger instead of assigning Serilog's static Log.Logger.
            // Nothing here logs through that static API, and sharing it means the first host
            // created in a process owns logging for every host after it — which inside a test
            // run sends one host's output to another host's sinks.
            preserveStaticLogger: true
        );

        return builder;
    }

    /// <summary>
    /// One log line per request, and one correlation id across every line a request
    /// writes. Call it before anything else in the pipeline.
    /// </summary>
    public static WebApplication UseRequestLogging(this WebApplication app)
    {
        // Outermost, so the id is already in place when the request line below is
        // written on the way back out.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseSerilogRequestLogging(options =>
        {
            // The middleware defaults to Serilog's static logger, which this host
            // deliberately leaves unassigned (see above). Without this the request line
            // is written to nowhere, with no error to show for it.
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();

            // RequestPath excludes the query string, and no header is logged, so a
            // credential cannot reach this line.
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms from {ClientAddress}";

            // The real caller once forwarded headers are enabled (production, behind
            // Caddy); otherwise the peer address.
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                diagnosticContext.Set("ClientAddress", httpContext.Connection.RemoteIpAddress);
        });

        return app;
    }
}
