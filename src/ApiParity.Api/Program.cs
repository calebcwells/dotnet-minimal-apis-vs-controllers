using ApiParity.Api.Domain;
using ApiParity.Api.Endpoints;
using ApiParity.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ─── SHARED SERVICES ─────────────────────────────────────────────────────────────────────
// Everything registered here is consumed identically by all three API styles. That is the headline
// finding of this repository: the interesting parts of an ASP.NET Core app (DI, authentication,
// authorization, options, logging, ProblemDetails, OpenAPI) live BELOW the controller/minimal
// choice. Switching styles does not touch any of it.

builder.Services.Configure<ConferencePolicyOptions>(
    builder.Configuration.GetSection(ConferencePolicyOptions.SectionName));

builder.Services.AddSingleton<ProposalStore>();
builder.Services.AddScoped<IProposalService, ProposalService>();
// Required by the controller's [ServiceFilter(typeof(CorrelationIdActionFilter))]. Its minimal-API
// counterpart needs no registration: .AddEndpointFilter<T>() activates the type directly.
builder.Services.AddScoped<CorrelationIdActionFilter>();

// The class-based endpoint module is registered exactly like a controller would be: scoped, so it
// is safe for it to hold scoped dependencies.
builder.Services.AddScoped<ProposalEndpointModule>();
builder.Services.AddScoped<HandlerLifetimeProbe>();

// ─── VALIDATION (.NET 10) ────────────────────────────────────────────────────────────────
// The one line that closes the historical DataAnnotations gap for minimal APIs. A source generator
// finds the DTOs referenced by minimal-API handlers and the framework validates them before the
// handler runs, the same treatment [ApiController] gives a controller action.
builder.Services.AddValidation();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

// Policies are registered once and named by every style.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(ReviewPolicies.Read, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(ReviewPolicies.ScopeClaimType, ReviewPolicies.ReadScope))
    .AddPolicy(ReviewPolicies.Accept, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(ReviewPolicies.ScopeClaimType, ReviewPolicies.AcceptScope));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

// Controllers must be opted into; minimal APIs need no equivalent registration.
builder.Services.AddControllers();

// Built-in OpenAPI (.NET 9+). One document describes ALL THREE styles, proof that minimal-API
// endpoints are first-class citizens of the same OpenAPI pipeline, not a second-tier surface.
builder.Services.AddOpenApi(options =>
{
    // Declare the X-Api-Key scheme so UI clients (Scalar below) can offer an auth field. Without
    // this the document is still accurate about shapes and status codes, but a "try it" click from
    // any UI would 401 with no way to supply the key.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyAuthenticationHandler.HeaderName,
                Description = "Demo keys: 'speaker-key' (read) or 'reviewer-key' (read + accept)."
            };
        return Task.CompletedTask;
    });

    // Mark the operations that actually require the key, driven by the same endpoint metadata the
    // authorization middleware reads: [Authorize] on the controller and .RequireAuthorization()
    // on the minimal groups both surface as IAuthorizeData, so all three styles come out
    // identically and the anonymous endpoints (health, the lifetime probes) stay unmarked.
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(
                    ApiKeyAuthenticationHandler.SchemeName, context.Document)] = []
            });
        }

        return Task.CompletedTask;
    });

    // Scalar's sidebar lists tags in the order their operations first appear in the document, and
    // the framework describes minimal-API endpoints before controller ones regardless of mapping
    // order, which buries the controller, the baseline of the whole comparison, at the bottom of
    // the list. Reorder the paths into the README's order: controller first, then the two minimal
    // styles, then the diagnostics.
    options.AddDocumentTransformer((document, _, _) =>
    {
        if (document.Paths is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        static int Rank(string path) =>
            path.StartsWith("/api/v1/controller/", StringComparison.Ordinal) ? 0
            : path.StartsWith("/api/v1/minimal/", StringComparison.Ordinal) ? 1
            : path.StartsWith("/api/v1/class/", StringComparison.Ordinal) ? 2
            : 3;

        var ordered = new OpenApiPaths();
        foreach (var (path, item) in document.Paths.OrderBy(pair => Rank(pair.Key)))
        {
            ordered[path] = item;
        }

        document.Paths = ordered;

        // Reordering the paths is not enough: Scalar's sidebar follows the document-level tags
        // array, which the generator also builds in emission order. Rebuild it to match the tags'
        // first appearance in the reordered paths.
        if (document.Tags is { Count: > 0 })
        {
            var byName = document.Tags
                .Where(tag => tag.Name is not null)
                .ToDictionary(tag => tag.Name!, StringComparer.Ordinal);
            var reordered = new HashSet<OpenApiTag>();

            foreach (var operation in ordered.Values.SelectMany(
                pathItem => pathItem.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
            {
                foreach (var tagReference in operation.Tags ?? Enumerable.Empty<OpenApiTagReference>())
                {
                    if (tagReference.Name is { } name && byName.Remove(name, out var tag))
                    {
                        reordered.Add(tag);
                    }
                }
            }

            reordered.UnionWith(byName.Values);
            document.Tags = reordered;
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Redirects plain-HTTP callers to the https endpoint declared in launchSettings.json. Under the
// in-memory TestServer there is no https address, so this middleware cannot determine a redirect
// port and passes requests through, which is why the parity tests and benchmarks run over plain
// HTTP unchanged.
app.UseHttpsRedirection();

app.UseExceptionHandler();
// Turns bare 401/403 status codes from the auth middleware into ProblemDetails bodies for every
// style alike.
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// ─── THE THREE STYLES, MOUNTED SIDE BY SIDE ──────────────────────────────────────────────
// /api/v1/controller/proposals  → Controllers/ProposalsController.cs
app.MapControllers();
// /api/v1/minimal/proposals     → Endpoints/ProposalEndpoints.cs        (static handlers)
app.MapProposalEndpoints();
// /api/v1/class/proposals       → Endpoints/ProposalEndpointModule.cs   (instance methods)
app.MapProposalModule();
app.MapHandlerLifetimeProbes();

app.MapOpenApi();

// Interactive documentation over the same document, at /scalar/v1. The reviewer key is pre-filled
// so every operation works out of the box; switch it to 'speaker-key' to watch the accept
// endpoints start returning 403. That is the layered-policy behavior the parity tests assert.
app.MapScalarApiReference(options => options
    .AddPreferredSecuritySchemes(ApiKeyAuthenticationHandler.SchemeName)
    .AddApiKeyAuthentication(
        ApiKeyAuthenticationHandler.SchemeName,
        scheme => scheme.Value = ApiKeyAuthenticationHandler.ReviewerKey));

// Unauthenticated liveness probe, and the shortest possible illustration of a minimal API.
app.MapGet("/health", () => TypedResults.Ok(new { status = "healthy" }))
    .AllowAnonymous()
    .WithName("Health")
    // Untagged endpoints fall back to a tag named after the assembly ("ApiParity.Api"), which
    // looks like a mistake in the Scalar sidebar.
    .WithTags("Health");

app.Run();

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the app in tests.</summary>
public partial class Program;
