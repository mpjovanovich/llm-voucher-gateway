using System.Net;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// The gateway's health is whether it can reach the inference server. What these cannot
/// prove is the real client's probe of vLLM, which only shows up against a live server.
/// </summary>
public class HealthEndpointTests
{
    private const string HealthPath = "/health";

    [Fact]
    public async Task Health_WhenTheUpstreamIsHealthy_IsOkWithoutACredential()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(HealthPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_WhenTheUpstreamCannotBeReached_IsServiceUnavailable()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();
        factory.InferenceClient.Fault = new HttpRequestException("Connection refused");

        using HttpResponseMessage response = await client.GetAsync(HealthPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Health_WhenTheUpstreamAnswersUnhealthy_IsServiceUnavailable()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();
        factory.InferenceClient.Healthy = false;

        using HttpResponseMessage response = await client.GetAsync(HealthPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Health_RecoversWhenTheUpstreamComesBack()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        factory.InferenceClient.Fault = new HttpRequestException("Connection refused");
        using HttpResponseMessage down = await client.GetAsync(HealthPath);

        factory.InferenceClient.Fault = null;
        using HttpResponseMessage up = await client.GetAsync(HealthPath);

        // Nothing is cached between probes, so a restarted vLLM shows up on the next one.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
    }
}
