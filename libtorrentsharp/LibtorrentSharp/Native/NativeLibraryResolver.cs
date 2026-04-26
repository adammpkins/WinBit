using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibtorrentSharp.Native;

internal static class NativeLibraryResolver
{
    private const string LibraryName = "lts";

    [ModuleInitializer]
    internal static void Initialize()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryResolver).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var baseDir = Path.GetDirectoryName(assembly.Location);
        if (string.IsNullOrEmpty(baseDir))
        {
            return IntPtr.Zero;
        }

        var fileName = NativeFileName();

        string[] candidates =
        {
            Path.Combine(baseDir, "runtimes", CurrentRid(), "native", fileName),
            Path.Combine(baseDir, fileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string NativeFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return LibraryName + ".dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "lib" + LibraryName + ".dylib";
        }

        return "lib" + LibraryName + ".so";
    }

    private static string CurrentRid()
    {
        var os = OperatingSystem.IsWindows() ? "win"
            : OperatingSystem.IsMacOS() ? "osx"
            : "linux";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
        };

        return os + "-" + arch;
    }
}
