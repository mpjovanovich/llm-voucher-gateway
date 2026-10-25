using LlmVoucherGateway.Domain.Abstractions;

namespace LlmVoucherGateway.Domain.UnitTests;

internal sealed class FakeDateTimeProvider(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = utcNow;
}
