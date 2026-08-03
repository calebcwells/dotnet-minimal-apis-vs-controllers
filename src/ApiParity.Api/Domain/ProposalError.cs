namespace ApiParity.Api.Domain;

/// <summary>
/// Typed error hierarchy: a discriminated union in all but name. The service returns these as the
/// Left side of an <see cref="Either{TLeft,TRight}"/>; each API style maps them to HTTP
/// independently, which is what makes the parity tests worth running.
/// <para>
/// Every case here is reachable and covered by a parity test. Resist adding one the service cannot
/// actually produce: the three mappers would each grow an untested branch, and an HTTP response no
/// test exercises is exactly the kind of undocumented claim this repository argues against.
/// </para>
/// <para>
/// The constructor is <c>private protected</c> so the set of cases is closed. C# still cannot prove
/// exhaustiveness over a class hierarchy (a <c>switch</c> without a discard arm warns CS8509 even
/// here), so each mapper ends in a discard that throws rather than inventing a status code.
/// </para>
/// </summary>
public abstract record ProposalError
{
    private protected ProposalError() { }

    public sealed record NotFound(Guid Id) : ProposalError;

    public sealed record OverLength(int LimitMinutes, int RequestedMinutes) : ProposalError;

    public sealed record AlreadyDecided(ProposalStatus Status) : ProposalError;
}
