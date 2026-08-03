using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ApiParity.Api.Infrastructure;

namespace ApiParity.Tests;

/// <summary>
/// The three route prefixes under comparison. Everything below the transport layer (service,
/// store, policies, ProblemDetails configuration) is shared, so a difference in response is
/// attributable to the controller-vs-minimal choice and nothing else.
/// </summary>
public static class ApiStyle
{
    /// <summary>MVC controller with attribute routing.</summary>
    public const string Controller = "/api/v1/controller/proposals";

    /// <summary>Minimal API, static handler methods.</summary>
    public const string Minimal = "/api/v1/minimal/proposals";

    /// <summary>Minimal API, instance methods on a DI-resolved class.</summary>
    public const string Class = "/api/v1/class/proposals";

    /// <summary>
    /// The controller is the baseline every other style is compared against. It is the shape most
    /// teams already trust.
    /// </summary>
    public static readonly string[] Alternatives = [Minimal, Class];
}

/// <summary>A captured response, reduced to the parts a contract comparison should care about.</summary>
public sealed record Captured(
    int StatusCode,
    string? MediaType,
    string? Location,
    string? CorrelationId,
    JsonNode? Body)
{
    /// <summary>
    /// The body with per-request diagnostics stripped. <c>traceId</c> is unique to every request by
    /// design, so comparing raw bodies would always fail; removing it is the only normalization
    /// these tests perform.
    /// </summary>
    public JsonNode? ComparableBody()
    {
        if (Body is not JsonObject obj)
        {
            return Body;
        }

        var clone = (JsonObject)obj.DeepClone();
        clone.Remove("traceId");
        return clone;
    }

    public string ComparableJson() => ComparableBody()?.ToJsonString() ?? "null";
}

public static class HttpExtensions
{
    public const string SpeakerKey = "speaker-key";
    public const string ReviewerKey = "reviewer-key";

    public static async Task<Captured> CaptureAsync(this HttpClient client, HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);
        string raw = await response.Content.ReadAsStringAsync();

        JsonNode? body = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);

        return new Captured(
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.Location?.ToString(),
            response.Headers.TryGetValues(CorrelationId.HeaderName, out var values)
                ? values.FirstOrDefault()
                : null,
            body);
    }

    public static HttpRequestMessage WithKey(this HttpRequestMessage request, string? apiKey)
    {
        if (apiKey is not null)
        {
            request.Headers.Add(ApiKeyAuthenticationHandler.HeaderName, apiKey);
        }

        return request;
    }

    public static HttpRequestMessage WithJson(this HttpRequestMessage request, string json)
    {
        request.Content = new StringContent(json);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return request;
    }

    public static HttpRequestMessage Get(string url) => new(HttpMethod.Get, url);

    public static HttpRequestMessage Post(string url) => new(HttpMethod.Post, url);
}
