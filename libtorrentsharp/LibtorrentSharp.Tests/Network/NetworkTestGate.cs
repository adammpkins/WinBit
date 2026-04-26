using System;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// Per-test gate for the Phase C public-swarm integration tests. These tests dial out
/// to public DHT routers and rely on a live BitTorrent swarm — slow, brittle on
/// air-gapped CI runners, and expensive to leave on by default. Tests in
/// <see cref="PublicSwarmTests"/> open the gate via this helper and short-circuit
/// when it's closed, which keeps the default <c>dotnet test</c> run fast and keeps
/// the <c>Category=Network</c> trait the only thing that matters for opt-in.
/// </summary>
internal static class NetworkTestGate
{
    private const string EnvVar = "WINBIT_NETWORK_TESTS";

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnvVar) == "1";

    /// <summary>
    /// Returns <c>true</c> if the caller should proceed with the network test, or
    /// <c>false</c> when the test should silently early-return. xunit v2 has no
    /// runtime <c>Assert.Skip</c> and SkippableFact would add a NuGet dep that
    /// central-package-management doesn't currently track — early-return is the
    /// pragmatic compromise. Run the tests for real with:
    /// <code>
    /// $env:WINBIT_NETWORK_TESTS = "1"
    /// dotnet test libtorrentsharp/LibtorrentSharp.Tests/LibtorrentSharp.Tests.csproj `
    ///   --filter "Category=Network"
    /// </code>
    /// </summary>
    public static bool ShouldRun() => IsEnabled;
}
