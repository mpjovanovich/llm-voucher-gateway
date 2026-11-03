using System.Net;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// Tests of the model list relay.
/// </summary>
public class ModelsEndpointTests
{
    private const string ModelsPath = "/v1/models";

    [Fact]
    public async Task Models_RelaysTheUpstreamResponseVerbatim()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        // Byte-for-byte, as with chat completions: the relay parses nothing on this path
        // either, so a model list the gateway does not recognise still reaches the client.
        Assert.Equal(FakeInferenceClient.ModelListBody, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Models_WithoutAVoucher_IsRejectedAndNotForwarded()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The status a caller saw and whether the request still reached upstream are
        // different questions. This path costs no GPU time, but it does disclose which
        // model is served, so the second one is still worth asking.
        Assert.Equal(0, factory.InferenceClient.ListModelsCalls);
    }
}
