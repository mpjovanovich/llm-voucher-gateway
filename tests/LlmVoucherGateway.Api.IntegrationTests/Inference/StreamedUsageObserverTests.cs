using System.Text;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

public class StreamedUsageObserverTests
{
    [Fact]
    public void ReadTotalTokens_AfterWholeCapture_ReturnsFinalTotal()
    {
        StreamedUsageObserver observer = new();

        observer.Observe(Encoding.UTF8.GetBytes(CapturedResponses.StreamingBody()));

        Assert.Equal(60, observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_WhenCaptureArrivesOneByteAtATime_ReturnsFinalTotal()
    {
        StreamedUsageObserver observer = new();

        foreach (byte b in Encoding.UTF8.GetBytes(CapturedResponses.StreamingBody()))
        {
            observer.Observe([b]);
        }

        Assert.Equal(60, observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_WhenStreamIsCutMidLine_ReturnsLastCompleteRunningTotal()
    {
        string body = CapturedResponses.StreamingBody();
        int cutAfter = body.IndexOf("\"total_tokens\":45", StringComparison.Ordinal);
        int nextEvent = body.IndexOf("\n\n", cutAfter, StringComparison.Ordinal) + 2;

        StreamedUsageObserver observer = new();

        observer.Observe(Encoding.UTF8.GetBytes(body[..(nextEvent + 40)]));

        Assert.Equal(45, observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_WithCrLfLineEndings_ReturnsFinalTotal()
    {
        StreamedUsageObserver observer = new();

        observer.Observe(Encoding.UTF8.GetBytes(CapturedResponses.StreamingBody().Replace("\n", "\r\n", StringComparison.Ordinal)));

        Assert.Equal(60, observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_WhenChunksCarryNoUsage_ReturnsNull()
    {
        StreamedUsageObserver observer = new();

        observer.Observe(
            """
            data: {"choices":[{"index":0,"delta":{"content":"Hello"},"finish_reason":"stop"}]}

            data: [DONE]


            """u8
        );

        Assert.Null(observer.ReadTotalTokens());
    }

    [Fact]
    public void ReadTotalTokens_WithNothingObserved_ReturnsNull()
    {
        Assert.Null(new StreamedUsageObserver().ReadTotalTokens());
    }
}
