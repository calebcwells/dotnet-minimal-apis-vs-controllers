using ApiParity.Api.Domain;

namespace ApiParity.Api.Infrastructure;

/// <summary>
/// Shared <em>wording</em> only, deliberately not a shared error-to-response mapper.
/// Each API style writes its own <c>switch</c> over <see cref="ProposalError"/> in its own idiom
/// (<c>ControllerBase.Problem</c> vs <c>TypedResults.Problem</c>); the parity tests then prove the
/// three independent mappings produce the same HTTP response. Sharing the mapper itself would make
/// that proof circular.
/// </summary>
public static class ProblemText
{
    public const string NotFoundTitle = "Proposal not found.";
    public const string OverLengthTitle = "Proposal exceeds the maximum session length.";
    public const string AlreadyDecidedTitle = "Proposal has already been decided.";

    public static string NotFound(Guid id) => $"No proposal exists with id {id}.";

    public static string OverLength(int limitMinutes, int requestedMinutes) =>
        $"Requested {requestedMinutes} minutes exceeds the maximum session length of {limitMinutes} minutes.";

    public static string AlreadyDecided(ProposalStatus status) =>
        $"The proposal is already {status}.";

    /// <summary>
    /// The message for the discard arm each mapper ends in. It is a bug report, not a client-facing
    /// string. See the note on <see cref="ProposalError"/>.
    /// </summary>
    public static string Unmapped(ProposalError error) =>
        $"No HTTP mapping for {nameof(ProposalError)}.{error.GetType().Name}.";
}
