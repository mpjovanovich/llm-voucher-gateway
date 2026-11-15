using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// The gateway is only useful while it can reach the inference server, so that is what
/// its health means. A gateway that is up in front of a dead vLLM reports unhealthy.
/// </summary>
internal sealed class InferenceHealthCheck(IInferenceClient inferenceClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        // An unreachable server throws instead, which the health check service already
        // reports as unhealthy, so only the "answered, but not ready" case is handled.
        return await inferenceClient.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The inference server reported that it is not healthy.");
    }
}
