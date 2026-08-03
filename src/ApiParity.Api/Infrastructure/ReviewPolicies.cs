namespace ApiParity.Api.Infrastructure;

/// <summary>
/// Policy names shared by all three styles. Controllers reference them via
/// <c>[Authorize(Policy = ...)]</c>; minimal APIs via <c>.RequireAuthorization(...)</c>. Both
/// resolve against the same <c>IAuthorizationService</c> and the same registered policies. There
/// is no separate authorization stack for minimal APIs.
/// </summary>
public static class ReviewPolicies
{
    public const string ScopeClaimType = "scope";

    public const string ReadScope = "proposals:read";
    public const string AcceptScope = "proposals:accept";

    public const string Read = "ProposalsRead";
    public const string Accept = "ProposalsAccept";
}
