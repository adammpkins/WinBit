using WinBit.Core.Settings;

namespace WinBit.Core.Shell;

/// <summary>
/// Encapsulates the "should we offer to make WinBit the default handler?" decision so the UI
/// layer stays a thin dispatcher. The prompt fires exactly once per installation: when either
/// .torrent or magnet: isn't owned by this executable AND the user hasn't previously dismissed
/// the prompt. Manual registration from Settings ignores this policy and goes through the
/// association service directly.
/// </summary>
public static class DefaultClientPromptPolicy
{
    public static bool ShouldPrompt(ShellAssociationStatus status, BehaviorSettings behavior)
    {
        if (behavior.DefaultClientPromptDismissed)
        {
            return false;
        }
        return !status.TorrentFile || !status.MagnetProtocol;
    }
}
