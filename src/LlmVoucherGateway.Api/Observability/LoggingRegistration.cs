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
}
