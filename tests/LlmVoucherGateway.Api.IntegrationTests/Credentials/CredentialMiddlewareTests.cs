using System.Net.Http.Headers;

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
