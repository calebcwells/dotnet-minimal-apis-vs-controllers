namespace ApiParity.Api.Endpoints;

/// <summary>
/// A scoped service that reports which instance served the request, used to demonstrate the one
/// genuine hazard of class-based (non-static) minimal-API handlers.
/// </summary>
public sealed class HandlerLifetimeProbe
{
    /// <summary>Unique per constructed instance, so callers can tell instances apart.</summary>
    public string InstanceId { get; } = Guid.NewGuid().ToString("N")[..12];

    public object Describe() => new { instanceId = InstanceId };
}

public static class HandlerLifetimeEndpoints
{
    /// <summary>
    /// Two endpoints backed by the same scoped type, registered two different ways.
    /// <para>
    /// <c>/resolved</c> takes the probe as a handler parameter, so DI supplies a fresh scoped
    /// instance per request. <c>/captured</c> uses a method group over an instance built at startup,
    /// so every request is served by that one captured instance no matter how the type is
    /// registered.
    /// </para>
    /// <para>
    /// Both are legal C# and both compile. The difference only shows up at runtime, which is exactly
    /// why it is worth a test rather than a comment.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapHandlerLifetimeProbes(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/diagnostics/handler-lifetime")
            .WithTags("Handler lifetime")
            .AllowAnonymous();

        // Correct: resolved from DI per request, honoring the registered scoped lifetime.
        group.MapGet("/resolved", (HandlerLifetimeProbe probe) => probe.Describe())
            .WithName("Lifetime_Resolved");

        // Hazard: the delegate closes over ONE instance created at startup. A scoped dependency held
        // by that instance would be captive, alive for the life of the application.
        var capturedAtStartup = new HandlerLifetimeProbe();
        group.MapGet("/captured", capturedAtStartup.Describe)
            .WithName("Lifetime_Captured");

        return app;
    }
}
