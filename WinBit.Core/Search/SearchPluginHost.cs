using System.Runtime.CompilerServices;
using System.Threading.Channels;
using WinBit.Core.Logging;

namespace WinBit.Core.Search;

/// <summary>
/// Concurrent plugin fan-out. Each selected plugin runs on its own pump task that writes into a
/// shared unbounded channel; <see cref="SearchAsync"/> reads the channel until every pump has
/// completed. A plugin throw (or hang-then-cancel) won't block the others — exceptions surface
/// in the log service and the offending plugin's stream just ends early.
/// </summary>
public sealed class SearchPluginHost : ISearchPluginHost
{
    private readonly ILogService _log;
    private readonly List<ISearchPlugin> _plugins;
    private readonly object _gate = new();

    public IReadOnlyList<ISearchPlugin> Plugins
    {
        get
        {
            lock (_gate)
            {
                return _plugins.ToArray();
            }
        }
    }

    public SearchPluginHost(IEnumerable<ISearchPlugin> plugins, ILogService log)
    {
        _plugins = plugins.ToList();
        _log = log;
    }

    public void Register(ISearchPlugin plugin)
    {
        lock (_gate)
        {
            if (_plugins.Any(p => string.Equals(p.Name, plugin.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            _plugins.Add(plugin);
        }
    }

    public bool Unregister(string pluginName)
    {
        lock (_gate)
        {
            var idx = _plugins.FindIndex(p => string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
            {
                return false;
            }
            _plugins.RemoveAt(idx);
            return true;
        }
    }

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        SearchRequest request,
        IReadOnlyCollection<string>? pluginNames = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var snapshot = Plugins;
        var active = pluginNames is null
            ? snapshot
            : snapshot.Where(p => pluginNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (active.Count == 0)
        {
            yield break;
        }

        var channel = Channel.CreateUnbounded<SearchResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var pumps = active.Select(p => Task.Run(() => PumpAsync(p, request, channel.Writer, ct), ct)).ToArray();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(pumps).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var hit in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return hit;
        }
    }

    private async Task PumpAsync(ISearchPlugin plugin, SearchRequest request, ChannelWriter<SearchResult> writer, CancellationToken ct)
    {
        try
        {
            await foreach (var hit in plugin.SearchAsync(request, ct).ConfigureAwait(false))
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }
                await writer.WriteAsync(hit, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on ct cancel — drop silently.
        }
        catch (Exception ex)
        {
            _log.Write($"Search plugin '{plugin.Name}' failed: {ex.Message}", LogSeverity.Warning);
        }
    }
}
