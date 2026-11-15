using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Inference;

internal static class InferenceRegistration
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registers the typed client that forwards inference requests upstream, and the health check
    /// that probes the same server.
    /// </summary>
    public static IServiceCollection AddInference(this IServiceCollection services)
    {
        services.AddHttpClient<IInferenceClient, VllmInferenceClient>((serviceProvider, httpClient) =>
        {
            InferenceOptions options = serviceProvider
                .GetRequiredService<IOptions<InferenceOptions>>().Value;

            httpClient.BaseAddress = new Uri(options.BaseUrl);

            // HttpClient's 100-second default would cancel long generations mid-stream and
            // surface as a TaskCanceledException that reads like a network fault. Request
            // lifetime is bounded by the caller's own cancellation instead.
            httpClient.Timeout = Timeout.InfiniteTimeSpan;
        });

        // The client above never times out, so the probe carries its own limit: a vLLM
        // that accepts the connection and then hangs has to read as down, not as a
        // health request that never returns.
        services
            .AddHealthChecks()
            .AddCheck<InferenceHealthCheck>("inference", timeout: HealthCheckTimeout);

        return services;
    }
}
