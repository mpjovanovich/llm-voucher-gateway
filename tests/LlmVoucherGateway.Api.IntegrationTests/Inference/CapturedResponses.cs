using System.Text;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// The Milestone 1 captures, read from the files the repository documents rather than
/// from strings pasted into a test.
/// </summary>
internal static class CapturedResponses
{
    private static readonly string CaptureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Captures"
    );

    public static string NonStreamingBody() =>
        File.ReadAllText(Path.Combine(CaptureDirectory, "non-streaming-response.json"));

    /// <summary>
    /// The SSE body rebuilt from the capture's <c>data:</c> lines. The captured file is
    /// written for a human to read, so the stream has to be lifted back out of it —
    /// including the terminating <c>[DONE]</c>, which a harness waits for.
    /// </summary>
    public static string StreamingBody()
    {
        StringBuilder body = new();

        foreach (string line in File.ReadLines(Path.Combine(CaptureDirectory, "streaming-response.md")))
        {
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                // Blank line after each event: that is the frame separator SSE requires.
                body.Append(line).Append("\n\n");
            }
        }

        return body.ToString();
    }
}
