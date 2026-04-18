using Microsoft.Extensions.Options;
using WinBit.Core.Hosting;

namespace WinBit.Core.Persistence;

/// <summary>
/// Resolves on-disk paths under the data root (%LOCALAPPDATA%\WinBit by default). The full
/// directory tree is materialized eagerly in the constructor so subsystems can assume it
/// exists on first run.
/// </summary>
public sealed class Paths
{
    private readonly string _root;

    public Paths(IOptions<WinBitCoreOptions> options)
    {
        _root = options.Value.DataRoot
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinBit");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "rss"));
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
    }

    public string Root => _root;

    public string SettingsFile => Path.Combine(_root, "settings.json");

    public string StateDatabase => Path.Combine(_root, "state.db");

    public string CategoriesFile => Path.Combine(_root, "categories.json");

    public string TagsFile => Path.Combine(_root, "tags.json");

    public string WatchedFoldersFile => Path.Combine(_root, "watched-folders.json");

    public string RssDir => Path.Combine(_root, "rss");

    public string LogsDir => Path.Combine(_root, "logs");
}
