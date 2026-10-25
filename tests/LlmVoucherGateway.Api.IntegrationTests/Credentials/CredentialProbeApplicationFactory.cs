using LlmVoucherGateway.Api.Credentials;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Credentials;

/// <summary>
/// Adds a route at the end of the pipeline that reports which credential the middleware
/// resolved. The middleware only attaches a credential — enforcing it is a later
/// milestone — so without the probe there is nothing observable to assert over HTTP.
/// </summary>
internal sealed class CredentialProbeApplicationFactory : GatewayApplicationFactory
{
    public const string ProbePath = "/test/caller-credential";

    public const string NoCredential = "none";
    public const string AdminCredential = "admin";
    public const string VoucherCredential = "voucher";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

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
