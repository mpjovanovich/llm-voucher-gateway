using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LlmVoucherGateway.Api.IntegrationTests.Persistence;

/// <summary>
/// Makes saves fail on demand, for tests about what the gateway does when the database
/// cannot be written.
/// </summary>
internal sealed class DatabaseFaults : SaveChangesInterceptor
{
    public bool FailSaves { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    ) =>
        FailSaves
            ? throw new DbUpdateException("Simulated database failure.")
            : base.SavingChangesAsync(eventData, result, cancellationToken);
}
