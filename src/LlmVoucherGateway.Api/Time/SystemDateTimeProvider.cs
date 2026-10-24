using LlmVoucherGateway.Domain.Abstractions;

namespace LlmVoucherGateway.Api.Time;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
