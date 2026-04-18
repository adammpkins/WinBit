using System.Text.Json;
using WinBit.Core.Persistence;

namespace WinBit.Core.Stats;

public sealed class AllTimeStatsService : IAllTimeStatsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Paths _paths;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private long _downloadedBytes;
    private long _uploadedBytes;
    private long _lastTickSessionDownloaded;
    private long _lastTickSessionUploaded;
    private bool _baselineCaptured;

    public AllTimeStatsService(Paths paths) => _paths = paths;

    public AllTimeStats Current => new()
    {
        DownloadedBytes = Interlocked.Read(ref _downloadedBytes),
        UploadedBytes = Interlocked.Read(ref _uploadedBytes),
    };

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_paths.AllTimeStatsFile))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_paths.AllTimeStatsFile);
            var loaded = await JsonSerializer.DeserializeAsync<AllTimeStats>(stream, JsonOptions, ct).ConfigureAwait(false);
            if (loaded is not null)
            {
                Interlocked.Exchange(ref _downloadedBytes, loaded.DownloadedBytes);
                Interlocked.Exchange(ref _uploadedBytes, loaded.UploadedBytes);
            }
        }
        catch (Exception)
        {
            // Corrupt stats file shouldn't prevent startup — fall back to the in-memory zero baseline.
        }
    }

    public void Tick(long sessionDownloadedBytes, long sessionUploadedBytes)
    {
        if (!_baselineCaptured)
        {
            _lastTickSessionDownloaded = sessionDownloadedBytes;
            _lastTickSessionUploaded = sessionUploadedBytes;
            _baselineCaptured = true;
            return;
        }

        var downDelta = sessionDownloadedBytes - _lastTickSessionDownloaded;
        var upDelta = sessionUploadedBytes - _lastTickSessionUploaded;

        if (downDelta > 0)
        {
            Interlocked.Add(ref _downloadedBytes, downDelta);
        }
        if (upDelta > 0)
        {
            Interlocked.Add(ref _uploadedBytes, upDelta);
        }

        _lastTickSessionDownloaded = sessionDownloadedBytes;
        _lastTickSessionUploaded = sessionUploadedBytes;
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tmp = _paths.AllTimeStatsFile + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, ct).ConfigureAwait(false);
            }
            File.Move(tmp, _paths.AllTimeStatsFile, overwrite: true);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
