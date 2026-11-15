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
    /// The caller's unread request body, relayed upstream without inspection. Treat it as
    /// consumed once this returns: the send path may dispose it.
    /// </param>
    /// <param name="cancellationToken">
    /// Tears down the upstream request when the caller disconnects.
    /// </param>
    Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Fetches the upstream model list.
    /// </summary>
    /// <param name="cancellationToken">
    /// Tears down the upstream request when the caller disconnects.
    /// </param>
    Task<InferenceResponse> ListModelsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asks the upstream server whether it is up. <c>false</c> when it answers but says
    /// no; throws when it cannot be reached at all.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}
