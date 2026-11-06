using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Consumption;

/// <summary>
/// Charges a voucher for the tokens a relayed response reported.
/// </summary>
/// <remarks>
/// Runs after the response, against the voucher the filter already checked: the budget is
/// soft, so a request admitted under it is allowed to finish over it.
/// </remarks>
internal sealed class ConsumptionRecorder(
    VoucherDbContext dbContext,
    ILogger<ConsumptionRecorder> logger
)
{
    public async Task RecordAsync(
        Voucher voucher,
        IUsageObserver usageObserver,
        CancellationToken cancellationToken
    )
    {
        if (usageObserver.ReadTotalTokens() is not int totalTokens)
        {
            return;
        }

        try
        {
            voucher.RecordConsumption(totalTokens);

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        // The caller has already been sent the response by now, and an exception escaping
        // here would abort the connection before it is properly ended. A lost charge costs
        // a soft budget some tokens; a broken response costs the student the answer.
        catch (Exception exception)
        {
            // No voucher id or prefix, as for every other line: the time is the handle.
            logger.LogError(exception, "Failed to record {TotalTokens} consumed tokens.", totalTokens);
        }
    }
}
