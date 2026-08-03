using System.Collections.Concurrent;

namespace ApiParity.Api.Domain;

/// <summary>
/// Singleton in-memory store. Registered as a singleton while the service that consumes it is
/// scoped, which is exactly the shape a DI demonstration needs, and all three API styles resolve
/// it through the same container.
/// </summary>
public sealed class ProposalStore
{
    /// <summary>Seeded ids are stable so the tests and the README can reference them directly.</summary>
    public static readonly Guid SeededSubmittedId = new("11111111-1111-1111-1111-111111111111");

    public static readonly Guid SeededAcceptedId = new("22222222-2222-2222-2222-222222222222");

    private readonly ConcurrentDictionary<Guid, Proposal> _proposals = new();

    public ProposalStore() => Seed();

    /// <summary>
    /// Drops everything and restores the seeded rows. Exists for the benchmark harness: the create
    /// benchmark adds a record on every invocation, so without a reset between iterations the
    /// dictionary grows into the hundreds of thousands of entries and its resize allocations start
    /// showing up in the measurement.
    /// </summary>
    public void ResetToSeed()
    {
        _proposals.Clear();
        Seed();
    }

    private void Seed()
    {
        _proposals[SeededSubmittedId] = new Proposal(
            SeededSubmittedId, "Priya Raman", "Structured concurrency in practice", 45, ProposalStatus.Submitted);
        _proposals[SeededAcceptedId] = new Proposal(
            SeededAcceptedId, "Tomas Lindqvist", "What your profiler is not telling you", 30, ProposalStatus.Accepted);
    }

    public Proposal[] All() =>
        _proposals.Values.OrderBy(proposal => proposal.SpeakerName, StringComparer.Ordinal).ToArray();

    public Proposal? Find(Guid id) => _proposals.TryGetValue(id, out var proposal) ? proposal : null;

    public void Put(Proposal proposal) => _proposals[proposal.Id] = proposal;

    public bool TryUpdate(Proposal proposal, Proposal expected) =>
        _proposals.TryUpdate(proposal.Id, proposal, expected);
}
