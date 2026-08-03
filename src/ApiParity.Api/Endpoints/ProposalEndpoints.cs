using System.Diagnostics;
using ApiParity.Api.Contracts;
using ApiParity.Api.Domain;
using ApiParity.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ApiParity.Api.Endpoints;

/// <summary>
/// THE MINIMAL API IMPLEMENTATION: the same four operations as
/// <c>Controllers/ProposalsController.cs</c>, over the same service and the same authorization
/// policies.
/// <para>
/// The five capabilities people most often assume minimal APIs lack all appear below, labelled:
/// endpoint grouping, authorization, dependency injection, error handling, and DataAnnotations
/// validation.
/// </para>
/// </summary>
public static class ProposalEndpoints
{
    public static IEndpointRouteBuilder MapProposalEndpoints(this IEndpointRouteBuilder app)
    {
        // ─── GROUPING ────────────────────────────────────────────────────────────────────
        // MapGroup is the answer to "where did my [Route] attribute go?". Everything chained here
        // (route prefix, OpenAPI tag, authorization, filters) is inherited by every endpoint in
        // the group, which is precisely what class-level attributes do for a controller. Unlike
        // attributes, groups nest arbitrarily and are composable in code.
        var group = app.MapGroup("/api/v1/minimal/proposals")
            .WithTags("Proposals (minimal API)")
            // ─── AUTHORIZATION ───────────────────────────────────────────────────────────
            // The same registered policy the controller names in [Authorize(Policy = ...)]. This is
            // real server-side enforcement, not a rendering hint.
            .RequireAuthorization(ReviewPolicies.Read)
            // The endpoint-filter equivalent of an MVC action filter, applied group-wide.
            .AddEndpointFilter<CorrelationIdEndpointFilter>();

        // On the .Produces* calls below: a nuance worth knowing before you promise your team that
        // "the signature documents itself". Success shapes ARE inferred from the Results<...>
        // union: Ok<T> and Created<T> carry their status code in the type, so OpenAPI picks up
        // 200/201 with the right schema and the compiler stops the handler returning anything
        // undeclared. Problem responses are not, because ProblemHttpResult takes its status at
        // runtime, so 404/409/422 need to be declared explicitly, exactly as a controller declares them
        // with [ProducesResponseType]. Omit these and the endpoint still behaves correctly but
        // silently under-documents itself.
        group.MapGet("", ListAsync)
            .WithName("Minimal_ListProposals")
            .WithSummary("Lists proposals, optionally filtered by status.");

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("Minimal_GetProposal")
            .WithSummary("Fetches a single proposal.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("Minimal_CreateProposal")
            .WithSummary("Submits a proposal.")
            // Documents the 400 that AddValidation() produces automatically. The handler itself
            // never sees an invalid payload.
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/acceptance", AcceptAsync)
            .WithName("Minimal_AcceptProposal")
            .WithSummary("Accepts a submitted proposal.")
            // A second policy layered on top of the group's. Both must pass, exactly as with a
            // method-level [Authorize] over a class-level one.
            .RequireAuthorization(ReviewPolicies.Accept)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/boom", Boom)
            .WithName("Minimal_Boom")
            .WithSummary("Deliberately throws, to compare unhandled-exception behavior.")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // ─── DEPENDENCY INJECTION ────────────────────────────────────────────────────────────
    // IProposalService and CancellationToken are injected as plain parameters. There is no
    // [FromServices] attribute and no constructor: since .NET 7 the framework resolves any
    // parameter it cannot bind from the route, query, header, or body out of the DI container.
    // Because these handlers are static methods with explicit parameters, they are also directly
    // unit-testable without a controller context or an HttpContext.

    private static async Task<Results<Ok<ProposalResponse[]>, ProblemHttpResult>> ListAsync(
        [FromQuery] ProposalStatus? status,
        IProposalService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(status, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse[]>, ProblemHttpResult>>(
            Right: proposals => TypedResults.Ok(proposals.Select(ProposalResponse.From).ToArray()),
            Left: error => ToProblem(error));
    }

    private static async Task<Results<Ok<ProposalResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        IProposalService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse>, ProblemHttpResult>>(
            Right: proposal => TypedResults.Ok(ProposalResponse.From(proposal)),
            Left: error => ToProblem(error));
    }

    // ─── DATA ANNOTATIONS ────────────────────────────────────────────────────────────────
    // No validation code here either. builder.Services.AddValidation() (new in .NET 10) makes a
    // source generator discover CreateProposalDto, and the framework short-circuits with a 400
    // ValidationProblemDetails before this method runs, the same deal [ApiController] gives the
    // controller. Prior to .NET 10 this was the one genuine capability gap and needed a
    // hand-rolled endpoint filter.
    private static async Task<Results<Created<ProposalResponse>, ProblemHttpResult>> CreateAsync(
        CreateProposalDto request,
        IProposalService service,
        // LinkGenerator is the minimal-API answer to CreatedAtRoute/Url.Link: named-route URL
        // generation off the same route table, so the Location header is an absolute URL built from
        // routing rather than a hand-concatenated string.
        LinkGenerator linkGenerator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            request.SpeakerName, request.Title, request.DurationMinutes, cancellationToken);

        return result.Match<Results<Created<ProposalResponse>, ProblemHttpResult>>(
            Right: created => TypedResults.Created(
                linkGenerator.GetUriByName(httpContext, "Minimal_GetProposal", new { id = created.Id }),
                ProposalResponse.From(created)),
            Left: error => ToProblem(error));
    }

    private static async Task<Results<Ok<ProposalResponse>, ProblemHttpResult>> AcceptAsync(
        Guid id,
        IProposalService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(id, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse>, ProblemHttpResult>>(
            Right: accepted => TypedResults.Ok(ProposalResponse.From(accepted)),
            Left: error => ToProblem(error));
    }

    private static IResult Boom() => throw new InvalidOperationException("Deliberate demo failure.");

    // ─── ERROR HANDLING ──────────────────────────────────────────────────────────────────
    // Written independently of the controller's ToProblem so the parity tests are proving something
    // rather than asserting that shared code equals itself.
    //
    // The Results<T1, T2> union is a real upgrade over [ProducesResponseType] for the SUCCESS path:
    // the compiler rejects a handler that returns an undeclared type, so the documented success
    // contract cannot drift from the implemented one the way an attribute can. It is not a complete
    // replacement. See the note on .ProducesProblem above.
    private static ProblemHttpResult ToProblem(ProposalError error) => error switch
    {
        ProposalError.NotFound e => TypedResults.Problem(
            title: ProblemText.NotFoundTitle,
            detail: ProblemText.NotFound(e.Id),
            statusCode: StatusCodes.Status404NotFound),

        ProposalError.OverLength e => TypedResults.Problem(
            title: ProblemText.OverLengthTitle,
            detail: ProblemText.OverLength(e.LimitMinutes, e.RequestedMinutes),
            statusCode: StatusCodes.Status422UnprocessableEntity),

        ProposalError.AlreadyDecided e => TypedResults.Problem(
            title: ProblemText.AlreadyDecidedTitle,
            detail: ProblemText.AlreadyDecided(e.Status),
            statusCode: StatusCodes.Status409Conflict),

        // See the note on the controller's ToProblem: unreachable, mandated by CS8509, and throwing
        // rather than inventing a status code.
        _ => throw new UnreachableException(ProblemText.Unmapped(error))
    };
}
