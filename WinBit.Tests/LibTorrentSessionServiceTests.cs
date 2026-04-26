using FluentAssertions;
using Microsoft.Extensions.Options;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Hosting;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;
using WinBit.Core.Settings;
using WinBit.Core.Threading;
using WinBit.Tests.Helpers;
using Xunit;

namespace WinBit.Tests;

/// <summary>
/// Lifecycle tests that need a fresh <see cref="LibTorrentSessionService"/> per test.
/// Kept deliberately small — every instantiated <c>LibtorrentSession</c> eats process-wide
/// libtorrent resources that only clear when the test process exits. Add/Remove behavior
/// is covered by <see cref="LibTorrentSessionServiceAddRemoveTests"/>, which shares a
/// single service across its cases.
/// </summary>
[Trait("Category", "Native")]
public sealed class LibTorrentSessionServiceTests
{
    [Fact]
    public async Task StopAsync_without_start_is_noop()
    {
        // Pre-Start: nothing native happens, no LibtorrentSession is constructed.
        using var temp = new TempDirectory();
        await using var service = CreateService(temp);

        await service.StopAsync();

        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_when_engine_not_running_returns_failure()
    {
        // Pre-Start: nothing native happens.
        using var temp = new TempDirectory();
        await using var service = CreateService(temp);

        var result = await service.AddAsync(new AddTorrentParams
        {
            Source = "magnet:?xt=urn:btih:dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c",
            SavePath = temp.Path,
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not running");
    }

    /// <summary>
    /// All start/stop assertions in one test so the test process spins up exactly one
    /// extra LibtorrentSession for the entire lifecycle suite (the AddRemove tests share a
    /// second one via fixture). Repeated start/stop cycles within a single test are a
    /// stability headache for libtorrent at the moment, so this also exercises only the
    /// minimum needed to validate idempotency.
    /// </summary>
    [Fact]
    public async Task Lifecycle_start_idempotent_stop_dispose_smoke()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);

        service.IsRunning.Should().BeFalse();

        await service.StartAsync();
        service.IsRunning.Should().BeTrue();

        // Idempotent — second StartAsync is a no-op.
        await service.StartAsync();
        service.IsRunning.Should().BeTrue();

        await service.StopAsync();
        service.IsRunning.Should().BeFalse();

        // After Stop, DisposeAsync is also a no-op.
        await service.DisposeAsync();
        service.IsRunning.Should().BeFalse();
    }

    internal static LibTorrentSessionService CreateService(
        TempDirectory temp,
        IDispatcherQueueProvider? dispatcher = null,
        ITorrentStateStore? stateStore = null)
    {
        var options = Options.Create(new WinBitCoreOptions
        {
            DataRoot = temp.Path,
            // ListenPort=0 routes through the adapter's test/dev path: ephemeral bind + no
            // DHT bootstrap. Keeps the test box from hammering public routers and lets
            // multiple parallel test processes coexist.
            ListenPort = 0,
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
        });
        var paths = new Paths(options);
        return new LibTorrentSessionService(
            new LogService(),
            paths,
            options,
            dispatcher ?? new InlineDispatcherQueueProvider(),
            stateStore ?? new InMemoryTorrentStateStore(),
            new JsonCustomNameStore(paths));
    }
}

/// <summary>
/// Test double for <see cref="ITorrentStateStore"/>. Holds rows + resume blobs in memory so
/// adapter tests can assert persistence behavior without spinning up SQLite per test.
/// </summary>
public sealed class InMemoryTorrentStateStore : ITorrentStateStore
{
    private readonly Dictionary<TorrentId, TorrentStateRecord> _records = new();
    private readonly Dictionary<TorrentId, (byte[] Blob, int Version)> _blobs = new();

    public IReadOnlyDictionary<TorrentId, TorrentStateRecord> Records => _records;
    public IReadOnlyDictionary<TorrentId, (byte[] Blob, int Version)> Blobs => _blobs;
    public int LoadFastResumeCalls { get; private set; }

    public void Seed(TorrentId id, byte[] blob, int version) => _blobs[id] = (blob, version);

