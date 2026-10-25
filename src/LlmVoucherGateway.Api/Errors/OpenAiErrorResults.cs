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

    public static IResult InvalidCredential(string message) =>
        Create(StatusCodes.Status401Unauthorized, InvalidRequestErrorType, message);

    /// <summary>
    /// 402 rather than 429: the credential is fine and the budget is not, which is a
    /// different thing from being asked to slow down. Rate limiting, if it ever arrives,
    /// needs 429 left free to mean that.
    /// </summary>
    public static IResult AllowanceExhausted(string message) =>
        Create(StatusCodes.Status402PaymentRequired, InsufficientQuotaErrorType, message);

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
