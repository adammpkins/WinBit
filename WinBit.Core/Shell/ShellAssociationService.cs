namespace WinBit.Core.Shell;

/// <summary>
/// Registers and inspects WinBit's ownership of <c>.torrent</c> / <c>magnet:</c> in HKCU. Key
/// names and ProgIDs are fixed so subsequent runs can locate and update the same entries.
/// Writes go through <see cref="IAssociationRegistryWriter"/> so tests can verify behavior
/// without touching the live registry.
/// </summary>
public sealed class ShellAssociationService : IShellAssociationService
{
    internal const string TorrentProgId = "WinBit.Torrent";
    internal const string MagnetProgId = "WinBit.Magnet";
    internal const string TorrentExtension = ".torrent";
    internal const string MagnetScheme = "magnet";

    private readonly IAssociationRegistryWriter _writer;
    private readonly string _executablePath;

    public ShellAssociationService(IAssociationRegistryWriter writer, string executablePath)
    {
        _writer = writer;
        _executablePath = executablePath;
    }

    public ShellAssociationStatus GetStatus()
    {
        var torrent = string.Equals(
            _writer.ReadClassDefault(TorrentExtension),
            TorrentProgId,
            StringComparison.OrdinalIgnoreCase);
        var magnet = string.Equals(
            _writer.ReadClassDefault($@"{MagnetScheme}\shell\open\command"),
            BuildShellOpenCommand(),
            StringComparison.OrdinalIgnoreCase);
        return new ShellAssociationStatus(torrent, magnet);
    }

    public Task RegisterAsync(bool torrent, bool magnet, CancellationToken ct = default)
    {
        if (torrent)
        {
            WriteTorrentHandler();
        }
        if (magnet)
        {
            WriteMagnetHandler();
        }
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(bool torrent, bool magnet, CancellationToken ct = default)
    {
        if (torrent)
        {
            _writer.DeleteClassKey(TorrentExtension);
            _writer.DeleteClassKey(TorrentProgId);
        }
        if (magnet)
        {
            _writer.DeleteClassKey(MagnetScheme);
            _writer.DeleteClassKey(MagnetProgId);
        }
        return Task.CompletedTask;
    }

    private void WriteTorrentHandler()
    {
        _writer.WriteClassDefault(TorrentExtension, TorrentProgId);
        _writer.WriteClassDefault(TorrentProgId, "WinBit Torrent");
        _writer.WriteClassDefault($@"{TorrentProgId}\DefaultIcon", $"\"{_executablePath}\",0");
        _writer.WriteClassDefault($@"{TorrentProgId}\shell\open\command", BuildShellOpenCommand());
    }

    private void WriteMagnetHandler()
    {
        // URI schemes are rooted at HKCU\Software\Classes\<scheme>; the URL Protocol value is the
        // sentinel that tells Windows this is a protocol handler rather than a filetype.
        _writer.WriteClassDefault(MagnetScheme, "URL:WinBit Magnet Link");
        _writer.WriteClassValue(MagnetScheme, "URL Protocol", string.Empty);
        _writer.WriteClassDefault($@"{MagnetScheme}\DefaultIcon", $"\"{_executablePath}\",0");
        _writer.WriteClassDefault($@"{MagnetScheme}\shell\open\command", BuildShellOpenCommand());
    }

    internal string BuildShellOpenCommand() => $"\"{_executablePath}\" \"%1\"";
}
