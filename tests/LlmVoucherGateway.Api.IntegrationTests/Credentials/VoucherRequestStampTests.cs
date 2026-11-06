using System.Net;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

/// <summary>
/// The last-requested stamp: when a voucher was presented, whether or not it was then
/// allowed to spend. The rejections are the half worth asserting — without the stamp a
/// revoked voucher still being hammered looks exactly like one quietly abandoned.
/// </summary>
public class VoucherRequestStampTests
{
    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const int Allowance = 100_000;

    // Fixed, so the stamp is asserted exactly rather than within a tolerance.
    private static readonly DateTime RequestedAt = new(2026, 11, 6, 14, 30, 15, DateTimeKind.Utc);

    private static readonly object ChatRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    [Fact]
    public async Task ChatCompletions_WithASpendableVoucher_StampsTheRequestTime()
    {
        await using GatewayApplicationFactory factory = new();
        factory.Clock.UtcNow = RequestedAt;

        using HttpClient client = await factory.CreateVoucherClientAsync();

        await PostAsync(client);

        Assert.Equal(new DateTimeOffset(RequestedAt), await ReadLastRequestedAtAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_WithARevokedVoucher_StampsTheRequestTimeAnyway() =>
        await AssertRejectionIsStampedAsync(
            voucher => voucher.Revoke(),
            HttpStatusCode.Unauthorized
        );

    [Fact]
    public async Task ChatCompletions_WithAnExhaustedVoucher_StampsTheRequestTimeAnyway() =>
        await AssertRejectionIsStampedAsync(
            voucher => voucher.RecordConsumption(Allowance),
            HttpStatusCode.Forbidden
        );

    [Fact]
    public async Task ChatCompletions_OnASecondRequest_ReplacesTheEarlierStamp()
    {
        await using GatewayApplicationFactory factory = new();
        factory.Clock.UtcNow = RequestedAt;

        using HttpClient client = await factory.CreateVoucherClientAsync();

        await PostAsync(client);

        DateTime laterRequestAt = RequestedAt.AddMinutes(5);
        factory.Clock.UtcNow = laterRequestAt;

        await PostAsync(client);

        Assert.Equal(new DateTimeOffset(laterRequestAt), await ReadLastRequestedAtAsync(factory));
    }

    [Fact]
    public async Task ChatCompletions_WhenStampingFails_StillAdmitsTheRequest()
    {
        await using GatewayApplicationFactory factory = new();
        factory.Clock.UtcNow = RequestedAt;

        using HttpClient client = await factory.CreateVoucherClientAsync();

        factory.DatabaseFaults.FailSaves = true;

        // The stamp is diagnostic. A database that cannot take it must not turn a
        // spendable voucher into a rejection, which is the failure this guards.
        await PostAsync(client);

        Assert.Single(factory.InferenceClient.ForwardedRequests);
        Assert.Null(await ReadLastRequestedAtAsync(factory));
        Assert.Contains("Failed to stamp", factory.LogOutput, StringComparison.Ordinal);
    }

    private static async Task AssertRejectionIsStampedAsync(
        Action<Voucher> mutate,
        HttpStatusCode expectedStatus
    )
    {
        await using GatewayApplicationFactory factory = new();
        factory.Clock.UtcNow = RequestedAt;

        using HttpClient client = await factory.CreateVoucherClientAsync();
        await MutateVoucherAsync(factory, mutate);

        await PostAsync(client, expectedStatus);

        Assert.Empty(factory.InferenceClient.ForwardedRequests);
        Assert.Equal(new DateTimeOffset(RequestedAt), await ReadLastRequestedAtAsync(factory));
    }

    private static async Task PostAsync(
        HttpClient client,
        HttpStatusCode expectedStatus = HttpStatusCode.OK
    )
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(ChatCompletionsPath, ChatRequest);

        Assert.Equal(expectedStatus, response.StatusCode);

        // Read to the end, so the request has finished before the stamp is read back.
        await response.Content.ReadAsStringAsync();
    }

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

    private static async Task<DateTimeOffset?> ReadLastRequestedAtAsync(
        GatewayApplicationFactory factory
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        return await dbContext.Vouchers.Select(voucher => voucher.LastRequestedAt).SingleAsync();
    }
}
