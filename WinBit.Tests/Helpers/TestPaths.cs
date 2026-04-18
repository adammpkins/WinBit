using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;
using WinBit.Core.Persistence;

namespace WinBit.Tests.Helpers;

/// <summary>
/// Shared helper that builds a <see cref="Paths"/> rooted in a caller-owned temp directory.
/// Most Web UI endpoint tests don't trip the cert-file code path, so they can reuse a single
/// shared instance rather than each spinning up its own <see cref="TempDirectory"/>.
/// </summary>
public static class TestPaths
{
    public static Paths ForTemp(TempDirectory temp) =>
        new(Options.Create(new WinBitCoreOptions { DataRoot = temp.Path }));

    /// <summary>Path rooted at the system temp dir — fine when the test never creates files.</summary>
    public static Paths Ambient { get; } =
        new(Options.Create(new WinBitCoreOptions { DataRoot = Path.GetTempPath() }));
}
