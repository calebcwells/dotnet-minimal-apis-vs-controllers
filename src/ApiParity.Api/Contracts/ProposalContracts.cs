using System.ComponentModel.DataAnnotations;
using ApiParity.Api.Domain;

namespace ApiParity.Api.Contracts;

// The request DTO, annotated once and consumed by all three API styles. Controllers run these
// attributes because of [ApiController]; minimal APIs run the same attributes because of
// builder.Services.AddValidation(), which is new in .NET 10 and closed the last real
// parity gap between the two styles. On .NET 8 or 9 this needed a hand-written endpoint filter.
//
// The properties are deliberately not `required`: with `required`, System.Text.Json rejects a
// missing member before validation ever runs, which would mask the behavior being demonstrated.
//
// The XML summaries here and below are terse on purpose. .NET 10 copies XML doc comments into the
// OpenAPI document, so anything in a <summary> on these types is consumer-facing text in the API
// reference UI. Notes meant for code readers belong in comments like this one.
/// <summary>A conference talk proposal submission.</summary>
public sealed record CreateProposalDto
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(100, MinimumLength = 2)]
    public string SpeakerName { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(120, MinimumLength = 2)]
    public string Title { get; init; } = string.Empty;

    // The [Range] is a static bound only. The conference's actual maximum session length is
    // configuration, so it lives in ConferencePolicyOptions and is enforced in the service. See
    // the 422 path there.
    /// <summary>Requested session length in minutes.</summary>
    [Range(5, 480)]
    public int DurationMinutes { get; init; }
}

/// <summary>A proposal as visible to API clients.</summary>
public sealed record ProposalResponse(
    Guid Id,
    string SpeakerName,
    string Title,
    int DurationMinutes,
    string Status)
{
    public static ProposalResponse From(Proposal proposal) => new(
        proposal.Id,
        proposal.SpeakerName,
        proposal.Title,
        proposal.DurationMinutes,
        proposal.Status.ToString());
}
