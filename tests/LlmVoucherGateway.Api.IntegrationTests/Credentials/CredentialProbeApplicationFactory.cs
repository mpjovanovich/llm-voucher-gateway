using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

/// <summary>
/// Hosts the gateway with a known admin key and a probe route that reports which
/// credential the middleware resolved. The middleware only attaches a credential —
/// enforcing it is a later milestone — so without the probe there is nothing
/// observable to assert over HTTP.
/// </summary>
internal sealed class CredentialProbeApplicationFactory : WebApplicationFactory<Program>
{
    public const string ConfiguredAdminKey = "integration-test-admin-key";
    public const string ProbePath = "/test/caller-credential";

    public const string NoCredential = "none";
    public const string AdminCredential = "admin";
    public const string VoucherCredential = "voucher";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The tests configure their own admin key. Inheriting the one from
        // appsettings.Development.json would let a rejection test pass for the wrong
        // reason, and would couple the suite to a developer's local settings.
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{AdminOptions.SectionName}:{nameof(AdminOptions.Keys)}:0"] = ConfiguredAdminKey,

                    // Startup validation requires this, but no test here reaches the
                    // inference client, so it only has to be a well-formed address.
                    [$"{InferenceOptions.SectionName}:{nameof(InferenceOptions.BaseUrl)}"] =
                        "http://inference.invalid",
                }
            )
        );

        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, CredentialProbe>());
    }

    private sealed class CredentialProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                // The application's own middleware registers first so the probe observes
                // a request that has already passed through CredentialMiddleware.
                next(app);

                app.Map(ProbePath, probe => probe.Run(WriteCredentialAsync));
            };

        private static Task WriteCredentialAsync(HttpContext httpContext)
        {
            CallerCredential? credential = httpContext.Features.Get<CallerCredential>();

            string resolved = credential switch
            {
                null => NoCredential,
                { IsAdmin: true } => AdminCredential,
                _ => VoucherCredential,
            };

            return httpContext.Response.WriteAsync(resolved, httpContext.RequestAborted);
        }
    }
}
