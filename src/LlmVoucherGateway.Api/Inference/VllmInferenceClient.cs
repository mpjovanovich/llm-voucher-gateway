using System.Net.Http.Headers;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Forwards chat completion requests to a vLLM server's OpenAI-compatible API.
/// </summary>
/// <remarks>
/// Registered as a typed client; its <see cref="HttpClient"/> is configured in
/// <c>Program.cs</c> with the upstream base address and an infinite timeout.
/// </remarks>
internal sealed class VllmInferenceClient : IInferenceClient
{
    private const string ChatCompletionsPath = "v1/chat/completions";

    private readonly HttpClient httpClient;

    public VllmInferenceClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Post, ChatCompletionsPath)
        {
            Content = new StreamContent(requestBody)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };

        // ResponseHeadersRead returns as soon as the headers land, leaving the body
        // unread on the wire. The default buffers the entire response first, which would
        // silently defeat streaming with no error to show for it.
        HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        return new InferenceResponse(response)
        {
            StatusCode = response.StatusCode,
            ContentType = response.Content.Headers.ContentType?.ToString(),
            Body = await response.Content.ReadAsStreamAsync(cancellationToken),
        };
    }
}
