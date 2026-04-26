using System;
using System.IO;
using LibtorrentSharp.Enums;

namespace LibtorrentSharp.Tests.Network;

/// <summary>
/// Shared fixture for Phase C public-swarm tests. One <see cref="LibtorrentSession"/>
/// per test class with DHT enabled and the canonical Ubuntu magnet attached. When
/// the network gate is closed (default), the fixture stays empty so the test
/// process doesn't spin up a real session.
/// </summary>
public sealed class PublicSwarmFixture : IDisposable
{
    /// <summary>
    /// Ubuntu 14.04.1 desktop ISO — long-lived swarm with thousands of seeders;
    /// the most stable public test target the repo can lean on. Same magnet the
    /// adapter's Phase B b-magnet-e2e checklist uses.
    /// </summary>
    public const string UbuntuMagnetUri =
        "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c&dn=ubuntu-14.04.1-desktop-amd64.iso";

    public LibtorrentSession? Client { get; }
    public MagnetHandle? Magnet { get; }
    public string? SavePath { get; }

    public PublicSwarmFixture()
    {
        if (!NetworkTestGate.IsEnabled)
        {
            return;
        }

        // DHT-enabled config is essential — magnets resolve via DHT first, even
        // when the URI carries trackers. Bind ephemeral so two concurrent test
        // runs on the same dev box don't fight over a fixed port.
        var pack = new SettingsPack();
        pack.Set("alert_mask", (int)AlertCategories.Status | (int)AlertCategories.Storage);
        pack.Set("listen_interfaces", "0.0.0.0:0,[::]:0");
        pack.Set("enable_dht", true);
        pack.Set("enable_lsd", false);
        pack.Set("enable_upnp", false);
        pack.Set("enable_natpmp", false);
        pack.Set("dht_bootstrap_nodes",
            "router.bittorrent.com:6881,router.utorrent.com:6881,dht.transmissionbt.com:6881");

        SavePath = Path.Combine(Path.GetTempPath(), "LibtorrentSharpTests-Network", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(SavePath);

        Client = new LibtorrentSession(pack)
        {
            DefaultDownloadPath = SavePath,
        };

        Magnet = Client.Add(new AddTorrentParams { MagnetUri = UbuntuMagnetUri, SavePath = SavePath }).Magnet!;
        // After this point the fixture user calls Magnet.Resume() to start work
        // (AddMagnet leaves the torrent paused per the C ABI's add_torrent_params).
        Magnet.Resume();
    }

    public void Dispose()
    {
        Client?.Dispose();
        if (SavePath is not null && Directory.Exists(SavePath))
        {
            try { Directory.Delete(SavePath, recursive: true); } catch { /* best-effort */ }
        }
    }
}
