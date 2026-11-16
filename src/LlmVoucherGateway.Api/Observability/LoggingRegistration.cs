using LlmVoucherGateway.Api.Credentials;
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
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0} ms from {ClientAddress} as {Caller}";

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                // The real caller once forwarded headers are enabled (production, behind
                // Caddy); otherwise the peer address.
                diagnosticContext.Set("ClientAddress", httpContext.Connection.RemoteIpAddress);
                diagnosticContext.Set("Caller", DescribeCaller(httpContext));
            };
        });

        return app;
    }

    /// <summary>
    /// Which voucher a request was made with, for grouping a caller's requests over time.
    /// </summary>
    /// <remarks>
    /// The client address is not a usable handle for every request. Traffic from the
    /// Streamlit UI reaches the gateway from the web container, so every browser user
    /// shares one address; only a harness calling through Caddy carries its own. The
    /// voucher prefix is the handle that works on both paths, and it is what revocation
    /// acts on.
    /// <para>
    /// The prefix is the first twelve characters of the key, so this does disclose part of
    /// a secret. Two things make that acceptable: the remaining characters leave the key
    /// unguessable, and the log sink writes into the same directory as the database, so a
    /// reader of these files can already read the voucher table itself.
    /// </para>
    /// <para>
    /// A voucher resolves whatever its state, so a revoked, expired or exhausted one is
    /// still named here beside the status it was rejected with. An absent or unrecognized
    /// token resolves to no credential at all, and both collapse into "unknown".
    /// </para>
    /// </remarks>
    private static string DescribeCaller(HttpContext httpContext) =>
        httpContext.Features.Get<CallerCredential>() switch
        {
            { IsAdmin: true } => "admin",
            { Voucher: { } voucher } => voucher.Key.Prefix,
            _ => "unknown",
        };
}
