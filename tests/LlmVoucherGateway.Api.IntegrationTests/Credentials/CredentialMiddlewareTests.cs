using System.Net.Http.Headers;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

public class CredentialMiddlewareTests
{
    [Fact]
    public async Task ConfiguredAdminKey_ResolvesToAnAdminCredential()
    {
        await using CredentialProbeApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, CredentialProbeApplicationFactory.ProbePath);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CredentialProbeApplicationFactory.ConfiguredAdminKey
        );

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(
            CredentialProbeApplicationFactory.AdminCredential,
            await response.Content.ReadAsStringAsync()
        );
    }

    [Fact]
    public async Task IssuedVoucherKey_ResolvesToAVoucherCredential()
    {
        await using CredentialProbeApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();

        Assert.Equal(
            CredentialProbeApplicationFactory.VoucherCredential,
            await ProbeAsync(factory, $"Bearer {voucherKey}")
        );
    }

    [Fact]
    public async Task RevokedVoucherKey_StillResolvesToAVoucherCredential()
    {
        await using CredentialProbeApplicationFactory factory = new();

        string voucherKey = await factory.IssueVoucherKeyAsync();

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

            Voucher voucher = await dbContext.Vouchers.SingleAsync();
            voucher.Revoke();

            await dbContext.SaveChangesAsync();
        }

        // Resolution answers who the holder is, not whether they may proceed. Collapsing
        // the two here would leave the route unable to say why it refused.
        Assert.Equal(
            CredentialProbeApplicationFactory.VoucherCredential,
            await ProbeAsync(factory, $"Bearer {voucherKey}")
        );
    }

    private static async Task<string> ProbeAsync(
        CredentialProbeApplicationFactory factory,
        string authorizationHeader
    )
    {
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            CredentialProbeApplicationFactory.ProbePath
        );
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        using HttpResponseMessage response = await client.SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-key")]
    [InlineData($"Basic {CredentialProbeApplicationFactory.ConfiguredAdminKey}")]
    [InlineData("Bearer ")]
    public async Task UnrecognizedAuthorizationHeader_ResolvesToNoCredential(string? authorizationHeader)
    {
        await using CredentialProbeApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, CredentialProbeApplicationFactory.ProbePath);

        // Added unvalidated so the malformed values above reach the middleware as written
        // rather than being rejected by HttpClient's header parser.
        if (authorizationHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(
            CredentialProbeApplicationFactory.NoCredential,
            await response.Content.ReadAsStringAsync()
        );
    }
}
