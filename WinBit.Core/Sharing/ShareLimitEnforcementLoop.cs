using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Common;
using WinBit.Core.Logging;
using WinBit.Core.Settings;

namespace WinBit.Core.Sharing;

/// <summary>
/// Background loop that evaluates per-torrent share limits and dispatches the configured
/// action. Ports the role of qBittorrent's seeding-limit timer (see
/// <c>qbittorrent/src/base/bittorrent/sessionimpl.cpp</c>, <c>processTorrentShareLimits</c>).
/// Seeding time and inactive-seeding time are derived from successive ticks — MonoTorrent
/// doesn't track them itself.
/// </summary>
public sealed class ShareLimitEnforcementLoop : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    private readonly ITorrentSessionService _session;
    private readonly IShareLimitOverrideService _overrides;
    private readonly ISettingsService _settings;
    private readonly ILogService _log;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, TorrentTracker> _trackers = new(StringComparer.OrdinalIgnoreCase);

    public ShareLimitEnforcementLoop(
        ITorrentSessionService session,
        IShareLimitOverrideService overrides,
        ISettingsService settings,
        ILogService log,
        TimeProvider? time = null)
    {
        _session = session;
        _overrides = overrides;
        _settings = settings;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Warm the overrides cache once — Effective() is sync and needs the map loaded.
        try
        {
            await _overrides.GetAllAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — normal.
        }
    }

    /// <summary>
    /// Runs a single evaluation pass over every torrent in the session. Public so tests can
    /// drive the loop deterministically without relying on <see cref="PeriodicTimer"/>.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        var ids = _session.Torrents;
        var now = _time.GetUtcNow();

        // Prune trackers for torrents that have been removed from the session.
        var liveIds = new HashSet<string>(ids.Select(i => i.Value), StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _trackers.Keys.Where(k => !liveIds.Contains(k)).ToList())
        {
            _trackers.Remove(stale);
        }

        var global = _settings.Current.BitTorrent.GlobalShareLimits;

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var snapshot = _session.GetShareLimitSnapshot(id);
            if (snapshot is null)
            {
                continue;
            }

            var inputs = UpdateTrackerAndComputeInputs(snapshot.Value, now);
            var effective = _overrides.Effective(id, global);
            var decision = ShareLimitEvaluator.Evaluate(effective, inputs);

            if (decision == ShareLimitDecision.NoAction)
            {
                continue;
            }

            await DispatchAsync(id, decision, ct).ConfigureAwait(false);
        }
    }

    private ShareLimitInputs UpdateTrackerAndComputeInputs(ShareLimitSnapshot snapshot, DateTimeOffset now)
    {
        if (!_trackers.TryGetValue(snapshot.Id.Value, out var tracker))
        {
            tracker = new TorrentTracker();
            _trackers[snapshot.Id.Value] = tracker;
        }

        // Seeding time: accumulates while the torrent is finished and not stopped. If it
        // pauses/stops mid-session, the clock is paused too; we don't clear accumulated time.
        if (snapshot.IsFinished && !snapshot.IsStopped)
        {
            if (tracker.LastFinishedActiveAtUtc is { } last)
            {
                tracker.AccumulatedSeedingTime += now - last;
            }
            tracker.LastFinishedActiveAtUtc = now;
        }
        else
        {
            tracker.LastFinishedActiveAtUtc = null;
        }

        // Inactive seeding time: grows as long as BytesUploaded does not change while
        // finished + active. Any byte delta resets the activity clock.
        if (snapshot.IsFinished && !snapshot.IsStopped)
        {
            if (tracker.LastUploadActivityUtc is null || snapshot.BytesUploaded != tracker.LastBytesUploaded)
            {
                tracker.LastUploadActivityUtc = now;
                tracker.LastBytesUploaded = snapshot.BytesUploaded;
            }
        }
        else
        {
            tracker.LastUploadActivityUtc = null;
            tracker.LastBytesUploaded = snapshot.BytesUploaded;
        }

        var inactive = tracker.LastUploadActivityUtc is { } activityAt
            ? now - activityAt
            : TimeSpan.Zero;

        return new ShareLimitInputs(
            IsFinished: snapshot.IsFinished,
            IsForced: snapshot.IsForced,
            IsStopped: snapshot.IsStopped,
            IsSuperSeeding: snapshot.IsSuperSeeding,
            Ratio: snapshot.Ratio,
            SeedingTime: tracker.AccumulatedSeedingTime,
            InactiveSeedingTime: inactive);
    }

    private async Task DispatchAsync(TorrentId id, ShareLimitDecision decision, CancellationToken ct)
    {
        var shortId = id.Value.Length >= 8 ? id.Value[..8] : id.Value;
        switch (decision)
        {
            case ShareLimitDecision.Stop:
                {
                    var result = await _session.PauseAsync(id, ct).ConfigureAwait(false);
                    LogResult(shortId, "Stop", result);
                    break;
                }
            case ShareLimitDecision.Remove:
                {
                    var result = await _session.RemoveAsync(id, deleteContent: false, ct).ConfigureAwait(false);
                    LogResult(shortId, "Remove", result);
                    break;
                }
            case ShareLimitDecision.RemoveWithContent:
                {
                    var result = await _session.RemoveAsync(id, deleteContent: true, ct).ConfigureAwait(false);
                    LogResult(shortId, "RemoveWithContent", result);
                    break;
                }
            case ShareLimitDecision.EnableSuperSeeding:
                {
                    var result = await _session.SetSuperSeedingAsync(id, enabled: true, ct).ConfigureAwait(false);
                    LogResult(shortId, "EnableSuperSeeding", result);
                    break;
                }
        }
    }

    private void LogResult(string shortId, string action, Result result)
    {
        if (result.IsSuccess)
        {
            _log.Write($"Share-limit enforcement: {action} applied to {shortId}.", LogSeverity.Info);
        }
        else
        {
            _log.Write($"Share-limit enforcement: {action} failed for {shortId}: {result.Error}", LogSeverity.Warning);
        }
    }

    private sealed class TorrentTracker
    {
        public TimeSpan AccumulatedSeedingTime { get; set; }
        public DateTimeOffset? LastFinishedActiveAtUtc { get; set; }
        public DateTimeOffset? LastUploadActivityUtc { get; set; }
        public long LastBytesUploaded { get; set; }
    }
}
