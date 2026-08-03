using Microsoft.Extensions.Options;

namespace ApiParity.Api.Domain;

/// <summary>
/// The business logic, returning <c>Either&lt;ProposalError, T&gt;</c>. Note what is
/// <em>absent</em>: no <c>ControllerBase</c>, no <c>HttpContext</c>, no <c>IActionResult</c>. That
/// is the point. Neither API style leaks into the layer that holds the actual behavior, which is
/// why one service can sit underneath all three.
/// </summary>
public sealed class ProposalService(
    ProposalStore store,
    IOptions<ConferencePolicyOptions> options,
    ILogger<ProposalService> logger) : IProposalService
{
    public Task<Either<ProposalError, Proposal[]>> ListAsync(
        ProposalStatus? status,
        CancellationToken cancellationToken = default)
    {
        Proposal[] matches = status is null
            ? store.All()
            : store.All().Where(proposal => proposal.Status == status).ToArray();

        // The Right side is Proposal[] rather than IReadOnlyList<Proposal> on purpose: C# does not
        // apply a user-defined implicit conversion when the source is an interface type, so an
        // Either whose Right is an interface has to be constructed explicitly.
        Either<ProposalError, Proposal[]> result = matches;

        return Task.FromResult(result);
    }

    public Task<Either<ProposalError, Proposal>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        Either<ProposalError, Proposal> result = store.Find(id) is { } found
            ? found
            : new ProposalError.NotFound(id);

        return Task.FromResult(result);
    }

    public Task<Either<ProposalError, Proposal>> CreateAsync(
        string speakerName,
        string title,
        int durationMinutes,
        CancellationToken cancellationToken = default)
    {
        int limit = options.Value.MaxSessionMinutes;

        Either<ProposalError, Proposal> result;

        if (durationMinutes > limit)
        {
            // A domain rule DataAnnotations cannot express because it depends on injected configuration.
            // This is the boundary between "validation" and "business rule", and all three API
            // styles have to honor it identically.
            logger.LogInformation(
                "Proposal rejected as over length. Limit: {Limit}, Requested: {Requested}",
                limit,
                durationMinutes);

            result = new ProposalError.OverLength(limit, durationMinutes);
        }
        else
        {
            var created = new Proposal(
                Guid.NewGuid(), speakerName, title, durationMinutes, ProposalStatus.Submitted);
            store.Put(created);

            result = created;
        }

        return Task.FromResult(result);
    }

    public Task<Either<ProposalError, Proposal>> AcceptAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        Either<ProposalError, Proposal> result;

        if (store.Find(id) is not { } existing)
        {
            result = new ProposalError.NotFound(id);
        }
        else if (existing.Status is not ProposalStatus.Submitted)
        {
            result = new ProposalError.AlreadyDecided(existing.Status);
        }
        else
        {
            var accepted = existing with { Status = ProposalStatus.Accepted };

            // Compare-and-swap rather than read-then-write: two concurrent acceptances of the same
            // proposal must not both succeed. Proposal is a record, so TryUpdate compares by value,
            // which gives exactly the "is this still the row I read?" semantics wanted here.
            result = store.TryUpdate(accepted, existing)
                ? accepted
                : new ProposalError.AlreadyDecided(
                    store.Find(id)?.Status ?? ProposalStatus.Accepted);
        }

        return Task.FromResult(result);
    }
}
