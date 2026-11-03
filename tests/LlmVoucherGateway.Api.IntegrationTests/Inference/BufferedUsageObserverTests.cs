using System.Text;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

public class BufferedUsageObserverTests
{
    [Fact]
    public void ReadTotalTokens_WhenBodyArrivesInPieces_ReturnsTotal()
    {
        byte[] body = Encoding.UTF8.GetBytes(CapturedResponses.NonStreamingBody());
        BufferedUsageObserver observer = new();

        observer.Observe(body.AsSpan(0, body.Length / 2));
        observer.Observe(body.AsSpan(body.Length / 2));

        Assert.Equal(45, observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_ForUpstreamErrorBody_ReturnsNull()
    {
        BufferedUsageObserver observer = new();

        observer.Observe(Encoding.UTF8.GetBytes(CapturedResponses.UnknownModelErrorBody()));

        Assert.Null(observer.ReadTotalTokens());
    }
}
