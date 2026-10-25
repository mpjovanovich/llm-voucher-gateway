using System.Security.Cryptography;
using System.Text;
using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Middleware;

/// <summary>
/// Resolves a request's bearer token into a <see cref="CallerCredential"/> and attaches
/// it to the request. Resolution only: an unrecognized token attaches nothing and the
/// request continues, leaving it to each route to decide what an absent credential means.
/// </summary>
internal sealed class CredentialMiddleware(RequestDelegate next, IOptions<AdminOptions> adminOptions)
{
    private const string BearerPrefix = "Bearer ";

    private readonly AdminOptions _adminOptions = adminOptions.Value;

    // VoucherDbContext arrives as a parameter rather than through the constructor: this
    // middleware is built once and reused, and a scoped context injected there would be
    // held for the life of the process and shared across concurrent requests.
    public async Task InvokeAsync(HttpContext httpContext, VoucherDbContext dbContext)
    {
        string? bearerToken = ReadBearerToken(httpContext.Request);

        if (bearerToken is not null)
        {
            if (MatchesAdminKey(bearerToken))
            {
                httpContext.Features.Set(CallerCredential.ForAdmin());
            }
            else
            {
                Voucher? voucher = await FindVoucherAsync(
                    dbContext,
                    bearerToken,
                    httpContext.RequestAborted
                );

                // Attached whatever its state: a revoked, expired or exhausted voucher
                // still identifies its holder, and turning them away is the route's
                // decision rather than this one.
                if (voucher is not null)
                {
                    httpContext.Features.Set(CallerCredential.ForVoucher(voucher));
                }
            }
        }

        await next(httpContext);
    }

    // Straight to the database on every request, with no cache. A lookup on an indexed
    // hash costs nothing next to a request that occupies a GPU for seconds, and caching
    // would buy a window where a revoked voucher still works.
    private static Task<Voucher?> FindVoucherAsync(
        VoucherDbContext dbContext,
        string bearerToken,
        CancellationToken cancellationToken
    )
    {
        string hash = VoucherKey.ComputeHash(bearerToken);

        return dbContext.Vouchers.SingleOrDefaultAsync(
            voucher => voucher.Key.Hash == hash,
            cancellationToken
        );
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
