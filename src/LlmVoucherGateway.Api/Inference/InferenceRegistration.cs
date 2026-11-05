using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Inference;

internal static class InferenceRegistration
{
    /// <summary>
    /// Registers the typed client that forwards inference requests upstream.
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

        return services;
    }
}
