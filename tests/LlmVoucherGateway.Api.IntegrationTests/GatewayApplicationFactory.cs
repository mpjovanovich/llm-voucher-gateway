using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LlmVoucherGateway.Api.IntegrationTests;

/// <summary>
/// Hosts the gateway with configuration the tests own. Inheriting the admin key from
/// appsettings.Development.json would let a rejection test pass for the wrong reason and
/// would couple the suite to a developer's local settings.
/// </summary>
internal class GatewayApplicationFactory : WebApplicationFactory<Program>
{
    public const string ConfiguredAdminKey = "integration-test-admin-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{AdminOptions.SectionName}:{nameof(AdminOptions.Keys)}:0"] = ConfiguredAdminKey,

                    // Startup validation requires this. No test reaches the inference
                    // client, so it only has to be a well-formed address.
                    [$"{InferenceOptions.SectionName}:{nameof(InferenceOptions.BaseUrl)}"] =
                        "http://inference.invalid",
                }
            )
        );
    }
}
