namespace LlmVoucherGateway.Api.Errors;

/// <summary>
/// The error envelope the upstream server uses, reproduced for the rejections this
/// gateway generates itself.
/// </summary>
/// <remarks>
/// A harness only understands one error format, and errors relayed from vLLM are already
/// in it — so anything we invent instead would render as garbage or crash the client.
/// The four fields and their names come from the Milestone 1 captures in
/// <c>api_documentation/errors/</c>; <c>code</c> is numeric there, carrying the HTTP
/// status, rather than the string OpenAI's own service sends.
/// </remarks>
internal sealed record OpenAiError(string Message, string Type, string? Param, int Code)
{
    /// <summary>
    /// Serialized as <c>{"error": { ... }}</c>: the body is always wrapped.
    /// </summary>
    internal sealed record Envelope(OpenAiError Error);

    public Envelope Wrapped() => new(this);
}
