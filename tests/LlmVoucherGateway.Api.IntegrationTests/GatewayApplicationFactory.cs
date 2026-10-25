using System.Net.Http.Headers;
using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.IntegrationTests.Inference;
using LlmVoucherGateway.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace LlmVoucherGateway.Api.IntegrationTests;

/// <summary>
/// Hosts the gateway with configuration the tests own. Inheriting the admin key from
/// appsettings.Development.json would let a rejection test pass for the wrong reason and
/// would couple the suite to a developer's local settings.
/// </summary>
internal class GatewayApplicationFactory : WebApplicationFactory<Program>
{
    public const string ConfiguredAdminKey = "integration-test-admin-key";

    private readonly RecordingSink _recordingSink = new();

    /// <summary>
    /// Stands in for vLLM. Exposed so a test can assert what the gateway forwarded, and
    /// what it did not.
    /// </summary>
    public FakeInferenceClient InferenceClient { get; } = new();

    /// <summary>
    /// A database of this factory's own, so tests neither share state nor inherit the
    /// developer's file. Startup migrates it into existence, and disposing deletes it.
    /// </summary>
    public string DatabasePath { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"llm-voucher-gateway-tests-{Guid.NewGuid():N}.db"
    );

    /// <summary>
    /// A client whose every request carries the configured admin bearer token, for tests
    /// about what an admin route does rather than about who may reach it.
    /// </summary>
    public HttpClient CreateAdminClient()
    {
        HttpClient client = CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ConfiguredAdminKey);

        return client;
    }

    /// <summary>
    /// Everything the application logged during this factory's lifetime.
    /// </summary>
    public string LogOutput => _recordingSink.Output;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The application's Serilog configuration reads sinks from the container, so
        // registering one here adds it beside the console sink rather than replacing the
        // configuration under test.
        builder.ConfigureServices(services =>
            services.AddSingleton<ILogEventSink>(_recordingSink));

        // Replaces the typed client registered in Program.cs, so nothing in the suite
        // depends on a reachable inference server.
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IInferenceClient>(InferenceClient));

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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            DeleteDatabase();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        DeleteDatabase();
    }

    private void DeleteDatabase()
    {
        // Connections are pooled, so the file can still be held open after the host is
        // gone. The -wal and -shm companions only exist in WAL mode; File.Delete is a
        // no-op when they do not.
        SqliteConnection.ClearAllPools();

        File.Delete(DatabasePath);
        File.Delete($"{DatabasePath}-wal");
        File.Delete($"{DatabasePath}-shm");
    }
}
