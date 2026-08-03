using System.Net.Http.Headers;
using ApiParity.Api.Domain;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ApiParity.Benchmarks;

/// <summary>
/// Measures REQUEST-PIPELINE OVERHEAD ONLY, and should be read with that limit in mind.
/// <para>
/// All three styles run in one process against an in-memory <c>TestServer</c> and an in-memory
/// store: no sockets, no TLS, no database. What is left is the part that actually differs:
/// routing, filter invocation, model binding, and result execution. That is the honest scope of the
/// comparison, and it is also why these numbers should NOT be quoted as an application-level
/// speedup: put a 20 ms database call behind any of these endpoints and the gap below is noise.
/// </para>
/// </summary>
[MemoryDiagnoser]
// Generous iteration counts on purpose: at ~15 us per operation this benchmark is well within the
// range where desktop scheduling noise dominates, and a short run produces a standard deviation
// large enough to make the result unquotable.
// invocationCount is pinned rather than left to BenchmarkDotNet's pilot stage. The
// [IterationCleanup] below forces UnrollFactor=1, and BDN's default response to per-iteration
// cleanup is InvocationCount=1, one HTTP call per iteration, which puts the measurement into the
// sub-millisecond range where scheduling noise swamps the signal. Batching 8192 invocations per
// iteration restores a ~100 ms iteration while still letting the store reset between them.
[SimpleJob(warmupCount: 10, iterationCount: 30, invocationCount: 8192)]
// Ratios are only meaningful within a category. Comparing a POST against a GET baseline would
// report a number that looks like a regression and means nothing.
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PipelineBenchmarks
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly string ReadPath = $"/proposals/{ProposalStore.SeededSubmittedId}";

    [GlobalSetup]
    public void Setup()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", "speaker-key");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// The create benchmarks add a record per invocation, so the singleton store would otherwise
    /// grow into the hundreds of thousands of entries over a run and charge its dictionary resizes
    /// to whichever benchmark happened to trigger them. Resetting between iterations keeps it
    /// bounded. This runs outside the measured region, so it costs the results nothing.
    /// </summary>
    [IterationCleanup]
    public void ResetStore() =>
        _factory.Services.GetRequiredService<ProposalStore>().ResetToSeed();

    [BenchmarkCategory("GET one")]
    [Benchmark(Baseline = true, Description = "controller")]
    public async Task ReadController()
    {
        using var response = await _client.GetAsync($"/api/v1/controller{ReadPath}");
    }

    [BenchmarkCategory("GET one")]
    [Benchmark(Description = "minimal API")]
    public async Task ReadMinimal()
    {
        using var response = await _client.GetAsync($"/api/v1/minimal{ReadPath}");
    }

    // The class-based style resolves a scoped handler object from DI on every request, so it is
    // worth measuring separately rather than assuming it costs the same as a static handler.
    [BenchmarkCategory("GET one")]
    [Benchmark(Description = "minimal API (class)")]
    public async Task ReadClass()
    {
        using var response = await _client.GetAsync($"/api/v1/class{ReadPath}");
    }

    [BenchmarkCategory("POST + validation")]
    [Benchmark(Baseline = true, Description = "controller")]
    public async Task CreateController()
    {
        using var payload = Payload();
        using var response = await _client.PostAsync("/api/v1/controller/proposals", payload);
    }

    [BenchmarkCategory("POST + validation")]
    [Benchmark(Description = "minimal API")]
    public async Task CreateMinimal()
    {
        using var payload = Payload();
        using var response = await _client.PostAsync("/api/v1/minimal/proposals", payload);
    }

    [BenchmarkCategory("POST + validation")]
    [Benchmark(Description = "minimal API (class)")]
    public async Task CreateClass()
    {
        using var payload = Payload();
        using var response = await _client.PostAsync("/api/v1/class/proposals", payload);
    }

    private static StringContent Payload()
    {
        var content = new StringContent(
            // A payload that SUCCEEDS. The create path is the one worth measuring: it exercises
            // model binding, DataAnnotations, service dispatch, and (the part that genuinely
            // differs between the styles) 201 result execution with route-based Location generation
            // (CreatedAtRoute vs LinkGenerator). An over-length payload would keep the store bounded
            // too, but it would measure the 422 error path instead and quietly stop covering the
            // difference this benchmark exists to show. ResetStore() above bounds the store without
            // changing the scenario.
            """{"speakerName":"Nadia Brennan","title":"Designing for failure","durationMinutes":45}""");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return content;
    }
}
