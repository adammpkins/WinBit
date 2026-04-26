// Derived from csdl by Albie Spriddell. See libtorrentsharp/NOTICE for attribution.
// csdl - a cross-platform libtorrent wrapper for .NET
// Licensed under Apache-2.0 - see the license file for more information

using LibtorrentSharp.Enums;

namespace LibtorrentSharp;

/// <summary>
/// Convenience configuration object for constructing a <see cref="LibtorrentSession"/>.
/// Each property maps to one or more keys in libtorrent's <c>settings_pack</c>
/// (see <see cref="Build"/> for the exact mapping); only properties whose backing
/// value is non-default get applied, so leaving fields unset preserves
/// libtorrent's defaults rather than overriding them with empty strings or
/// zero values. For finer-grained control over settings not surfaced here,
/// pass a hand-built <see cref="SettingsPack"/> directly.
/// </summary>
public class LibtorrentSessionConfig
{
    /// <summary>HTTP <c>User-Agent</c> header sent on tracker announces (libtorrent <c>user_agent</c> key). Empty/null skips the override and keeps libtorrent's default ("libtorrent/&lt;version&gt;"). Some private trackers reject unknown user agents — set this to your client's name+version to satisfy their policy.</summary>
    public string UserAgent { get; set; }

    /// <summary>Peer-ID prefix announced to other peers (libtorrent <c>peer_fingerprint</c> key). Conventionally a client-identification prefix like <c>-LT2010-</c>; libtorrent generates the random suffix. Empty/null skips the override.</summary>
    public string Fingerprint { get; set; }

    /// <summary>Enable libtorrent's anonymous mode (<c>anonymous_mode</c> key) — disables features that leak the client's IP/identity (e.g. tracker outgoing IP reporting, LSD broadcasts). Forces all peer/tracker traffic through the configured proxy when set; falls open if no proxy is configured.</summary>
    public bool PrivateMode { get; set; }

    /// <summary>When <c>true</c>, libtorrent stops dialing outbound connections from torrents that are seeding (inverted into <c>seeding_outgoing_connections=false</c> in the resulting <see cref="SettingsPack"/>). Useful for tight-NAT scenarios where outbound seeding adds connection-tracking churn without finding new peers.</summary>
    public bool BlockSeeding { get; set; }

    /// <summary>When <c>true</c>, sets both <c>out_enc_policy</c> and <c>in_enc_policy</c> to <c>0</c> (forced encryption — libtorrent will neither initiate nor accept unencrypted peer connections). When <c>false</c>, libtorrent's default policy (encrypt-when-supported, fall back to plaintext) applies.</summary>
    public bool ForceEncryption { get; set; }

    /// <summary>
    /// The alert categories to enable events for.
    /// Some types of alerts are always enabled to ensure the client functions correctly.
    /// </summary>
    public AlertCategories AlertCategories { get; set; }

    /// <summary>Hard cap on simultaneous peer connections session-wide (libtorrent <c>connections_limit</c> key). Defaults to <c>200</c> — generous for desktop scenarios but conservative for seedboxes. Set to <c>null</c> to leave libtorrent's default in place; set to a value you've confirmed your kernel can handle (each connection consumes a file descriptor — see the TooFewFileDescriptors performance warning).</summary>
    public int? MaxConnections { get; set; } = 200;

    public SettingsPack Build()
    {
        var pack = new SettingsPack();

        // user-agent
        if (!string.IsNullOrEmpty(UserAgent))
        {
            pack.Set("user_agent", UserAgent);
        }

        // fingerprint
        if (!string.IsNullOrEmpty(Fingerprint))
        {
            pack.Set("peer_fingerprint", Fingerprint);
        }

        // events
        pack.Set("alert_mask", (int)AlertCategories);

        pack.Set("anonymous_mode", PrivateMode);
        pack.Set("seeding_outgoing_connections", !BlockSeeding);

        if (MaxConnections.HasValue)
        {
            pack.Set("connections_limit", MaxConnections.Value);
        }

        if (ForceEncryption)
        {
            pack.Set("out_enc_policy", 0);
            pack.Set("in_enc_policy", 0);
        }

        return pack;
    }
}