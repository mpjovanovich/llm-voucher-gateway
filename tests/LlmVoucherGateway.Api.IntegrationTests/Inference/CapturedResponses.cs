namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// The Milestone 1 captures of live vLLM responses, replayed so the gateway can be tested
/// without a GPU. See <c>Fixtures/README.md</c> for what each one is and when it was taken.
/// </summary>
/// <remarks>
/// Raw payloads, owned by this project: tests assert on their exact bytes, both the token
/// totals the observers expect and the bodies the relay must pass through untouched.
/// Refreshing one is therefore a deliberate edit to a fixture, not a side effect of
/// rewriting prose somewhere else.
/// </remarks>
internal static class CapturedResponses
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures"
    );

    public static string NonStreamingBody() => Read("non-streaming-response.json");

    public static string UnknownModelErrorBody() =>
        Read(Path.Combine("errors", "unknown-model.json"));

    /// <summary>
    /// The forced-usage capture, because the gateway's vLLM always runs with
    /// <c>--enable-force-include-usage</c>: every chunk carries a running token count.
    /// </summary>
    public static string StreamingBodyWithUsage() => Read("streaming-response-usage-requested-server.sse");

    /// <summary>
    /// What a vLLM started without <c>--enable-force-include-usage</c> streams: no
    /// <c>usage</c> on any chunk.
    /// </summary>
    public static string StreamingBodyWithNoUsage() =>
        Read("streaming-response-usage-not-requested.sse");

    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(FixtureDirectory, fileName));
}
