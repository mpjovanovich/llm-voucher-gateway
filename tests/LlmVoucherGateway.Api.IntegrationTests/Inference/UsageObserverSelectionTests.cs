using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

public class UsageObserverSelectionTests
{
    [Theory]
    [InlineData("text/event-stream")]
    [InlineData("text/event-stream; charset=utf-8")]
    public void ForContentType_WithEventStream_ReadsStreamedChunks(string contentType)
    {
        Assert.IsType<StreamedUsageObserver>(IUsageObserver.ForContentType(contentType));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public void ForContentType_WithAnythingElse_ReadsWholeBody(string? contentType)
    {
        Assert.IsType<BufferedUsageObserver>(IUsageObserver.ForContentType(contentType));
    }
}
