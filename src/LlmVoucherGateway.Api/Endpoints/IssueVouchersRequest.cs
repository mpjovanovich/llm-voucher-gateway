namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// A batch to mint. Every voucher in the batch is stamped with the same section,
/// allowance and expiry, which is what makes policy a property of the batch rather
/// than of the gateway.
/// </summary>
internal sealed record IssueVouchersRequest(
    string? Section,
    int Count,
    int Allowance,
    DateTimeOffset ExpiresAt
);
