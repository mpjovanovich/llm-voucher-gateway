using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Errors;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

public class VoucherRequiredEndpointFilterTests
{
    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const int Allowance = 100_000;

    private static readonly object ChatRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    [Fact]
    public async Task ChatCompletions_WithASpendableVoucher_ReachesTheInferenceServer()
    {
        await using GatewayApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();

        ChatResponse response = await PostAsync(factory, $"Bearer {voucherKey}");

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Single(factory.InferenceClient.ForwardedRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer not-a-real-voucher")]
    [InlineData($"Bearer {GatewayApplicationFactory.ConfiguredAdminKey}")]
    public async Task ChatCompletions_WithoutAVoucher_IsUnauthorizedAndNeverForwarded(
        string? authorizationHeader
    )
    {
        await using GatewayApplicationFactory factory = new();

        ChatResponse response = await PostAsync(factory, authorizationHeader);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        AssertOpenAiError(response, OpenAiErrorResults.InvalidRequestErrorType);

        // The GPU is the expensive resource. A rejected request must not occupy it, and
        // asserting the status alone would not show that.
        Assert.Empty(factory.InferenceClient.ForwardedRequests);
    }

    [Fact]
    public async Task ChatCompletions_WithARevokedVoucher_IsUnauthorized()
    {
        await using GatewayApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();
        await MutateVoucherAsync(factory, voucher => voucher.Revoke());

        ChatResponse response = await PostAsync(factory, $"Bearer {voucherKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Contains("revoked", ErrorMessage(response), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(factory.InferenceClient.ForwardedRequests);
    }

    [Fact]
    public async Task ChatCompletions_WithAnExpiredVoucher_IsUnauthorized()
    {
        await using GatewayApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();

        // Expiry is read from the clock at request time, so the test moves the clock
        // rather than waiting for the voucher to age.
        factory.Clock.UtcNow = factory.Clock.UtcNow.AddDays(31);

        ChatResponse response = await PostAsync(factory, $"Bearer {voucherKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
        Assert.Contains("expired", ErrorMessage(response), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(factory.InferenceClient.ForwardedRequests);
    }

    [Fact]
    public async Task ChatCompletions_WithAnExhaustedVoucher_IsPaymentRequired()
    {
        await using GatewayApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();
        await MutateVoucherAsync(factory, voucher => voucher.RecordConsumption(Allowance));

        ChatResponse response = await PostAsync(factory, $"Bearer {voucherKey}");

        // 402, not 401: the credential is fine and the budget is not, which is what tells
        // a student "used it up" rather than "wrong key".
        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
        AssertOpenAiError(response, OpenAiErrorResults.InsufficientQuotaErrorType);
        Assert.Empty(factory.InferenceClient.ForwardedRequests);
    }

    /// <summary>
    /// The response, read in full before the client is disposed — the body cannot be
    /// read afterwards, because disposing the client tears down the handler holding it.
    /// </summary>
    private sealed record ChatResponse(HttpStatusCode Status, string? ContentType, string Body);

    private static async Task<ChatResponse> PostAsync(
        GatewayApplicationFactory factory,
        string? authorizationHeader
    )
    {
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, ChatCompletionsPath)
        {
            Content = JsonContent.Create(ChatRequest),
        };

        if (authorizationHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        return new ChatResponse(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync()
        );
    }

    /// <summary>
    /// Changes voucher state the way Milestones 7 and 8 will, since neither the revoke
    /// endpoint nor consumption recording exists yet.
    /// </summary>
    private static async Task MutateVoucherAsync(
        GatewayApplicationFactory factory,
        Action<Voucher> mutate
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        Voucher voucher = await dbContext.Vouchers.SingleAsync();
        mutate(voucher);

        await dbContext.SaveChangesAsync();
    }

    private static void AssertOpenAiError(ChatResponse response, string expectedType)
    {
        Assert.Equal("application/json", response.ContentType);

        using var body = JsonDocument.Parse(response.Body);

        JsonElement error = body.RootElement.GetProperty("error");

        Assert.Equal(expectedType, error.GetProperty("type").GetString());
        Assert.Equal((int)response.Status, error.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    private static string ErrorMessage(ChatResponse response)
    {
        using var body = JsonDocument.Parse(response.Body);

        return body.RootElement.GetProperty("error").GetProperty("message").GetString() ?? string.Empty;
    }
}
