using LlmVoucherGateway.Domain.Abstractions;

namespace LlmVoucherGateway.Api.IntegrationTests;

internal sealed class TestClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}
