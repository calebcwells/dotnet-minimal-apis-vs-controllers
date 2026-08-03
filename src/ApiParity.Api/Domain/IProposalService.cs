namespace ApiParity.Api.Domain;

public interface IProposalService
{
    Task<Either<ProposalError, Proposal[]>> ListAsync(
        ProposalStatus? status,
        CancellationToken cancellationToken = default);

    Task<Either<ProposalError, Proposal>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<Either<ProposalError, Proposal>> CreateAsync(
        string speakerName,
        string title,
        int durationMinutes,
        CancellationToken cancellationToken = default);

    Task<Either<ProposalError, Proposal>> AcceptAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
