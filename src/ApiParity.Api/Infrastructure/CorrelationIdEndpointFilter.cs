namespace ApiParity.Api.Infrastructure;

/// <summary>
/// The minimal-API answer to an MVC action filter. Compare with
/// <see cref="CorrelationIdActionFilter"/>: same effect, but this one is a plain class with a
/// single method and no <c>ActionExecutingContext</c>/<c>ActionExecutedContext</c> pair to reason
/// about. Attach it with <c>.AddEndpointFilter&lt;T&gt;()</c> on a single endpoint or on a whole
/// <c>MapGroup</c>.
/// </summary>
public sealed class CorrelationIdEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.Headers[CorrelationId.HeaderName] = CorrelationId.Resolve(httpContext);

        return await next(context);
    }
}

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// Echoes the caller's correlation id back, or mints one. Note that it reflects client input
    /// into a response header unvalidated. That is safe here because Kestrel rejects control characters in
    /// both directions, but a real service should cap the length and constrain the character set
    /// before echoing anything a caller sent, and should not let an unbounded caller-supplied value
    /// reach a log aggregator or downstream call.
    /// </summary>
    public static string Resolve(HttpContext httpContext) =>
        httpContext.Request.Headers.TryGetValue(HeaderName, out var incoming) && incoming.Count > 0
            ? incoming[0]!
            : Guid.NewGuid().ToString("N")[..12];
}
