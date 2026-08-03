namespace ApiParity.Api.Domain;

public enum ProposalStatus
{
    Submitted = 0,
    Accepted = 1,
    Declined = 2
}

/// <summary>
/// The domain entity: a conference talk proposal. Deliberately shared by all three API styles:
/// this repository varies the <em>transport layer only</em>, so any behavioral difference the
/// parity tests find is attributable to minimal APIs vs controllers and nothing else.
/// </summary>
public sealed record Proposal(
    Guid Id,
    string SpeakerName,
    string Title,
    int DurationMinutes,
    ProposalStatus Status);
