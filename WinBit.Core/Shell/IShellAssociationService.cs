namespace WinBit.Core.Shell;

/// <summary>
/// Registers WinBit as a handler for <c>.torrent</c> files and the <c>magnet:</c> URI scheme in
/// HKCU (per-user, unpackaged-app friendly). The next M11 deliverable (default-client prompt)
/// calls <see cref="RegisterAsync"/>; this interface exists now so the activation path and its
/// tests can land independently.
/// </summary>
public interface IShellAssociationService
{
    /// <summary>Snapshot of which associations currently point at this executable.</summary>
    ShellAssociationStatus GetStatus();

    /// <summary>Writes HKCU class entries for the requested association(s).</summary>
    Task RegisterAsync(bool torrent, bool magnet, CancellationToken ct = default);

    /// <summary>Removes the HKCU class entries previously written by <see cref="RegisterAsync"/>.</summary>
    Task UnregisterAsync(bool torrent, bool magnet, CancellationToken ct = default);
}

public readonly record struct ShellAssociationStatus(bool TorrentFile, bool MagnetProtocol);
