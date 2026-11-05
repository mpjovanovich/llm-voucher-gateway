using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Endpoints;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Persistence;

namespace LlmVoucherGateway.Api.Configuration;

internal static class OptionsRegistration
{
    /// <summary>
    /// Binds and validates every configuration section the gateway reads.
    /// </summary>
    public static IServiceCollection AddGatewayOptions(this IServiceCollection services)
    {
        // ValidateOnStart turns a missing or malformed section into a startup failure
        // rather than a request-time one, so a misconfigured deployment never serves
        // traffic.
        services
            .AddOptions<AdminOptions>()
            .BindConfiguration(AdminOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<InferenceOptions>()
            .BindConfiguration(InferenceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<VoucherIssuanceOptions>()
            .BindConfiguration(VoucherIssuanceOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
