using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Endpoints;

public class VoucherRevocationEndpointTests
{
    private const string IssuancePath = "/admin/vouchers";
    private const string Section = "CSCI-101";
    private const int Allowance = 100_000;
    private const string Reason = "Student withdrew from the course.";

    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddDays(30);

    private static readonly object NonStreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    private static string RevokePath(string prefix) => $"/admin/vouchers/{prefix}/revoke";

    private static string LookupPath(string prefix) => $"/admin/vouchers/{prefix}";

    [Fact]
    public async Task Revoke_ByPrefix_RevokesThatVoucherAndRecordsTheReason()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        List<IssuedVoucherResponse> issued = await IssueAsync(client, count: 2);
        IssuedVoucherResponse wanted = issued[0];
        IssuedVoucherResponse other = issued[1];

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RevokePath(wanted.Prefix),
            new RevokeVoucherRequest($"  {Reason}  ")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(wanted.Key, body, StringComparison.Ordinal);

        VoucherSummaryResponse? revoked = JsonSerializer.Deserialize<VoucherSummaryResponse>(
            body,
            JsonSerializerOptions.Web
        );

        Assert.NotNull(revoked);
        Assert.Equal(wanted.Id, revoked.Id);
        Assert.Equal(wanted.Prefix, revoked.Prefix);
        Assert.Equal(nameof(VoucherStatus.Revoked), revoked.Status);
        Assert.Equal(Reason, revoked.RevocationReason);

        VoucherSummaryResponse? untouched = await LookupAsync(client, other.Prefix);

        Assert.Equal(nameof(VoucherStatus.Active), untouched.Status);
        Assert.Null(untouched.RevocationReason);
    }

    [Fact]
    public async Task Revoke_ByPrefix_TakesEffectOnTheNextRequest()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient adminClient = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(adminClient, count: 1));

        using HttpResponseMessage revocation = await adminClient.PostAsJsonAsync(
            RevokePath(issued.Prefix),
            new RevokeVoucherRequest(Reason)
        );
        revocation.EnsureSuccessStatusCode();

        using HttpClient voucherClient = factory.CreateClient();
        voucherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Key);

        using HttpResponseMessage chat = await voucherClient.PostAsJsonAsync(
            "/v1/chat/completions",
            NonStreamingRequest
        );
        string body = await chat.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, chat.StatusCode);
        Assert.Contains("revoked", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(factory.InferenceClient.ForwardedRequests);
    }

    [Fact]
    public async Task Revoke_Again_KeepsTheFirstReason()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(client, count: 1));

        using HttpResponseMessage first = await client.PostAsJsonAsync(
            RevokePath(issued.Prefix),
            new RevokeVoucherRequest(Reason)
        );
        first.EnsureSuccessStatusCode();

        using HttpResponseMessage second = await client.PostAsJsonAsync(
            RevokePath(issued.Prefix),
            new RevokeVoucherRequest("Posted the key publicly.")
        );

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        VoucherSummaryResponse? revoked = await second.Content.ReadFromJsonAsync<VoucherSummaryResponse>();

        Assert.NotNull(revoked);
        Assert.Equal(Reason, revoked.RevocationReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Revoke_WithoutAReason_IsBadRequestAndLeavesTheVoucherActive(string? reason)
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(client, count: 1));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RevokePath(issued.Prefix),
            new RevokeVoucherRequest(reason)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        VoucherSummaryResponse unchanged = await LookupAsync(client, issued.Prefix);

        Assert.Equal(nameof(VoucherStatus.Active), unchanged.Status);
        Assert.Null(unchanged.RevocationReason);
    }

    [Fact]
    public async Task Revoke_WithAnUnknownPrefix_ReturnsNotFound()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        await IssueAsync(client, count: 1);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RevokePath("00000000"),
            new RevokeVoucherRequest(Reason)
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_WithASharedPrefix_ConflictsAndRevokesNothing()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        List<IssuedVoucherResponse> issued = await IssueAsync(client, count: 2);
        await SharePrefixAsync(factory, issued[1].Prefix, issued[0].Prefix);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RevokePath(issued[0].Prefix),
            new RevokeVoucherRequest(Reason)
        );
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Nothing was revoked", body, StringComparison.Ordinal);

        List<VoucherSummaryResponse> listed = await ListSectionAsync(client);

        Assert.Equal(2, listed.Count);
        Assert.All(listed, voucher => Assert.Equal(nameof(VoucherStatus.Active), voucher.Status));
        Assert.All(listed, voucher => Assert.Null(voucher.RevocationReason));
    }

    [Fact]
    public async Task Revoke_WithoutAnAdminKey_IsRejected()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            RevokePath("00000000"),
            new RevokeVoucherRequest(Reason)
        );

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

    private static async Task<VoucherSummaryResponse> LookupAsync(HttpClient client, string prefix)
    {
        using HttpResponseMessage response = await client.GetAsync(LookupPath(prefix));
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<VoucherSummaryResponse>())!;
    }

    private static async Task<List<VoucherSummaryResponse>> ListSectionAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync($"/admin/sections/{Section}/vouchers");
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<VoucherSummaryResponse>>())!;
    }

    // A collision is not something issuance can produce on demand. The row is updated
    // directly so the refusal can be proven against two real vouchers that share a prefix.
    private static async Task SharePrefixAsync(
        GatewayApplicationFactory factory,
        string fromPrefix,
        string toPrefix
    )
    {
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        int updated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Vouchers SET Key_Prefix = {toPrefix} WHERE Key_Prefix = {fromPrefix}"
        );

        Assert.Equal(1, updated);
    }
}
