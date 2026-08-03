using System.Diagnostics;
using ApiParity.Api.Contracts;
using ApiParity.Api.Domain;
using ApiParity.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiParity.Api.Controllers;

/// <summary>
/// THE CONTROLLER IMPLEMENTATION: the familiar half of the comparison, and the baseline every
/// other style is measured against.
/// <para>
/// Read this alongside <c>Endpoints/ProposalEndpoints.cs</c>, which exposes the same four
/// operations over the same service with the same authorization policies. All three are mounted in
/// the same application; the parity tests hit each one and assert the responses match.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/controller/proposals")]
// Deliberately NOT [Produces("application/json")]: that attribute also forces error responses to
// application/json, which would hide the correct application/problem+json content type and make
// this side of the comparison look different from the minimal-API side for no good reason.
[Authorize(Policy = ReviewPolicies.Read)]
// [ServiceFilter] resolves the filter FROM the container, so it honors the scoped registration in
// Program.cs. It is the direct analog of .AddEndpointFilter<T>() on the minimal side. [TypeFilter]
// would also work here but activates the type itself outside DI (only its constructor arguments are
// resolved), which would make the registration dead code and the comparison misleading.
[ServiceFilter(typeof(CorrelationIdActionFilter))]
[Tags("Proposals (controller)")]
public sealed class ProposalsController(IProposalService service) : ControllerBase
{
    // On the <summary> texts below: .NET 10 copies XML doc comments into the OpenAPI document, so
    // each action carries the same summary its minimal-API siblings declare with .WithSummary(),
    // and anything longer meant for code readers stays in comments like this one, out of the
    // consumer-facing API reference.
    /// <summary>Lists proposals, optionally filtered by status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ProposalResponse[]), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProposalResponse[]>> ListAsync(
        [FromQuery] ProposalStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(status, cancellationToken);

        return result.Match<ActionResult<ProposalResponse[]>>(
            Right: proposals => Ok(proposals.Select(ProposalResponse.From).ToArray()),
            Left: error => ToProblem(error));
    }

    /// <summary>Fetches a single proposal.</summary>
    [HttpGet("{id:guid}", Name = "Controller_GetProposal")]
    [ProducesResponseType(typeof(ProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProposalResponse>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(id, cancellationToken);

        return result.Match<ActionResult<ProposalResponse>>(
            Right: proposal => Ok(ProposalResponse.From(proposal)),
            Left: error => ToProblem(error));
    }

    // There is no explicit validation code here: [ApiController] runs the DataAnnotations on
    // CreateProposalDto and short-circuits with a 400 before this method is entered. The
    // minimal-API siblings get the same behavior from AddValidation().
    /// <summary>Submits a proposal.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProposalResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProposalResponse>> CreateAsync(
        [FromBody] CreateProposalDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(
            request.SpeakerName, request.Title, request.DurationMinutes, cancellationToken);

        return result.Match<ActionResult<ProposalResponse>>(
            Right: created => CreatedAtRoute(
                "Controller_GetProposal",
                new { id = created.Id },
                ProposalResponse.From(created)),
            Left: error => ToProblem(error));
    }

    // A second, stricter policy layered on top of the controller-level one. Both must pass.
    /// <summary>Accepts a submitted proposal.</summary>
    [HttpPost("{id:guid}/acceptance")]
    [Authorize(Policy = ReviewPolicies.Accept)]
    [ProducesResponseType(typeof(ProposalResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProposalResponse>> AcceptAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.AcceptAsync(id, cancellationToken);

        return result.Match<ActionResult<ProposalResponse>>(
            Right: accepted => Ok(ProposalResponse.From(accepted)),
            Left: error => ToProblem(error));
    }

    /// <summary>Deliberately throws, to compare unhandled-exception behavior.</summary>
    [HttpGet("boom")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public IActionResult Boom() => throw new InvalidOperationException("Deliberate demo failure.");

    /// <summary>
    /// The MVC idiom: build an <c>ObjectResult</c> through <c>ControllerBase.Problem</c>. Compare
    /// <c>ProposalEndpoints.ToProblem</c>, which does the same job with <c>TypedResults.Problem</c>.
    /// </summary>
    private ObjectResult ToProblem(ProposalError error) => error switch
    {
        ProposalError.NotFound e => Problem(
            title: ProblemText.NotFoundTitle,
            detail: ProblemText.NotFound(e.Id),
            statusCode: StatusCodes.Status404NotFound),

        ProposalError.OverLength e => Problem(
            title: ProblemText.OverLengthTitle,
            detail: ProblemText.OverLength(e.LimitMinutes, e.RequestedMinutes),
            statusCode: StatusCodes.Status422UnprocessableEntity),

        ProposalError.AlreadyDecided e => Problem(
            title: ProblemText.AlreadyDecidedTitle,
            detail: ProblemText.AlreadyDecided(e.Status),
            statusCode: StatusCodes.Status409Conflict),

        // Unreachable today, and required only because C# cannot prove the switch exhaustive. It
        // throws rather than inventing a status code: a new ProposalError with no mapping is a bug,
        // and this surfaces it as a loud 500 through UnhandledExceptionHandler (identically in all
        // three styles) instead of quietly returning a plausible-looking wrong answer.
        _ => throw new UnreachableException(ProblemText.Unmapped(error))
    };
}
