namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Rejects a request that did not resolve to an administrator. The middleware resolves
/// the credential; this is the decision to act on its absence, which is why it lives on
/// the route rather than in the pipeline — only routes know what an absent credential
/// should cost.
/// </summary>
internal sealed class AdminOnlyEndpointFilter(ILogger<AdminOnlyEndpointFilter> logger)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        CallerCredential? credential = context.HttpContext.Features.Get<CallerCredential>();

        if (credential is not { IsAdmin: true })
        {
            // Warning rather than Information: a rejected admin call is either an
            // operator with the wrong key or somebody probing for one, and the second
            // is the thing a post-mortem goes looking for. The request never reaches
            // the endpoint, so this line is the only record that it happened.
            logger.LogWarning(
                "Rejected {Method} {Path} from {SourceIpAddress}: {Reason}.",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress,
                credential is null ? "no admin credential" : "credential is not an administrator"
            );

            // No body. These routes are reached with curl by an operator, not by a
            // harness, and Milestone 6 is where the error shape gets settled for the
            // rejections that a client has to parse.
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
