using BenchmarkDotNet.Running;

namespace ApiParity.Benchmarks;

/// <summary>
/// An explicitly named entry point rather than top-level statements. Top-level statements compile to
/// a class called <c>Program</c>, which would shadow the API's own <c>Program</c> and make
/// <c>WebApplicationFactory&lt;Program&gt;</c> bind to this assembly instead of the API's.
/// </summary>
public static class BenchmarkEntryPoint
{
    public static void Main(string[] args) => BenchmarkRunner.Run<PipelineBenchmarks>(args: args);
}
