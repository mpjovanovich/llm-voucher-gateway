namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Watches an upstream body go past on its way to the caller and reports the token count
/// it carried. Never alters or holds back the bytes it is shown.
/// </summary>
internal interface IUsageObserver
{
    void Observe(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// The count from what has been observed so far, or <c>null</c> when there was none.
    /// </summary>
    int? ReadTotalTokens();
}
