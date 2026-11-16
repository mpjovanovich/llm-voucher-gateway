namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Why a voucher is being withdrawn. The reason is the record an operator looks at
/// later; the domain rejects a missing one.
/// </summary>
internal sealed record RevokeVoucherRequest(string? Reason);
