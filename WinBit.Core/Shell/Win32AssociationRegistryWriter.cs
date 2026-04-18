using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WinBit.Core.Shell;

/// <summary>
/// Concrete HKCU-backed <see cref="IAssociationRegistryWriter"/>. Per-user writes don't require
/// elevation and match how most modern unpackaged apps declare handlers.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32AssociationRegistryWriter : IAssociationRegistryWriter
{
    private const string ClassesRoot = @"Software\Classes";

    public string? ReadClassDefault(string key)
    {
        using var sub = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\{key}");
        return sub?.GetValue(string.Empty) as string;
    }

    public void WriteClassDefault(string key, string value)
    {
        using var sub = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{key}")
            ?? throw new InvalidOperationException($"Unable to create HKCU\\{ClassesRoot}\\{key}");
        sub.SetValue(string.Empty, value);
    }

    public void WriteClassValue(string key, string name, string value)
    {
        using var sub = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{key}")
            ?? throw new InvalidOperationException($"Unable to create HKCU\\{ClassesRoot}\\{key}");
        sub.SetValue(name, value);
    }

    public void DeleteClassKey(string key)
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{key}", throwOnMissingSubKey: false);
    }
}
