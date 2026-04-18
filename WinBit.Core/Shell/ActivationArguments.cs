namespace WinBit.Core.Shell;

/// <summary>
/// Parses a launched process's command-line arguments (or an activation argument string) into a
/// single piece of work: either a <c>.torrent</c> file path, a <c>magnet:</c> URI, or neither.
/// Shell-routed file activations quote the path as the first positional argument, and protocol
/// activations pass the URI as the first argument, so we honor whichever form we see first.
/// </summary>
public sealed record ActivationArguments(string? TorrentFilePath, string? MagnetUri)
{
    public static ActivationArguments None { get; } = new(null, null);

    public bool HasWork => TorrentFilePath is not null || MagnetUri is not null;

    public static ActivationArguments Parse(IReadOnlyList<string> args)
    {
        // WinUI passes the bare argument payload (no leading exe path), but when the app is
        // launched via explorer the first slot holds the exe path or a flag — we iterate and
        // take the first arg that looks like a magnet URI or a .torrent path.
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }
            var arg = raw.Trim().Trim('"');
            if (arg.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                return new ActivationArguments(null, arg);
            }
            if (arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            {
                return new ActivationArguments(arg, null);
            }
        }
        return None;
    }

    /// <summary>
    /// Splits a single activation string (as produced by WinUI's
    /// <c>LaunchActivatedEventArgs.Arguments</c>) into individual arguments while honoring
    /// standard Windows double-quote grouping — "C:\My Files\foo.torrent" becomes one arg.
    /// </summary>
    public static ActivationArguments ParseCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return None;
        }
        return Parse(SplitRespectingQuotes(commandLine));
    }

    private static List<string> SplitRespectingQuotes(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in input)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }
        return result;
    }
}
