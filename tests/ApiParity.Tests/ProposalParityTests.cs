using System.Text.Json.Nodes;
using ApiParity.Api.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ApiParity.Tests.HttpExtensions;

namespace ApiParity.Tests;

/// <summary>
/// The evidence for "minimal APIs can do everything controllers can".
/// <para>
/// Each test drives the SAME scenario through THREE implementations (an MVC controller, a minimal
/// API with static handlers, and a minimal API with instance methods on a DI-resolved class) and
/// asserts the responses agree. The controller is the baseline; the other two are compared against
/// it.
/// </para>
/// <para>
/// All three map their errors independently (see the comment on <c>ProblemText</c>), so these
/// compare behavior rather than asserting that shared code equals itself.
/// </para>
/// <para>
/// Where a genuine difference exists, a test ASSERTS the difference rather than a comment claiming
/// parity. Those are grouped at the bottom under "documented divergences".
/// </para>
/// </summary>
public sealed class ProposalParityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private const string ValidPayload =
        """{"speakerName":"Nadia Brennan","title":"Designing for failure","durationMinutes":45}""";

    private const string OverLengthPayload =
        """{"speakerName":"Nadia Brennan","title":"Designing for failure","durationMinutes":90}""";

    private const string InvalidPayload =
        """{"speakerName":"","title":"X","durationMinutes":0}""";

    // ─── Authorization ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_credentials_are_rejected_identically()
    {
        var responses = await AllStylesAsync(style => Get(style).WithKey(null));

        responses.Baseline.StatusCode.ShouldBe(401);
        responses.AssertAllMatchBaseline();
    }

    [Fact]
    public async Task An_unrecognized_api_key_is_rejected_identically()
    {
        var responses = await AllStylesAsync(style => Get(style).WithKey("not-a-real-key"));

        responses.Baseline.StatusCode.ShouldBe(401);
        responses.AssertAllMatchBaseline();
    }

    /// <summary>
    /// The load-bearing authorization test: the speaker key satisfies the group/class-level read
    /// policy but not the endpoint/method-level accept policy. Every style must enforce the stricter
    /// of the two layered policies.
    /// </summary>
    [Fact]
    public async Task An_insufficient_scope_is_forbidden_identically()
    {
        var responses = await AllStylesAsync(style =>
            Post($"{style}/{ProposalStore.SeededSubmittedId}/acceptance").WithKey(SpeakerKey));

        responses.Baseline.StatusCode.ShouldBe(403);
        responses.AssertAllMatchBaseline();
    }

    [Fact]
    public async Task A_sufficient_scope_is_allowed_identically()
    {
        foreach (string style in AllStyles)
        {
            // A fresh submitted proposal per style, so no acceptance collides with another's.
            var id = await CreateSubmittedAsync(style);

            var accepted = await _client.CaptureAsync(
                Post($"{style}/{id}/acceptance").WithKey(ReviewerKey));

            accepted.StatusCode.ShouldBe(200, $"{style} should allow a reviewer");
            accepted.Body!["status"]!.GetValue<string>().ShouldBe("Accepted");
        }
    }

    [Fact]
    public async Task Only_one_concurrent_acceptance_can_succeed()
    {
        var store = new ProposalStore();
        var service = new ProposalService(
            store,
            Options.Create(new ConferencePolicyOptions()),
            NullLogger<ProposalService>.Instance);
        using var start = new ManualResetEventSlim();

        Task<bool>[] acceptances = Enumerable.Range(0, 256)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                return (await service.AcceptAsync(ProposalStore.SeededSubmittedId)).IsRight;
            }))
            .ToArray();

        start.Set();
        bool[] results = await Task.WhenAll(acceptances);

        results.Count(succeeded => succeeded).ShouldBe(1);
    }

    // ─── Error handling ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_missing_resource_produces_the_same_problem_details()
    {
        var missing = Guid.NewGuid();
        var responses = await AllStylesAsync(style => Get($"{style}/{missing}").WithKey(SpeakerKey));

        responses.Baseline.StatusCode.ShouldBe(404);
        responses.AssertAllMatchBaseline();
        responses.Baseline.Body!["detail"]!.GetValue<string>().ShouldContain(missing.ToString());
    }

    [Fact]
    public async Task A_conflicting_state_change_produces_the_same_problem_details()
    {
        var responses = await AllStylesAsync(style =>
            Post($"{style}/{ProposalStore.SeededAcceptedId}/acceptance").WithKey(ReviewerKey));

        responses.Baseline.StatusCode.ShouldBe(409);
        responses.AssertAllMatchBaseline();
    }

    [Fact]
    public async Task A_business_rule_failure_produces_the_same_problem_details()
    {
        var responses = await AllStylesAsync(style =>
            Post(style).WithKey(SpeakerKey).WithJson(OverLengthPayload));

        responses.Baseline.StatusCode.ShouldBe(422);
        responses.AssertAllMatchBaseline();
    }

    [Fact]
    public async Task An_unhandled_exception_produces_the_same_problem_details()
    {
        var responses = await AllStylesAsync(style => Get($"{style}/boom").WithKey(SpeakerKey));

        responses.Baseline.StatusCode.ShouldBe(500);
        responses.AssertAllMatchBaseline();

        // The exception message must not reach the client in any style.
        foreach (var captured in responses.All)
        {
            captured.ComparableJson().ShouldNotContain("Deliberate demo failure");
        }
    }

    // ─── DataAnnotations validation ──────────────────────────────────────────────────────

    /// <summary>
    /// The question most likely to draw skepticism. Controllers get this from
    /// <c>[ApiController]</c>; both minimal styles get it from <c>AddValidation()</c>, new in
    /// .NET 10. Same status, same RFC type, same title, same offending fields.
    /// </summary>
    [Fact]
    public async Task Data_annotations_are_enforced_by_every_style()
    {
        var responses = await AllStylesAsync(style =>
            Post(style).WithKey(SpeakerKey).WithJson(InvalidPayload));

        foreach (var captured in responses.All)
        {
            captured.StatusCode.ShouldBe(400);
            captured.MediaType.ShouldBe("application/problem+json");
            captured.Body!["title"]!.GetValue<string>()
                .ShouldBe(responses.Baseline.Body!["title"]!.GetValue<string>());
            captured.Body!["type"]!.GetValue<string>()
                .ShouldBe(responses.Baseline.Body!["type"]!.GetValue<string>());
            ErrorKeys(captured).ShouldBe(["DurationMinutes", "SpeakerName", "Title"]);
        }
    }

    [Fact]
    public async Task A_valid_payload_is_accepted_by_every_style()
    {
        var responses = await AllStylesAsync(style =>
            Post(style).WithKey(SpeakerKey).WithJson(ValidPayload));

        foreach ((string style, Captured created) in AllStyles.Zip(responses.All))
        {
            created.StatusCode.ShouldBe(201, $"{style} should create");

            // MVC's CreatedAtRoute and the minimal styles' LinkGenerator all produce an absolute URL
            // off the same route table. Each response points at the resource IT created, so each is
            // checked against its own id.
            created.Location.ShouldNotBeNull();
            var location = new Uri(created.Location!);
            location.IsAbsoluteUri.ShouldBeTrue();
            location.AbsolutePath.ShouldBe($"{style}/{created.Body!["id"]!.GetValue<Guid>()}");

            // And the generated URL is real: following it returns the resource.
            var followed = await _client.CaptureAsync(Get(location.AbsolutePath).WithKey(SpeakerKey));
            followed.StatusCode.ShouldBe(200);
            followed.Body!["speakerName"]!.GetValue<string>().ShouldBe("Nadia Brennan");
        }
    }

    // ─── Reads, filters and cross-cutting concerns ───────────────────────────────────────

    [Fact]
    public async Task Reads_return_the_same_payload()
    {
        var responses = await AllStylesAsync(style =>
            Get($"{style}/{ProposalStore.SeededSubmittedId}").WithKey(SpeakerKey));

        responses.Baseline.StatusCode.ShouldBe(200);
        responses.AssertAllMatchBaseline();
    }

    [Fact]
    public async Task Query_string_binding_and_filtering_behave_the_same()
    {
        var responses = await AllStylesAsync(style => Get($"{style}?status=Accepted").WithKey(SpeakerKey));

        responses.Baseline.StatusCode.ShouldBe(200);
        responses.AssertAllMatchBaseline();
        responses.Baseline.Body!.AsArray().Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// An MVC action filter and a minimal-API endpoint filter, doing the same job, attached at the
    /// class/group level in each style.
    /// </summary>
    [Fact]
    public async Task Group_wide_filters_run_for_every_style()
    {
        var responses = await AllStylesAsync(style =>
        {
            var request = Get(style).WithKey(SpeakerKey);
            request.Headers.Add("X-Correlation-Id", "demo-correlation-1");
            return request;
        });

        foreach (var captured in responses.All)
        {
            captured.CorrelationId.ShouldBe("demo-correlation-1");
        }
    }

    [Fact]
    public async Task Every_style_is_described_by_the_same_openapi_document()
    {
        var document = await _client.CaptureAsync(Get("/openapi/v1.json"));
        document.StatusCode.ShouldBe(200);

        var paths = document.Body!["paths"]!.AsObject();

        foreach (string template in new[] { "", "/{id}", "/{id}/acceptance" })
        {
            string baselinePath = $"{ApiStyle.Controller}{template}";
            paths[baselinePath].ShouldNotBeNull($"{baselinePath} is missing from the document");

            foreach (string style in ApiStyle.Alternatives)
            {
                string path = $"{style}{template}";
                paths[path].ShouldNotBeNull($"{path} is missing from the document");

                // Every style must advertise the same set of status codes per operation.
                ResponseCodes(paths[path]!).ShouldBe(ResponseCodes(paths[baselinePath]!));
            }
        }
    }

    // Regression guard: response codes belong to an operation, not merely to a path.

    [Fact]
    public void Openapi_response_code_comparison_is_operation_specific()
    {
        var baseline = JsonNode.Parse(
            """{"get":{"responses":{"200":{}}},"post":{"responses":{"201":{}}}}""")!;
        var swapped = JsonNode.Parse(
            """{"get":{"responses":{"201":{}}},"post":{"responses":{"200":{}}}}""")!;

        ResponseCodes(swapped).ShouldNotBe(ResponseCodes(baseline));
    }

    // ─── Handler shape: static vs instance methods ───────────────────────────────────────

    /// <summary>
    /// The class-based style is not a second-class citizen. It passes every parity test above. This
    /// test pins the thing that actually differs: how the handler instance is obtained.
    /// <para>
    /// A handler resolved from DI per request gets a fresh scoped instance. A handler bound as a
    /// method group over an instance created at startup is served by that ONE instance forever,
    /// regardless of how the type is registered. That is the captive-dependency trap.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_di_resolved_handler_is_scoped_but_a_captured_instance_is_not()
    {
        var firstResolved = await _client.CaptureAsync(Get("/diagnostics/handler-lifetime/resolved"));
        var secondResolved = await _client.CaptureAsync(Get("/diagnostics/handler-lifetime/resolved"));

        var firstCaptured = await _client.CaptureAsync(Get("/diagnostics/handler-lifetime/captured"));
        var secondCaptured = await _client.CaptureAsync(Get("/diagnostics/handler-lifetime/captured"));

        static string InstanceId(Captured captured) => captured.Body!["instanceId"]!.GetValue<string>();

        // Resolved from DI: a new scoped instance per request, as registered.
        InstanceId(firstResolved).ShouldNotBe(InstanceId(secondResolved));

        // Captured at startup: the same instance serves every request. If it held a scoped
        // dependency, that dependency would outlive its scope.
        InstanceId(firstCaptured).ShouldBe(InstanceId(secondCaptured));
    }

    // ─── Documented divergences ──────────────────────────────────────────────────────────
    // Real differences, asserted rather than described, so that if a future .NET release closes one
    // of these gaps the test fails and the README gets corrected.

    /// <summary>
    /// MVC reports every failing DataAnnotation on a property; the .NET 10 minimal-API validator
    /// stops at the first failure per property. Same fields flagged, fewer messages each. This is a
    /// property of the validator, so it applies to BOTH minimal styles.
    /// </summary>
    [Fact]
    public async Task Divergence_mvc_reports_every_failed_attribute_per_property()
    {
        var responses = await AllStylesAsync(style =>
            Post(style).WithKey(SpeakerKey).WithJson(InvalidPayload));

        // SpeakerName is empty, so it violates both [Required] and [StringLength].
        static int MessageCount(Captured captured) =>
            captured.Body!["errors"]!["SpeakerName"]!.AsArray().Count;

        MessageCount(responses.Baseline).ShouldBe(2);

        foreach (var alternative in responses.Alternatives)
        {
            MessageCount(alternative).ShouldBe(1);
        }
    }

    /// <summary>
    /// MVC's output formatter appends <c>; charset=utf-8</c> to problem responses it writes itself;
    /// the minimal-API ProblemDetails writer does not. The media type is identical and UTF-8 is the
    /// JSON default, so this is cosmetic, but it is a real byte difference and a strict client-side
    /// content-type comparison would notice.
    /// </summary>
    [Fact]
    public async Task Divergence_mvc_appends_a_charset_to_problem_responses()
    {
        var missing = Guid.NewGuid();

        using var controllerResponse = await _client.SendAsync(
            Get($"{ApiStyle.Controller}/{missing}").WithKey(SpeakerKey));
        controllerResponse.Content.Headers.ContentType!.ToString()
            .ShouldBe("application/problem+json; charset=utf-8");

        foreach (string style in ApiStyle.Alternatives)
        {
            using var response = await _client.SendAsync(Get($"{style}/{missing}").WithKey(SpeakerKey));
            response.Content.Headers.ContentType!.ToString().ShouldBe("application/problem+json");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────

    private static readonly string[] AllStyles = [ApiStyle.Controller, .. ApiStyle.Alternatives];

    /// <summary>Responses for one scenario across every style, in <see cref="AllStyles"/> order.</summary>
    private sealed record StyleResponses(Captured[] All)
    {
        public Captured Baseline => All[0];

        public IEnumerable<Captured> Alternatives => All.Skip(1);

        public void AssertAllMatchBaseline()
        {
            foreach ((string style, Captured captured) in ApiStyle.Alternatives.Zip(Alternatives))
            {
                captured.StatusCode.ShouldBe(Baseline.StatusCode, $"{style} status");
                captured.MediaType.ShouldBe(Baseline.MediaType, $"{style} content type");
                captured.ComparableJson().ShouldBe(Baseline.ComparableJson(), $"{style} body");
            }
        }
    }

    private async Task<StyleResponses> AllStylesAsync(Func<string, HttpRequestMessage> build)
    {
        var captured = new List<Captured>(AllStyles.Length);

        // Captured back to back so the shared in-memory store cannot drift between them.
        foreach (string style in AllStyles)
        {
            captured.Add(await _client.CaptureAsync(build(style)));
        }

        return new StyleResponses([.. captured]);
    }

    private async Task<Guid> CreateSubmittedAsync(string style)
    {
        var created = await _client.CaptureAsync(Post(style).WithKey(SpeakerKey).WithJson(ValidPayload));
        created.StatusCode.ShouldBe(201);

        return created.Body!["id"]!.GetValue<Guid>();
    }

    private static string[] ErrorKeys(Captured captured) =>
        captured.Body!["errors"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();

    private static string[] ResponseCodes(JsonNode pathItem) =>
        pathItem.AsObject()
            .SelectMany(operation => operation.Value!["responses"]!.AsObject()
                .Select(response => $"{operation.Key}:{response.Key}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
