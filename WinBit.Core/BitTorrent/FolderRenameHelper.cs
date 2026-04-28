using System.Collections.Generic;

namespace WinBit.Core.BitTorrent;

public static class FolderRenameHelper
{
    public static IEnumerable<(int Index, string NewRelativePath)> BuildRenamedPaths(
        IEnumerable<TorrentFileEntry> files,
        string oldFolderPath,
        string newFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldFolderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFolderPath);

        string prefix = oldFolderPath.TrimEnd('/') + "/";
        string newBase = newFolderPath.TrimEnd('/') + "/";

        foreach (var file in files)
        {
            if (file.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
                yield return (file.Index, newBase + file.RelativePath[prefix.Length..]);
        }
    }
}
