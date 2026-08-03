using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ApiParity.Api.Infrastructure;

/// <summary>
/// A deliberately tiny <c>X-Api-Key</c> scheme so the demo can exercise real 401/403 behavior
/// without standing up an identity provider. Authentication is middleware. It sits below all
/// three API styles and is unaware of any of them.
/// </summary>
/// <remarks>
/// Hard-coded keys compared with a dictionary lookup are fine for a demonstration and are not a
/// pattern for production. Real deployments want a proper scheme (OAuth/OIDC, or at minimum keys
/// that are hashed, rotatable, and compared in constant time).
/// </remarks>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    public const string SpeakerKey = "speaker-key";
    public const string ReviewerKey = "reviewer-key";

    private static readonly Dictionary<string, (string Name, string[] Scopes)> KnownKeys = new(StringComparer.Ordinal)
    {
        [SpeakerKey] = ("priya.raman", [ReviewPolicies.ReadScope]),
        [ReviewerKey] = ("tomas.lindqvist", [ReviewPolicies.ReadScope, ReviewPolicies.AcceptScope])
    };

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var provided) || provided.Count == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (provided[0] is not { } key || !KnownKeys.TryGetValue(key, out var principalData))
        {
            // Never log the presented key itself. Log a correlation-safe message only.
            Logger.LogWarning("Rejected an unrecognized API key on {Path}", Request.Path);
            return Task.FromResult(AuthenticateResult.Fail("Unrecognized API key."));
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, principalData.Name),
            new(ClaimTypes.Name, principalData.Name),
            .. principalData.Scopes.Select(scope => new Claim(ReviewPolicies.ScopeClaimType, scope))
        ];

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
