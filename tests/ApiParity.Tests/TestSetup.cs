// Xunit is a global using. See the <Using Include="Xunit" /> item in ApiParity.Tests.csproj.

// The parity tests share one application instance (and therefore one in-memory store), and several
// of them compare responses captured back to back. Running them sequentially keeps those groups
// adjacent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
