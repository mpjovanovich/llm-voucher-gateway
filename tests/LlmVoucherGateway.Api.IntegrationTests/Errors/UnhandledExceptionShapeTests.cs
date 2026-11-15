using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LlmVoucherGateway.Api.Errors;

namespace LlmVoucherGateway.Api.IntegrationTests.Errors;

/// <summary>
/// An exception nothing handled still has to leave in a format its caller can read:
/// the OpenAI envelope for a harness on <c>/v1</c>, ProblemDetails for an operator.
/// </summary>
public class UnhandledExceptionShapeTests
{
    private const string UpstreamFailureMessage = "Connection refused (llm-server:8000)";

    private static readonly object ChatRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    [Fact]
    public async Task ChatCompletions_WhenTheUpstreamThrows_ReturnsTheOpenAiErrorShape()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();
        factory.InferenceClient.Fault = new HttpRequestException(UpstreamFailureMessage);

        using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/chat/completions", ChatRequest);

        await AssertOpenAiServerErrorAsync(response);
    }

    [Fact]
    public async Task Models_WhenTheUpstreamThrows_ReturnsTheOpenAiErrorShape()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();
        factory.InferenceClient.Fault = new HttpRequestException(UpstreamFailureMessage);

        using HttpResponseMessage response = await client.GetAsync("/v1/models");

        await AssertOpenAiServerErrorAsync(response);
    }

    [Fact]
    public async Task AdminRoutes_KeepProblemDetails()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();
        using StringContent malformed = new("{ not json", Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync("/admin/vouchers", malformed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task AssertOpenAiServerErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;

        // Exactly the envelope: a harness that finds ProblemDetails fields here has
        // been handed a format it does not know.
        Assert.Equal(["error"], root.EnumerateObject().Select(property => property.Name));

        JsonElement error = root.GetProperty("error");

        Assert.Equal(
            ["code", "message", "param", "type"],
            error.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
        );
        Assert.Equal(OpenAiErrorResults.ServerErrorType, error.GetProperty("type").GetString());
        Assert.Equal(500, error.GetProperty("code").GetInt32());

        // What went wrong inside is for the log, not for a student's terminal.
        Assert.DoesNotContain(UpstreamFailureMessage, body, StringComparison.Ordinal);
    }
}
