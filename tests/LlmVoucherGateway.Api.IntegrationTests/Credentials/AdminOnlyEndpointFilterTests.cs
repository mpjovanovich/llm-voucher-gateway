using System.Net;
using System.Net.Http.Json;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

public class AdminOnlyEndpointFilterTests
{
    private const string IssuancePath = "/admin/vouchers";

    private static readonly IssueVouchersRequest ValidRequest = new(
        "CSCI-101",
        Count: 5,
        Allowance: 100_000,
        DateTimeOffset.UtcNow.AddDays(30)
    );

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer wrong-key")]
    [InlineData($"Basic {GatewayApplicationFactory.ConfiguredAdminKey}")]
    public async Task AdminRoute_WithoutAnAdminCredential_ReturnsUnauthorized(string? authorizationHeader)
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Post, IssuancePath)
        {
            Content = JsonContent.Create(ValidRequest),
        };

        if (authorizationHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The filter runs before the endpoint, so a rejected call mints nothing. Minting
        // and then discarding would burn keys and write rows for a caller who never
        // received them.
        using IServiceScope scope = factory.Services.CreateScope();
        VoucherDbContext dbContext = scope.ServiceProvider.GetRequiredService<VoucherDbContext>();

        Assert.False(await dbContext.Vouchers.AnyAsync());
    }

    [Fact]
    public async Task AdminRoute_WithAConfiguredAdminKey_IsReached()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(IssuancePath, ValidRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