    public Task UpsertTorrentAsync(TorrentStateRecord record, CancellationToken ct = default)
    {
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task RemoveTorrentAsync(TorrentId id, CancellationToken ct = default)
    {
        _records.Remove(id);
        _blobs.Remove(id);
        return Task.CompletedTask;
    }

    public Task SaveFastResumeAsync(TorrentId id, byte[] blob, int version, CancellationToken ct = default)
    {
        if (_records.ContainsKey(id))
        {
            _blobs[id] = (blob, version);
        }
        return Task.CompletedTask;
    }

    public Task<byte[]?> LoadFastResumeAsync(TorrentId id, int expectedVersion, CancellationToken ct = default)
    {
        LoadFastResumeCalls++;
        return Task.FromResult(_blobs.TryGetValue(id, out var entry) && entry.Version == expectedVersion
            ? entry.Blob
            : null);
    }

    public Task<IReadOnlyList<TorrentStateRecord>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TorrentStateRecord>>(_records.Values.ToList());
}

/// <summary>
/// Fixture shared across <see cref="LibTorrentSessionServiceAddRemoveTests"/>. A single
/// running <c>LibtorrentSession</c> satisfies every test, which avoids the process-wide
/// resource pressure that repeated session create/destroy cycles expose in the current
/// LibtorrentSharp surface.
/// </summary>
public sealed class LibTorrentRunningServiceFixture : IAsyncLifetime
{
    public TempDirectory Temp { get; private set; } = null!;
    public LibTorrentSessionService Service { get; private set; } = null!;
    public InMemoryTorrentStateStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Temp = new TempDirectory();
        Store = new InMemoryTorrentStateStore();
        Service = LibTorrentSessionServiceTests.CreateService(Temp, stateStore: Store);
        await Service.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Service.DisposeAsync();
        Temp.Dispose();
    }
}

/// <summary>
/// Add/Remove tests that share a single running <see cref="LibTorrentSessionService"/>.
/// Each test uses a unique info-hash so cross-test bookkeeping stays clean, and each test
/// removes what it added so the <c>Torrents</c> collection returns to empty between runs.
/// </summary>
[Trait("Category", "Native")]
public sealed class LibTorrentSessionServiceAddRemoveTests : IClassFixture<LibTorrentRunningServiceFixture>
{
    private readonly LibTorrentRunningServiceFixture _fixture;

    public LibTorrentSessionServiceAddRemoveTests(LibTorrentRunningServiceFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Torrents_starts_empty()
    {
        _fixture.Service.Torrents.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAndPublishSnapshots_without_subscribers_is_noop()
    {
        var act = _fixture.Service.CaptureAndPublishSnapshots;
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetSnapshots_starts_empty_when_nothing_added()
    {
        // Defensive — a previous test may have left state. Drain anything pending
        // so this assertion reflects post-cleanup truth.
        _fixture.Service.CaptureAndPublishSnapshots();
        _fixture.Service.GetSnapshots().Where(s => _fixture.Service.Torrents.Contains(s.Id))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAndPublishSnapshots_after_add_emits_snapshot_for_new_torrent()
    {
        const string hash = "22222222222222222222222222222222bbbbbbbb";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}&dn=snapshot-test",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        IReadOnlyList<TorrentSnapshot>? received = null;
        EventHandler<IReadOnlyList<TorrentSnapshot>> handler = (_, batch) => received = batch;
        _fixture.Service.TorrentUpdated += handler;
        try
        {
            _fixture.Service.CaptureAndPublishSnapshots();
        }
        finally
        {
            _fixture.Service.TorrentUpdated -= handler;
        }

        received.Should().NotBeNull();
        received!.Should().Contain(s => s.Id == add.Value);
        var snapshot = received!.First(s => s.Id == add.Value);
        snapshot.Progress.Should().BeInRange(0.0, 1.0);
        snapshot.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);

        // GetSnapshots returns the same cached batch we just fanned out.
        _fixture.Service.GetSnapshots().Should().Contain(s => s.Id == add.Value);

        await _fixture.Service.RemoveAsync(add.Value);
    }

    [Fact]
    public async Task CaptureAndPublishSnapshots_returns_to_empty_after_remove()
    {
        const string hash = "33333333333333333333333333333333cccccccc";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        _fixture.Service.CaptureAndPublishSnapshots();
        _fixture.Service.GetSnapshots().Should().Contain(s => s.Id == add.Value);

        await _fixture.Service.RemoveAsync(add.Value);
        _fixture.Service.CaptureAndPublishSnapshots();
        _fixture.Service.GetSnapshots().Should().NotContain(s => s.Id == add.Value);
    }

    [Fact]
    public async Task AddAsync_with_malformed_magnet_returns_failure()
    {
        var result = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = "magnet:?dn=missing-hash",
            SavePath = _fixture.Temp.Path,
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("xt=urn:btih");
    }

    [Fact]
    public async Task AddAsync_with_unknown_source_returns_failure()
    {
        var result = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = Path.Combine(_fixture.Temp.Path, "does-not-exist.torrent"),
            SavePath = _fixture.Temp.Path,
        });

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Unknown torrent source");
    }

