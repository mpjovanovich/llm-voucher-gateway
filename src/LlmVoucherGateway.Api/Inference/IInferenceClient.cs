namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// The gateway's seam onto the upstream inference server.
/// </summary>
internal interface IInferenceClient
{
    /// <summary>
    /// Forwards an opaque chat completion request body upstream.
    /// </summary>
    /// <param name="requestBody">
    /// The caller's unread request body, relayed without inspection. Not disposed by the
    /// implementation; ownership stays with the caller.
    /// </param>
    /// <param name="cancellationToken">
    /// Tears down the upstream request when the caller disconnects.
    /// </param>
    Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken
    );
}
