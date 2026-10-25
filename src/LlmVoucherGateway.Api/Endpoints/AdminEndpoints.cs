using LlmVoucherGateway.Api.Credentials;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// The administrative surface. Every route is mapped into the group below, so the
/// <see cref="AdminOnlyEndpointFilter"/> covers each one without being repeated — and a
/// route added here cannot be left unguarded by forgetting it.
/// </summary>
internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder adminEndpoints = endpoints
            .MapGroup("/admin")
            .AddEndpointFilter<AdminOnlyEndpointFilter>();

        adminEndpoints.MapVoucherIssuance();
    }
}
