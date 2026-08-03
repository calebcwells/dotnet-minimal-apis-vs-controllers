using Microsoft.AspNetCore.Diagnostics;

namespace ApiParity.Api.Infrastructure;

/// <summary>
/// <c>IExceptionHandler</c> + <c>app.UseExceptionHandler()</c> is middleware, so it covers
/// controllers and minimal APIs identically. There is no minimal-API equivalent of an MVC
/// exception <em>filter</em>, and none is needed. This is the modern shape for both.
/// </summary>
public sealed class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Detail is deliberately generic. The exception text stays in the server log.
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            }
        });
    }
}
