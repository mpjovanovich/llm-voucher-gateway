namespace LlmVoucherGateway.Domain.Vouchers;

public sealed record IssuedVoucher(Voucher Voucher, string PlaintextKey);
