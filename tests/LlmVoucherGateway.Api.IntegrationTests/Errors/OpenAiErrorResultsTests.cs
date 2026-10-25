using System.Net;
using System.Text.Json;
using LlmVoucherGateway.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LlmVoucherGateway.Api.IntegrationTests.Errors;

/// <summary>
/// Pins the rejections this gateway generates to the envelope the upstream server uses.
/// A harness understands one error format, so the comparison is against the Milestone 1
/// captures rather than against a shape written down in a test.
/// </summary>
public class OpenAiErrorResultsTests
{
    private static readonly string CapturedErrorDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Captures",
        "errors"
    );

    public static TheoryData<string> CapturedErrorFiles() =>
        new()
        {
            "unknown-model.json",
            "context-length-overflow.json",
        };

    [Theory]
    [MemberData(nameof(CapturedErrorFiles))]
    public async Task InvalidCredential_HasTheSameFieldsAsTheUpstreamErrors(string capturedErrorFile)
    {
        JsonElement captured = CapturedErrorBody(capturedErrorFile);
        JsonElement generated = await ExecuteAsync(OpenAiErrorResults.InvalidCredential("nope"));

        Assert.Equal(FieldNames(captured), FieldNames(generated));
        Assert.Equal(
            FieldNames(captured.GetProperty("error")),
            FieldNames(generated.GetProperty("error"))
        );
    }

    [Fact]
    public async Task InvalidCredential_IsUnauthorizedAndCarriesTheMessage()
    {
        JsonElement body = await ExecuteAsync(
            OpenAiErrorResults.InvalidCredential("Voucher has been revoked."),
            expectedStatusCode: HttpStatusCode.Unauthorized
        );

        JsonElement error = body.GetProperty("error");

        Assert.Equal("Voucher has been revoked.", error.GetProperty("message").GetString());
        Assert.Equal(OpenAiErrorResults.InvalidRequestErrorType, error.GetProperty("type").GetString());
        Assert.Equal(401, error.GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("param").ValueKind);
    }

    [Fact]
    public async Task AllowanceExhausted_IsPaymentRequiredAndADistinctType()
    {
        JsonElement body = await ExecuteAsync(
            OpenAiErrorResults.AllowanceExhausted("Voucher allowance is exhausted."),
            expectedStatusCode: HttpStatusCode.PaymentRequired
        );

        JsonElement error = body.GetProperty("error");

        // The distinction a student reads as "used it up" rather than "wrong key".
        Assert.Equal(OpenAiErrorResults.InsufficientQuotaErrorType, error.GetProperty("type").GetString());
        Assert.Equal(402, error.GetProperty("code").GetInt32());
    }

    private static string[] FieldNames(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static JsonElement CapturedErrorBody(string capturedErrorFile)
    {
        using var captured = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(CapturedErrorDirectory, capturedErrorFile))
        );

        JsonElement root = captured.RootElement;

        // One capture holds variants of the same error rather than a single response.
        JsonElement response = root.TryGetProperty("response", out JsonElement single)
            ? single
            : root.GetProperty("variants")[0].GetProperty("response");

        return response.GetProperty("body").Clone();
    }

    /// <summary>
    /// Runs the result against a bare HttpContext, which is what turns an
    /// <see cref="IResult"/> into the status code and bytes a client would receive.
    /// </summary>
    private static async Task<JsonElement> ExecuteAsync(
        IResult result,
        HttpStatusCode? expectedStatusCode = null
    )
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();

        using MemoryStream responseBody = new();

        DefaultHttpContext httpContext = new() { RequestServices = services };
        httpContext.Response.Body = responseBody;

        await result.ExecuteAsync(httpContext);

        if (expectedStatusCode is not null)
        {
            Assert.Equal((int)expectedStatusCode, httpContext.Response.StatusCode);
            Assert.Equal("application/json", httpContext.Response.ContentType);
        }

        responseBody.Position = 0;

        using JsonDocument written = await JsonDocument.ParseAsync(responseBody);

        return written.RootElement.Clone();
    }
}
