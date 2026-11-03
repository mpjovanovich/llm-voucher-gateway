using System.Text;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

public class TokenUsageTests
{
    [Fact]
    public void ReadTotalTokens_FromNonStreamingCapture_ReturnsTotalNotPromptPlusTotal()
    {
        // The capture reports prompt_tokens 35 and total_tokens 45.
        Assert.Equal(45, Read(CapturedResponses.NonStreamingBody()));
    }

    [Fact]
    public void ReadTotalTokens_FromFinalStreamedChunk_ReturnsRunningTotal()
    {
        string lastChunk = CapturedResponses.StreamingBody()
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line != "data: [DONE]")["data: ".Length..];

        Assert.Equal(60, Read(lastChunk));
    }

    [Theory]
    [InlineData("""{"id":"chatcmpl-1","choices":[]}""")]
    [InlineData("""{"usage":null}""")]
    [InlineData("""{"usage":{"prompt_tokens":35}}""")]
    [InlineData("""{"usage":{"total_tokens":"60"}}""")]
    [InlineData("""{"usage":{"total_tokens":-1}}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""{"usage":{"total_tokens":6""")]
    [InlineData("[DONE]")]
    [InlineData("")]
    public void ReadTotalTokens_WithoutReadableCount_ReturnsNull(string json)
    {
        Assert.Null(Read(json));
    }

    private static int? Read(string json) => TokenUsage.ReadTotalTokens(Encoding.UTF8.GetBytes(json));
}
