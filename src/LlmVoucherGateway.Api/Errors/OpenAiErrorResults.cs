namespace LlmVoucherGateway.Api.Errors;

/// <summary>
/// The rejections this gateway generates, as results an endpoint or filter can return.
/// </summary>
internal static class OpenAiErrorResults
{
    /// <summary>
    /// Missing, unknown, revoked or expired credential. All of these are the same answer
    /// to the client — the credential cannot be used — and the message says which.
    /// </summary>
    public const string InvalidRequestErrorType = "invalid_request_error";

    /// <summary>
    /// A credential that is valid but has nothing left to spend. Separate from the above
    /// so a student can tell "wrong key" from "used it up".
    /// </summary>
    public const string InsufficientQuotaErrorType = "insufficient_quota";

    /// <summary>
    /// A failure on our side of the request. OpenAI's name for it, which is what a
    /// harness keys its "retry later" handling on when it looks at the type at all.
    /// </summary>
    public const string ServerErrorType = "server_error";

    public static IResult InvalidCredential(string message) =>
        Create(StatusCodes.Status401Unauthorized, InvalidRequestErrorType, message);

    public static IResult AllowanceExhausted(string message) =>
        Create(StatusCodes.Status403Forbidden, InsufficientQuotaErrorType, message);

    /// <summary>
    /// An exception nothing handled, on a path a harness calls. The status decides the
    /// type: a 4xx is the caller's request, anything else is ours.
    /// </summary>
    public static IResult UnhandledFailure(int statusCode, string message) =>
        Create(
            statusCode,
            statusCode is >= 400 and < 500 ? InvalidRequestErrorType : ServerErrorType,
            message
        );

    private static IResult Create(int statusCode, string type, string message)
    {
        OpenAiError error = new(message, type, Param: null, Code: statusCode);

        return Results.Json(
            error.Wrapped(),
            statusCode: statusCode,
            contentType: "application/json"
        );
    }
}