    [Fact]
    public async Task RemoveAsync_with_unknown_id_returns_failure()
    {
        var result = await _fixture.Service.RemoveAsync(TorrentId.FromInfoHash(new string('a', 40)));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("currently loaded");
    }

    [Fact]
    public async Task PauseAsync_with_unknown_id_returns_failure()
    {
        var result = await _fixture.Service.PauseAsync(TorrentId.FromInfoHash(new string('b', 40)));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("currently loaded");
    }

    [Fact]
    public async Task PersistFastResumeAsync_with_no_torrents_is_noop()
    {
        // No-op should not throw and should not write anything to the store.
        var blobsBefore = _fixture.Store.Blobs.Count;
        await _fixture.Service.PersistFastResumeAsync();
        _fixture.Store.Blobs.Count.Should().Be(blobsBefore);
    }

    [Fact]
    public async Task AddAsync_consults_state_store_for_resume_blob()
    {
        const string hash = "66666666666666666666666666666666ffffffff";
        var loadsBefore = _fixture.Store.LoadFastResumeCalls;

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads-resume-load"),
            StartImmediately = false,
        });

        try
        {
            add.IsSuccess.Should().BeTrue(add.Error);
            // Adapter must always probe the store on every add — that's how a real persisted
            // blob would be discovered on the cold-start path.
            _fixture.Store.LoadFastResumeCalls.Should().Be(loadsBefore + 1);
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task AddAsync_falls_back_to_fresh_add_when_stored_blob_is_corrupt()
    {
        const string hash = "77777777777777777777777777777777deadbeef";
        var id = TorrentId.FromInfoHash(hash);
        // Pre-seed an obviously-malformed blob so libtorrent's resume parse fails and the
        // adapter takes its fallback path rather than the AttachTorrentWithResume happy path.
        _fixture.Store.Seed(id, new byte[] { 0x00, 0x01, 0x02, 0x03 }, version: 1);

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads-resume-corrupt"),
            StartImmediately = false,
        });

