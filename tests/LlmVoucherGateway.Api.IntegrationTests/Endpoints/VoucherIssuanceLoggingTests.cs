using System.Net.Http.Json;
using LlmVoucherGateway.Api.Endpoints;

namespace LlmVoucherGateway.Api.IntegrationTests.Endpoints;

/// <summary>
/// The audit trail for keys that exist in exactly one response body. Each test pairs a
/// positive assertion with the negative one: a search for an absent secret also passes
/// when nothing was logged at all, which would be the wrong way to go green.
/// </summary>
public class VoucherIssuanceLoggingTests
{
    private const string IssuancePath = "/admin/vouchers";

    private static IssueVouchersRequest ValidRequest =>
        new("CSCI-101", Count: 5, Allowance: 100_000, DateTimeOffset.UtcNow.AddDays(30));

    [Fact]
    public async Task Issue_LogsTheAttempt_WithoutAnyPlaintextKey()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(IssuancePath, ValidRequest);

        List<IssuedVoucherResponse>? issued =
            await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>();

        Assert.NotNull(issued);

        string log = factory.LogOutput;

        Assert.Contains("Voucher issuance requested", log, StringComparison.Ordinal);
        Assert.Contains("CSCI-101", log, StringComparison.Ordinal);

        Assert.All(issued, voucher =>
            Assert.DoesNotContain(voucher.Key, log, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Issue_WithAnUnknownBearer_LogsTheRejectionWithoutTheAttemptedToken()
    {
        const string attemptedToken = "a-token-that-should-never-be-logged";

        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, IssuancePath)
        {
            Content = JsonContent.Create(ValidRequest),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {attemptedToken}");

        using HttpResponseMessage response = await client.SendAsync(request);

        string log = factory.LogOutput;

        Assert.Contains("Rejected POST /admin/vouchers", log, StringComparison.Ordinal);

        // A rejected credential is still a credential: logging it would put a secret in
        // the log the first time somebody pasted the right key into the wrong tool.
        Assert.DoesNotContain(attemptedToken, log, StringComparison.Ordinal);
        Assert.DoesNotContain(GatewayApplicationFactory.ConfiguredAdminKey, log, StringComparison.Ordinal);
    }
}
