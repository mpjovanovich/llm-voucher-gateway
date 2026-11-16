using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.IntegrationTests.Endpoints;

public class VoucherLookupEndpointTests
{
    private const string IssuancePath = "/admin/vouchers";
    private const string Section = "CSCI-101";
    private const int Allowance = 100_000;

    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddDays(30);

    private static readonly object NonStreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    private static string LookupPath(string prefix) => $"/admin/vouchers/{prefix}";

    [Fact]
    public async Task Lookup_ByPrefix_ReturnsThatVoucher()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        List<IssuedVoucherResponse> issued = await IssueAsync(client, count: 3);
        IssuedVoucherResponse wanted = issued[1];

        using HttpResponseMessage response = await client.GetAsync(LookupPath(wanted.Prefix));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        VoucherSummaryResponse? found = await response.Content.ReadFromJsonAsync<VoucherSummaryResponse>();

        Assert.NotNull(found);
        Assert.Equal(wanted.Id, found.Id);
        Assert.Equal(wanted.Prefix, found.Prefix);
        Assert.Equal(Section, found.Section);
        Assert.Equal(Allowance, found.Allowance);
        Assert.Equal(nameof(VoucherStatus.Active), found.Status);
        Assert.Equal(ExpiresAt, found.ExpiresAt);
    }

    [Fact]
    public async Task Lookup_AfterAMeteredRequest_ReportsWhatWasConsumed()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient adminClient = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(adminClient, count: 1));

        using HttpClient voucherClient = factory.CreateClient();
        voucherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Key);

        // The non-streaming capture reports total_tokens 45.
        using HttpResponseMessage chat = await voucherClient.PostAsJsonAsync("/v1/chat/completions", NonStreamingRequest);
        chat.EnsureSuccessStatusCode();

        using HttpResponseMessage response = await adminClient.GetAsync(LookupPath(issued.Prefix));
        VoucherSummaryResponse? found = await response.Content.ReadFromJsonAsync<VoucherSummaryResponse>();

        Assert.NotNull(found);
        Assert.Equal(45, found.Consumed);
        Assert.NotNull(found.LastRequestedAt);
    }

    [Fact]
    public async Task Lookup_NeverContainsTheVoucherKey()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(client, count: 1));

        using HttpResponseMessage response = await client.GetAsync(LookupPath(issued.Prefix));
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(issued.Key, body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"key\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lookup_WithAnUnknownPrefix_ReturnsNotFound()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        await IssueAsync(client, count: 1);

        using HttpResponseMessage response = await client.GetAsync(LookupPath("00000000"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_WithoutAnAdminKey_IsRejected()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(LookupPath("00000000"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<List<IssuedVoucherResponse>> IssueAsync(HttpClient client, int count)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, count, Allowance, ExpiresAt)
        );

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>())!;
    }
}
