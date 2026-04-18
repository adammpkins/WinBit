using System.Text.Json;
using Microsoft.Extensions.Hosting;
using WinBit.Core.BitTorrent;
using WinBit.Core.Logging;
using WinBit.Core.Persistence;

namespace WinBit.Core.WatchedFolders;

/// <summary>
/// Per-folder debounced <c>FileSystemWatcher</c> that auto-adds <c>.torrent</c> files via
/// <see cref="ITorrentSessionService"/>. Ports the worker loop from
/// <c>qbittorrent/src/base/torrentfileswatcher.cpp</c>: one watcher per folder, coalesces
/// bursts of Created/Changed/Renamed events into a single scan, and optionally deletes
/// successfully-added sources. Runs as a hosted service and persists folder list to
/// <c>watched-folders.json</c>.
/// </summary>
public sealed class WatchedFolderService : IWatchedFolderService, IHostedService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // qBittorrent's worker uses 2 s; we pick a tighter window to match the "auto-add within 1 s"
    // verification target while still coalescing the Created + Changed burst Windows emits for
    // a single file drop.
    internal const int DebounceMilliseconds = 400;

    private readonly Paths _paths;
    private readonly ITorrentSessionService _session;
    private readonly ILogService _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, WatchedFolder> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherState> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loaded;
    private bool _started;

    public WatchedFolderService(Paths paths, ITorrentSessionService session, ILogService log)
    {
        _paths = paths;
        _session = session;
        _log = log;
    }

    public async Task<IReadOnlyList<WatchedFolder>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _byPath.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(WatchedFolder folder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folder.Path))
        {
            throw new ArgumentException("Watched folder path must not be empty.", nameof(folder));
        }

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _byPath[folder.Path] = folder;
            await PersistAsync(ct).ConfigureAwait(false);
            if (_started)
            {
                RebuildWatcher(folder);
            }
        }
        finally
        {
            _lock.Release();
        }

        if (_started)
        {
            await ScanAsync(folder, ct).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(string path, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_byPath.Remove(path))
            {
                await PersistAsync(ct).ConfigureAwait(false);
                DisposeWatcher(path);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        _started = true;

        WatchedFolder[] snapshot;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            snapshot = _byPath.Values.ToArray();
            foreach (var f in snapshot)
            {
                RebuildWatcher(f);
            }
        }
        finally
        {
            _lock.Release();
        }

        foreach (var f in snapshot)
        {
            await ScanAsync(f, ct).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        _started = false;
        if (!_lifetime.IsCancellationRequested)
        {
            try { _lifetime.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        lock (_watchers)
        {
            foreach (var state in _watchers.Values)
            {
                state.Dispose();
            }
            _watchers.Clear();
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        try { _lifetime.Dispose(); } catch (ObjectDisposedException) { }
        try { _lock.Dispose(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Scans the folder once, adding every <c>.torrent</c> it finds. Public so tests can drive
    /// the core add-pipeline without depending on <c>FileSystemWatcher</c> timing.
    /// </summary>
    public async Task ScanAsync(WatchedFolder folder, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder.Path))
        {
            return;
        }

        var option = folder.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] files;
        try
        {
            files = Directory.GetFiles(folder.Path, "*.torrent", option);
        }
        catch (Exception ex)
        {
            _log.Write($"Watched folder '{folder.Path}' enumeration failed: {ex.Message}", LogSeverity.Warning);
            return;
        }

        var savePath = string.IsNullOrWhiteSpace(folder.SavePath) ? folder.Path : folder.SavePath!;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _session.AddAsync(new AddTorrentParams
            {
                Source = file,
                SavePath = savePath,
                StartImmediately = folder.StartImmediately,
            }, ct).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                if (folder.DeleteSourceOnAdd)
                {
                    TryDelete(file);
                }
            }
            else
            {
                _log.Write($"Watched folder add failed for '{file}': {result.Error}", LogSeverity.Warning);
            }
        }
    }

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex)
        {
            _log.Write($"Watched folder source delete failed for '{file}': {ex.Message}", LogSeverity.Warning);
        }
    }

    private void RebuildWatcher(WatchedFolder folder)
    {
        DisposeWatcher(folder.Path);
        if (!Directory.Exists(folder.Path))
        {
            return;
        }

        var state = new WatcherState(folder, this);
        _watchers[folder.Path] = state;
    }

    private void DisposeWatcher(string path)
    {
        if (_watchers.Remove(path, out var state))
        {
            state.Dispose();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_paths.WatchedFoldersFile))
            {
                await using var stream = File.OpenRead(_paths.WatchedFoldersFile);
                var loaded = await JsonSerializer.DeserializeAsync<List<WatchedFolder>>(stream, JsonOptions, ct).ConfigureAwait(false);
                if (loaded is not null)
                {
                    foreach (var f in loaded)
                    {
                        if (!string.IsNullOrWhiteSpace(f.Path))
                        {
                            _byPath[f.Path] = f;
                        }
                    }
                }
            }

            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var tmp = _paths.WatchedFoldersFile + ".tmp";
        var snapshot = _byPath.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        File.Move(tmp, _paths.WatchedFoldersFile, overwrite: true);
    }

    private sealed class WatcherState : IDisposable
    {
        private readonly WatchedFolderService _owner;
        private readonly WatchedFolder _folder;
        private readonly FileSystemWatcher _fsw;
        private CancellationTokenSource? _pending;

        public WatcherState(WatchedFolder folder, WatchedFolderService owner)
        {
            _owner = owner;
            _folder = folder;
            _fsw = new FileSystemWatcher(folder.Path, "*.torrent")
            {
                IncludeSubdirectories = folder.Recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _fsw.Created += OnChanged;
            _fsw.Changed += OnChanged;
            _fsw.Renamed += OnChanged;
            _fsw.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var previous = Interlocked.Exchange(ref _pending, new CancellationTokenSource());
            previous?.Cancel();
            var token = _pending!.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(DebounceMilliseconds, token).ConfigureAwait(false);
                    await _owner.ScanAsync(_folder, _owner._lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Either a newer event superseded us, or the service is stopping.
                }
                catch (Exception ex)
                {
                    _owner._log.Write($"Watched folder scan failed for '{_folder.Path}': {ex.Message}", LogSeverity.Warning);
                }
            });
        }

        public void Dispose()
        {
            _fsw.EnableRaisingEvents = false;
            _fsw.Created -= OnChanged;
            _fsw.Changed -= OnChanged;
            _fsw.Renamed -= OnChanged;
            _fsw.Dispose();
            _pending?.Cancel();
            _pending?.Dispose();
        }
    }
}
