using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Resolves a request's bearer token into a <see cref="CallerCredential"/> and attaches
/// it to the request. Resolution only: an unrecognized token attaches nothing and the
/// request continues, leaving it to each route to decide what an absent credential means.
/// </summary>
internal sealed class CredentialMiddleware(RequestDelegate next, IOptions<AdminOptions> adminOptions)
{
    private const string BearerPrefix = "Bearer ";

    private readonly AdminOptions _adminOptions = adminOptions.Value;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        string? bearerToken = ReadBearerToken(httpContext.Request);

        if (bearerToken is not null && MatchesAdminKey(bearerToken))
        {
            httpContext.Features.Set(CallerCredential.ForAdmin());
        }

        // The voucher branch — hash the token and look up the voucher — lands in
        // Milestone 6. Until then a non-admin token resolves to no credential.

        await next(httpContext);
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        string? authorizationHeader = request.Headers.Authorization;

        if (string.IsNullOrEmpty(authorizationHeader))
        {
            return null;
        }

        if (!authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string bearerToken = authorizationHeader[BearerPrefix.Length..].Trim();

        return bearerToken.Length == 0 ? null : bearerToken;
    }

    private bool MatchesAdminKey(string bearerToken)
    {
        byte[] bearerTokenBytes = Encoding.UTF8.GetBytes(bearerToken);

        // Every configured key is compared in fixed time, and the loop does not break on a
        // match, so how long this takes reveals nothing about how close a token came.
        bool matched = false;

        foreach (string adminKey in _adminOptions.Keys)
        {
            matched |= CryptographicOperations.FixedTimeEquals(
                bearerTokenBytes,
                Encoding.UTF8.GetBytes(adminKey)
            );
        }

        return matched;
    }
}
