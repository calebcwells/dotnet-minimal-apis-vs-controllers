namespace ApiParity.Api.Domain;

/// <summary>
/// Bound with <c>IOptions&lt;T&gt;</c> so the demo exercises the options pattern, not just plain
/// interface injection. All three API styles resolve it identically, via the service.
/// </summary>
public sealed class ConferencePolicyOptions
{
    public const string SectionName = "ConferencePolicy";

    /// <summary>
    /// The longest session this conference will schedule. Note this is configuration, not a
    /// constant, which is exactly why it cannot be expressed as a DataAnnotation on the request
    /// DTO, and why it makes a good example of the boundary between input validation and a
    /// business rule.
    /// </summary>
    public int MaxSessionMinutes { get; set; } = 60;
}
