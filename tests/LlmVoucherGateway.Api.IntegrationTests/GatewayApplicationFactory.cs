using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Persistence;
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

    /// <summary>
    /// A database of this factory's own, so tests neither share state nor inherit the
    /// developer's file. Nothing opens it yet; deleting it on dispose arrives with the
    /// first test that does.
    /// </summary>
    public string DatabasePath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"llm-voucher-gateway-tests-{Guid.NewGuid():N}.db"
    );

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

                    [$"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Path)}"] = DatabasePath,
                }
            )
        );
    }
}
