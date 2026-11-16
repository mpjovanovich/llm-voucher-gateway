using System.Net;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.IntegrationTests.Endpoints;

public class SectionVouchersEndpointTests
{
    private const string IssuancePath = "/admin/vouchers";
    private const string Section = "CSCI-101";
    private const string OtherSection = "CSCI-202";
    private const int Allowance = 100_000;

    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddDays(30);

    private static string ListPath(string section) => $"/admin/sections/{section}/vouchers";

    [Fact]
    public async Task List_ReturnsOnlyTheVouchersIssuedToThatSection()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        List<IssuedVoucherResponse> issued = await IssueAsync(client, Section, count: 3);
        await IssueAsync(client, OtherSection, count: 2);

        using HttpResponseMessage response = await client.GetAsync(ListPath(Section));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<VoucherSummaryResponse>? listed =
            await response.Content.ReadFromJsonAsync<List<VoucherSummaryResponse>>();

        Assert.NotNull(listed);
        Assert.Equal(
            issued.Select(voucher => voucher.Id).Order(),
            listed.Select(voucher => voucher.Id).Order()
        );
        Assert.All(listed, voucher => Assert.Equal(Section, voucher.Section));
    }

    [Fact]
    public async Task List_ReportsStatusAndConsumptionForEachVoucher()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        IssuedVoucherResponse issued = Assert.Single(await IssueAsync(client, Section, count: 1));

        using HttpResponseMessage response = await client.GetAsync(ListPath(Section));

        VoucherSummaryResponse listed = Assert.Single(
            (await response.Content.ReadFromJsonAsync<List<VoucherSummaryResponse>>())!
        );

        Assert.Equal(issued.Id, listed.Id);
        Assert.Equal(issued.Prefix, listed.Prefix);
        Assert.Equal(Allowance, listed.Allowance);
        Assert.Equal(0, listed.Consumed);
        Assert.Equal(nameof(VoucherStatus.Active), listed.Status);
        Assert.Equal(issued.CreatedAt, listed.CreatedAt);
        Assert.Equal(ExpiresAt, listed.ExpiresAt);
        Assert.Null(listed.LastRequestedAt);
    }

    [Fact]
    public async Task List_NeverContainsAVoucherKey()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        List<IssuedVoucherResponse> issued = await IssueAsync(client, Section, count: 2);

        using HttpResponseMessage response = await client.GetAsync(ListPath(Section));
        string body = await response.Content.ReadAsStringAsync();

        // The plaintext key exists only in the issuance response. A listing that could
        // reproduce it would turn read access to the admin API into spending power.
        Assert.All(issued, voucher => Assert.DoesNotContain(voucher.Key, body, StringComparison.Ordinal));
        Assert.DoesNotContain("\"key\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_ForASectionWithNoVouchers_ReturnsAnEmptyList()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        await IssueAsync(client, OtherSection, count: 1);

        using HttpResponseMessage response = await client.GetAsync(ListPath(Section));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<VoucherSummaryResponse>? listed =
            await response.Content.ReadFromJsonAsync<List<VoucherSummaryResponse>>();

        Assert.NotNull(listed);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task List_WithoutAnAdminKey_IsRejected()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ListPath(Section));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<List<IssuedVoucherResponse>> IssueAsync(
        HttpClient client,
        string section,
        int count
    )
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(section, count, Allowance, ExpiresAt)
        );

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>())!;
    }
}
