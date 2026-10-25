using System.Net;
using System.Net.Http.Json;
using System.Text;
using LlmVoucherGateway.Api.Endpoints;

namespace LlmVoucherGateway.Api.IntegrationTests.Endpoints;

public class VoucherIssuanceEndpointTests
{
    private const string IssuancePath = "/admin/vouchers";
    private const string Section = "CSCI-101";
    private const int Allowance = 100_000;

    private static readonly DateTimeOffset ExpiresAt = DateTimeOffset.UtcNow.AddDays(30);

    [Fact]
    public async Task Issue_WithValidRequest_ReturnsTheRequestedNumberOfVouchers()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, Count: 20, Allowance, ExpiresAt)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<IssuedVoucherResponse>? issued =
            await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>();

        Assert.NotNull(issued);
        Assert.Equal(20, issued.Count);
    }

    [Fact]
    public async Task Issue_WithValidRequest_StampsTheBatchPolicyOnEveryVoucher()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, Count: 3, Allowance, ExpiresAt)
        );

        List<IssuedVoucherResponse>? issued =
            await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>();

        Assert.NotNull(issued);
        Assert.All(issued, voucher =>
        {
            Assert.Equal(Section, voucher.Section);
            Assert.Equal(Allowance, voucher.Allowance);
            Assert.Equal(0, voucher.Consumed);
            Assert.Equal(nameof(Domain.Vouchers.VoucherStatus.Active), voucher.Status);
            Assert.Equal(ExpiresAt, voucher.ExpiresAt);

            // The prefix is what survives in the database, so it has to identify the key
            // it was taken from.
            Assert.Equal(voucher.Key[..voucher.Prefix.Length], voucher.Prefix);
        });
    }

    [Fact]
    public async Task Issue_WithValidRequest_GivesEveryVoucherItsOwnIdAndKey()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, Count: 20, Allowance, ExpiresAt)
        );

        List<IssuedVoucherResponse>? issued =
            await response.Content.ReadFromJsonAsync<List<IssuedVoucherResponse>>();

        Assert.NotNull(issued);
        Assert.Equal(20, issued.Select(voucher => voucher.Id).Distinct().Count());
        Assert.Equal(20, issued.Select(voucher => voucher.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Issue_WithNonPositiveCount_ReturnsBadRequest(int count)
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, count, Allowance, ExpiresAt)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Names the offending field, so the test cannot pass on a 400 raised by
        // something other than the count check.
        Assert.Contains("count", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_WithNonPositiveAllowance_ReturnsBadRequest()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, Count: 1, Allowance: 0, ExpiresAt)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Issue_WithExpiryInThePast_ReturnsBadRequest()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            IssuancePath,
            new IssueVouchersRequest(Section, Count: 1, Allowance, DateTimeOffset.UtcNow.AddDays(-1))
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Issue_WithMalformedJson_ReturnsBadRequest()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        // Model binding fails before the endpoint is entered. Without a case for it in
        // the exception handler this arrives as a 500.
        using StringContent malformedJson = new(
            """{"count":5,"allowance":100,""",
            Encoding.UTF8,
            "application/json"
        );

        using HttpResponseMessage response = await client.PostAsync(IssuancePath, malformedJson);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