        try
        {
            add.IsSuccess.Should().BeTrue(add.Error);
            _fixture.Service.Torrents.Should().Contain(id);
        }
        finally
        {
            await _fixture.Service.RemoveAsync(id);
        }
    }

    [Fact]
    public async Task AddAsync_upserts_state_record_with_savepath_and_dn_name()
    {
        const string hash = "55555555555555555555555555555555eeeeeeee";
        const string dnName = "ubuntu-derived-fixture";
        var savePath = Path.Combine(_fixture.Temp.Path, "downloads-upsert");

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(dnName)}",
            SavePath = savePath,
            StartImmediately = false,
        });

        try
        {
            add.IsSuccess.Should().BeTrue(add.Error);

            _fixture.Store.Records.Should().ContainKey(add.Value);
            var record = _fixture.Store.Records[add.Value];
            record.SavePath.Should().Be(savePath);
            record.Name.Should().Be(dnName);
            record.AddedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task GetSessionStats_aggregates_zero_when_snapshot_cache_is_empty()
    {
        // Cache is empty until a CaptureAndPublishSnapshots tick after at least one alert.
        var stats = _fixture.Service.GetSessionStats();
        stats.OpenConnections.Should().Be(0);
        stats.DhtNodes.Should().Be(0);
    }

    [Fact]
    public async Task Meta_methods_resolve_for_added_magnet_and_clear_after_remove()
    {
        const string hash = "99999999999999999999999999999999bbbb2222";
        const string dnName = "meta-test-fixture";
        var savePath = Path.Combine(_fixture.Temp.Path, "downloads-meta");
        var sourceUri = $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(dnName)}";

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = sourceUri,
            SavePath = savePath,
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        try
        {
            _fixture.Service.GetMagnetUri(add.Value).Should().Be(sourceUri);
            _fixture.Service.GetName(add.Value).Should().Be(dnName);
            _fixture.Service.GetSavePath(add.Value).Should().Be(savePath);
            // No trackers set → empty list (not null).
            _fixture.Service.GetTrackerHosts(add.Value).Should().BeEmpty();
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }

        _fixture.Service.GetMagnetUri(add.Value).Should().BeNull();
        _fixture.Service.GetName(add.Value).Should().BeNull();
    }

    [Fact]
    public async Task GetShareLimitSnapshot_returns_null_for_unknown_id()
    {
        _fixture.Service.GetShareLimitSnapshot(TorrentId.FromInfoHash(new string('c', 40)))
            .Should().BeNull();
    }

    [Fact]
    public async Task GetShareLimitSnapshot_for_added_magnet_carries_id_and_state()
    {
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads-share"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        try
        {
            var snapshot = _fixture.Service.GetShareLimitSnapshot(add.Value);
            snapshot.Should().NotBeNull();
            snapshot!.Value.Id.Should().Be(add.Value);
            // Brand-new magnet hasn't uploaded anything yet.
            snapshot.Value.BytesUploaded.Should().Be(0);
            snapshot.Value.IsSuperSeeding.Should().BeFalse();
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task SetSuperSeedingAsync_succeeds_for_added_magnet()
    {
        const string hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads-super"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        try
        {
            (await _fixture.Service.SetSuperSeedingAsync(add.Value, enabled: true)).IsSuccess.Should().BeTrue();
            (await _fixture.Service.SetSuperSeedingAsync(add.Value, enabled: false)).IsSuccess.Should().BeTrue();
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task SetSpeedLimitsAsync_round_trips_through_GetSpeedLimits()
    {
        const string hash = "88888888888888888888888888888888aaaa1111";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads-rate"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        try
        {
            var set = await _fixture.Service.SetSpeedLimitsAsync(add.Value, downloadBps: 250_000, uploadBps: 50_000);
            set.IsSuccess.Should().BeTrue(set.Error);

            var limits = _fixture.Service.GetSpeedLimits(add.Value);
            limits.Should().NotBeNull();
            limits!.Value.DownloadBps.Should().Be(250_000);
            limits.Value.UploadBps.Should().Be(50_000);
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task SetGlobalSpeedLimitsAsync_succeeds_when_running()
    {
        var result = await _fixture.Service.SetGlobalSpeedLimitsAsync(downloadBps: 1_000_000, uploadBps: 500_000);
        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task SetPortForwardingAsync_succeeds_when_running()
    {
        (await _fixture.Service.SetPortForwardingAsync(true)).IsSuccess.Should().BeTrue();
        (await _fixture.Service.SetPortForwardingAsync(false)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(EncryptionMode.Prefer)]
    [InlineData(EncryptionMode.Require)]
    [InlineData(EncryptionMode.Disable)]
    public async Task SetEncryptionModeAsync_succeeds_for_every_mode(EncryptionMode mode)
    {
        var result = await _fixture.Service.SetEncryptionModeAsync(mode);
        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task SetPeerDiscoveryAsync_succeeds_when_running()
    {
        (await _fixture.Service.SetPeerDiscoveryAsync(dht: true, pex: true, lsd: true)).IsSuccess.Should().BeTrue();
        (await _fixture.Service.SetPeerDiscoveryAsync(dht: false, pex: false, lsd: false)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Action_methods_round_trip_for_added_magnet()
    {
        const string hash = "44444444444444444444444444444444dddddddd";
        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}&dn=actions",
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads"),
            StartImmediately = false,
        });
        add.IsSuccess.Should().BeTrue(add.Error);

        try
        {
            // Each action exercises the InvokeOnHandle path; libtorrent treats each as
            // idempotent at the native level so back-to-back calls all return Success.
            (await _fixture.Service.PauseAsync(add.Value)).IsSuccess.Should().BeTrue();
            (await _fixture.Service.PauseAsync(add.Value)).IsSuccess.Should().BeTrue("Pause is idempotent");
            (await _fixture.Service.ResumeAsync(add.Value)).IsSuccess.Should().BeTrue();
            (await _fixture.Service.ResumeAsync(add.Value)).IsSuccess.Should().BeTrue("Resume is idempotent");
            (await _fixture.Service.ForceRecheckAsync(add.Value)).IsSuccess.Should().BeTrue();
            (await _fixture.Service.ForceReannounceAsync(add.Value)).IsSuccess.Should().BeTrue();
        }
        finally
        {
            await _fixture.Service.RemoveAsync(add.Value);
        }
    }

    [Fact]
    public async Task AddAsync_then_RemoveAsync_round_trip_for_magnet()
    {
        // Unique hash per test to avoid colliding with other cases that share this fixture.
        const string hash = "11111111111111111111111111111111aaaaaaaa";
        var downloads = Path.Combine(_fixture.Temp.Path, "downloads");

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = $"magnet:?xt=urn:btih:{hash}&dn=round-trip",
            SavePath = downloads,
            StartImmediately = false,
        });

        add.IsSuccess.Should().BeTrue(add.Error);
        add.Value.Value.Should().Be(hash);
        _fixture.Service.Torrents.Should().Contain(add.Value);

        var remove = await _fixture.Service.RemoveAsync(add.Value);
        remove.IsSuccess.Should().BeTrue(remove.Error);
        _fixture.Service.Torrents.Should().NotContain(add.Value);
    }

    /// <summary>
    /// Passes in isolation but is the flakiest case under full-suite load — the
    /// AttachTorrent/DetachTorrent path interacts poorly with WinBit.Tests' MonoTorrent
    /// ClientEngine smoke tests when both touch native sockets in the same test process.
    /// Re-enable once the LibtorrentSharp session lifecycle is hardened (Phase E polish
    /// or earlier — tracked in docs/libtorrent-binding.md → "Adapter testing fragility").
    /// </summary>
    [Fact(Skip = "Flaky under full-suite native socket load; runs green in isolation. See remarks for the underlying LibtorrentSharp lifecycle gap.")]
    public async Task AddAsync_then_RemoveAsync_round_trip_for_torrent_file()
    {
        var sourceDir = Path.Combine(_fixture.Temp.Path, $"payload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllBytesAsync(Path.Combine(sourceDir, "blob.bin"), new byte[16 * 1024]);

        var torrentPath = Path.Combine(_fixture.Temp.Path, $"fixture-{Guid.NewGuid():N}.torrent");
        var create = await new TorrentCreatorService().CreateAsync(new TorrentCreateRequest
        {
            SourcePath = sourceDir,
            OutputPath = torrentPath,
            Name = "round-trip-fixture",
            TrackerTiers = new[] { new[] { "udp://tracker.example:6969/announce" } },
        });
        create.IsSuccess.Should().BeTrue(create.Error);

        var add = await _fixture.Service.AddAsync(new AddTorrentParams
        {
            Source = torrentPath,
            SavePath = Path.Combine(_fixture.Temp.Path, "downloads"),
            StartImmediately = false,
        });

        add.IsSuccess.Should().BeTrue(add.Error);
        _fixture.Service.Torrents.Should().Contain(add.Value);

        var remove = await _fixture.Service.RemoveAsync(add.Value);
        remove.IsSuccess.Should().BeTrue(remove.Error);
        _fixture.Service.Torrents.Should().NotContain(add.Value);
    }
}
