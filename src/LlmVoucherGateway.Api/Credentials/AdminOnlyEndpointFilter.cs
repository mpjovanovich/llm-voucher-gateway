namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Rejects a request that did not resolve to an administrator. The middleware resolves
/// the credential; this is the decision to act on its absence, which is why it lives on
/// the route rather than in the pipeline — only routes know what an absent credential
/// should cost.
/// </summary>
internal sealed class AdminOnlyEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        CallerCredential? credential = context.HttpContext.Features.Get<CallerCredential>();

        if (credential is not { IsAdmin: true })
        {
            // No body. These routes are reached with curl by an operator, not by a
            // harness, and Milestone 6 is where the error shape gets settled for the
            // rejections that a client has to parse.
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
