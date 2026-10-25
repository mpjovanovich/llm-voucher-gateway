using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LlmVoucherGateway.Api.ExceptionHandlers;

/// <summary>
/// Translates unhandled exceptions into ProblemDetails responses. Endpoints let domain
/// rules throw rather than pre-checking them, so this is the single place a rule
/// violation becomes a status code.
/// </summary>
internal sealed class ExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        ExceptionDetails exceptionDetails = GetExceptionDetails(exception);

        ProblemDetails problemDetails = new()
        {
            Status = exceptionDetails.Status,
            Title = exceptionDetails.Title,
            Detail = exceptionDetails.Detail,
        };

        if (exceptionDetails.Errors is not null)
        {
            problemDetails.Extensions["errors"] = exceptionDetails.Errors;
        }

        httpContext.Response.StatusCode = exceptionDetails.Status;

        // The service fills in the type URI and the problem+json content type, and
        // reports false if the response had already started — in which case returning
        // false lets the framework abort the connection instead of splicing JSON into
        // a half-sent body. That case is real here: /v1/chat/completions streams.
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        });
    }

    private static ExceptionDetails GetExceptionDetails(Exception exception) =>
        exception switch
        {
            // Thrown by model binding before an endpoint is entered — unparseable JSON,
            // a body that is too large. It carries the status the framework would have
            // used had this handler not been in the way.
            BadHttpRequestException badHttpRequestException => new ExceptionDetails(
                badHttpRequestException.StatusCode,
                "Invalid request",
                badHttpRequestException.Message,
                null
            ),
            // The domain rejects bad input with ArgumentException-derived types, so an
            // admin sees 400 rather than 500. The tradeoff: a genuine argument bug in our
            // own code also reports as a bad request. If that ever misleads, give the
            // domain its own exception type and match on that instead.
            ArgumentException argumentException => new ExceptionDetails(
                StatusCodes.Status400BadRequest,
                "Invalid request",
                argumentException.Message,
                null
            ),
            _ => new ExceptionDetails(
                StatusCodes.Status500InternalServerError,
                "Server error",
                "An unexpected error has occurred.",
                null
            ),
        };

    private sealed record ExceptionDetails(
        int Status,
        string Title,
        string Detail,
        IEnumerable<object>? Errors
    );
}
