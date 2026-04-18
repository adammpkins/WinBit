namespace WinBit.Core.Shell;

/// <summary>
/// Abstraction over the small slice of the Windows registry we touch for shell associations.
/// <see cref="Win32AssociationRegistryWriter"/> hits HKCU for real; tests swap in an in-memory
/// fake to avoid polluting the host registry.
/// </summary>
public interface IAssociationRegistryWriter
{
    /// <summary>Reads the (Default) value of HKCU\Software\Classes\{key}, or null when missing.</summary>
    string? ReadClassDefault(string key);

    /// <summary>Writes the (Default) value of HKCU\Software\Classes\{key}, creating the key if needed.</summary>
    void WriteClassDefault(string key, string value);

    /// <summary>Writes a named value under HKCU\Software\Classes\{key}, creating the key if needed.</summary>
    void WriteClassValue(string key, string name, string value);

    /// <summary>Deletes the HKCU\Software\Classes\{key} subtree. Idempotent.</summary>
    void DeleteClassKey(string key);
}
