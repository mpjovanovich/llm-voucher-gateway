using System.Net;
using System.Net.Http.Headers;
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
    public async Task ChatCompletions_WhenARequestOvershootsTheAllowance_RecordsAllOfItAndRefusesTheNext()
    {
        await using GatewayApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync(allowance: 50);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", voucherKey);

        // Admitted with 50 left, so it runs to completion and its 60 tokens are kept as
        // evidence of the overshoot rather than capped at the allowance.
        await PostAsync(client, StreamingRequest);

        Assert.Equal(60, await ReadConsumedAsync(factory));

        await PostAsync(client, StreamingRequest, HttpStatusCode.Forbidden);

        Assert.Single(factory.InferenceClient.ForwardedRequests);
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
            CapturedResponses.StreamingBodyWithNoUsage()
        );

        string body = await PostAsync(client, StreamingRequest);

        Assert.Equal(CapturedResponses.StreamingBodyWithNoUsage(), body);
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
            stream ? CapturedResponses.StreamingBodyWithUsage() : CapturedResponses.NonStreamingBody(),
            body
        );
        Assert.Equal(0, await ReadConsumedAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_WhenClientDisconnectsMidStream_RecordsTheLastRunningTotal()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        // The second chunk is never released, so the stream is still open upstream when
        // the client walks away.
        const string FirstChunk =
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}],\"usage\":{\"prompt_tokens\":35,\"total_tokens\":40}}\n\n";
        factory.InferenceClient.StreamingBody = new ReleasedChunkStream(FirstChunk, "data: [DONE]\n\n");

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

        using (HttpRequestMessage request = new(HttpMethod.Post, ChatCompletionsPath))
        {
            request.Content = JsonContent.Create(StreamingRequest);

            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );

            await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
            await body.ReadExactlyAsync(new byte[FirstChunk.Length], timeout.Token);
        }

        // The gateway records once it notices the disconnect, which happens on its own
        // schedule after the client is gone.
        while (await ReadConsumedAsync(factory) != 40)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
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
