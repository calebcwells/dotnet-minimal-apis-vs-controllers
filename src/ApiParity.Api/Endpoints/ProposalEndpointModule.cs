using System.Diagnostics;
using ApiParity.Api.Contracts;
using ApiParity.Api.Domain;
using ApiParity.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ApiParity.Api.Endpoints;

/// <summary>
/// THE CLASS-BASED MINIMAL API: answering "do the handlers have to be static?".
/// <para>
/// They do not. This is an ordinary class with a primary constructor and ordinary instance methods,
/// and it looks almost exactly like the controller: dependencies come in through the constructor,
/// handlers use them as fields. What it does NOT inherit is <c>ControllerBase</c>, so there is no
/// <c>HttpContext</c>/<c>ModelState</c>/<c>Url</c> surface to reach for, and the class is directly
/// constructible in a unit test.
/// </para>
/// <para>
/// The registration in <see cref="ProposalModuleEndpoints"/> is the part that matters. See the
/// lifetime warning there. Get that wrong and you have a singleton holding a scoped dependency,
/// which is the one genuine hazard in this style.
/// </para>
/// </summary>
public sealed class ProposalEndpointModule(IProposalService service)
{
    public async Task<Results<Ok<ProposalResponse[]>, ProblemHttpResult>> ListAsync(
        [FromQuery] ProposalStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(status, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse[]>, ProblemHttpResult>>(
            Right: proposals => TypedResults.Ok(proposals.Select(ProposalResponse.From).ToArray()),
            Left: error => ToProblem(error));
    }

    public async Task<Results<Ok<ProposalResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse>, ProblemHttpResult>>(
            Right: proposal => TypedResults.Ok(ProposalResponse.From(proposal)),
            Left: error => ToProblem(error));
    }

    public async Task<Results<Created<ProposalResponse>, ProblemHttpResult>> CreateAsync(
        CreateProposalDto request,
        LinkGenerator linkGenerator,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            request.SpeakerName, request.Title, request.DurationMinutes, cancellationToken);

        return result.Match<Results<Created<ProposalResponse>, ProblemHttpResult>>(
            Right: created => TypedResults.Created(
                linkGenerator.GetUriByName(httpContext, "Class_GetProposal", new { id = created.Id }),
                ProposalResponse.From(created)),
            Left: error => ToProblem(error));
    }

    public async Task<Results<Ok<ProposalResponse>, ProblemHttpResult>> AcceptAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(id, cancellationToken);

        return result.Match<Results<Ok<ProposalResponse>, ProblemHttpResult>>(
            Right: accepted => TypedResults.Ok(ProposalResponse.From(accepted)),
            Left: error => ToProblem(error));
    }

    public IResult Boom() => throw new InvalidOperationException("Deliberate demo failure.");

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

/// <summary>
/// Registration for the class-based module. This is where the lifetime decision is made.
/// </summary>
public static class ProposalModuleEndpoints
{
    public static IEndpointRouteBuilder MapProposalModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/class/proposals")
            .WithTags("Proposals (class-based minimal API)")
            .RequireAuthorization(ReviewPolicies.Read)
            .AddEndpointFilter<CorrelationIdEndpointFilter>();

        // ─── THE LIFETIME RULE ───────────────────────────────────────────────────────────
        // Each handler takes the module as a PARAMETER, so the framework resolves it from DI per
        // request and it gets the registered (scoped) lifetime, the same treatment MVC gives a
        // controller.
        //
        // The tempting alternative is a method group over an instance you created yourself:
        //
        //     var module = new ProposalEndpointModule(service);   // DON'T
        //     group.MapGet("", module.ListAsync);
        //
        // That compiles and works, but the delegate captures ONE instance at startup, so the module
        // is effectively a singleton holding whatever it was constructed with. With a scoped
        // dependency (a DbContext, a per-request tenant accessor) that is a captive-dependency bug
        // that will not surface until concurrent traffic hits it. The probe endpoints in
        // HandlerLifetimeProbe.cs demonstrate the difference, and a test asserts it.
        group.MapGet("", (ProposalEndpointModule module, [FromQuery] ProposalStatus? status,
                CancellationToken ct) => module.ListAsync(status, ct))
            .WithName("Class_ListProposals")
            .WithSummary("Lists proposals, optionally filtered by status.");

        group.MapGet("/{id:guid}", (ProposalEndpointModule module, Guid id, CancellationToken ct) =>
                module.GetAsync(id, ct))
            .WithName("Class_GetProposal")
            .WithSummary("Fetches a single proposal.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", (ProposalEndpointModule module, CreateProposalDto request,
                LinkGenerator links, HttpContext http, CancellationToken ct) =>
                module.CreateAsync(request, links, http, ct))
            .WithName("Class_CreateProposal")
            .WithSummary("Submits a proposal.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/acceptance", (ProposalEndpointModule module, Guid id,
                CancellationToken ct) => module.AcceptAsync(id, ct))
            .WithName("Class_AcceptProposal")
            .WithSummary("Accepts a submitted proposal.")
            .RequireAuthorization(ReviewPolicies.Accept)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/boom", (ProposalEndpointModule module) => module.Boom())
            .WithName("Class_Boom")
            .WithSummary("Deliberately throws, to compare unhandled-exception behavior.")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }
}
