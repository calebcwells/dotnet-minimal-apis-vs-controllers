using Microsoft.AspNetCore.Mvc.Filters;

namespace ApiParity.Api.Infrastructure;

/// <summary>
/// The MVC equivalent of <see cref="CorrelationIdEndpointFilter"/>, included so the two mechanisms
/// can be compared directly. Functionally identical; the parity tests assert every style emits the
/// same header.
/// </summary>
public sealed class CorrelationIdActionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var httpContext = context.HttpContext;
        httpContext.Response.Headers[CorrelationId.HeaderName] = CorrelationId.Resolve(httpContext);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        // Nothing to do on the way out. It exists only to satisfy IActionFilter, which is itself a
        // small illustration of the extra ceremony the MVC filter model carries.
    }
}
