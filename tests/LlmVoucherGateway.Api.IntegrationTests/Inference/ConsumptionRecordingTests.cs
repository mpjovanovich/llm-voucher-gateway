using System.Net;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// Known-token requests against the captures, asserting the decrement in the database.
/// The non-streaming capture reports <c>total_tokens</c> 45 and the streamed one ends at 60.
/// </summary>
public class ConsumptionRecordingTests
{
    private const string ChatCompletionsPath = "/v1/chat/completions";

    private static readonly object NonStreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    private static readonly object StreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
        stream = true,
    };

    [Fact]
    public async Task ChatCompletions_NonStreaming_RecordsTotalTokens()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        await PostAsync(client, NonStreamingRequest);

        Assert.Equal(45, await ReadConsumedAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_Streaming_RecordsFinalRunningTotal()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        await PostAsync(client, StreamingRequest);

        Assert.Equal(60, await ReadConsumedAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_AcrossSeveralTurns_Accumulates()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        await PostAsync(client, StreamingRequest);
        await PostAsync(client, NonStreamingRequest);
        await PostAsync(client, StreamingRequest);

        Assert.Equal(60 + 45 + 60, await ReadConsumedAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_WhenUpstreamRejectsTheRequest_RelaysTheErrorAndRecordsNothing()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        factory.InferenceClient.ChatCompletionResponse = new ScriptedResponse(
            HttpStatusCode.NotFound,
            "application/json",
            CapturedResponses.UnknownModelErrorBody()
        );

        await PostAsync(client, NonStreamingRequest, HttpStatusCode.NotFound);

        Assert.Equal(0, await ReadConsumedAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_WhenStreamCarriesNoUsage_SucceedsAndRecordsNothing()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        // A server started without --enable-force-include-usage. The request still has to
        // succeed; it simply goes unmetered.
        factory.InferenceClient.ChatCompletionResponse = new ScriptedResponse(
            HttpStatusCode.OK,
            "text/event-stream",
            CapturedResponses.UnforcedStreamingBody()
        );

        string body = await PostAsync(client, StreamingRequest);

        Assert.Equal(CapturedResponses.UnforcedStreamingBody(), body);
        Assert.Equal(0, await ReadConsumedAsync(factory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatCompletions_WhenRecordingFails_StillDeliversTheWholeResponse(bool stream)
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        factory.DatabaseFaults.FailSaves = true;

        string body = await PostAsync(client, stream ? StreamingRequest : NonStreamingRequest);

        Assert.Equal(
            stream ? CapturedResponses.StreamingBody() : CapturedResponses.NonStreamingBody(),
            body
        );
        Assert.Equal(0, await ReadConsumedAsync(factory));
    }

    private static async Task<string> PostAsync(
        HttpClient client,
        object request,
        HttpStatusCode expectedStatus = HttpStatusCode.OK
    )
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(ChatCompletionsPath, request);

        Assert.Equal(expectedStatus, response.StatusCode);

        // Read to the end, so the request has finished, and been recorded, before returning.
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<int> ReadConsumedAsync(GatewayApplicationFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        return await dbContext.Vouchers.Select(voucher => voucher.Consumed).SingleAsync();
    }
}
