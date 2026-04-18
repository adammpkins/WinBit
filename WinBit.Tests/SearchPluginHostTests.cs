using System.Runtime.CompilerServices;
using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Search;
using Xunit;

namespace WinBit.Tests;

public sealed class SearchPluginHostTests
{
    [Fact]
    public void Register_adds_plugin_and_is_idempotent_by_name()
    {
        var host = new SearchPluginHost(Array.Empty<ISearchPlugin>(), new NoopLog());
        host.Register(new FakePlugin("alpha", Array.Empty<SearchResult>()));
        host.Register(new FakePlugin("alpha", Array.Empty<SearchResult>()));
        host.Plugins.Should().HaveCount(1);
    }

    [Fact]
    public void Unregister_removes_named_plugin_and_returns_true()
    {
        var host = new SearchPluginHost(Array.Empty<ISearchPlugin>(), new NoopLog());
        host.Register(new FakePlugin("alpha", Array.Empty<SearchResult>()));
        host.Unregister("ALPHA").Should().BeTrue();
        host.Unregister("alpha").Should().BeFalse();
        host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_host_returns_no_results()
    {
        var host = new SearchPluginHost(Array.Empty<ISearchPlugin>(), new NoopLog());
        var hits = await CollectAsync(host.SearchAsync(new SearchRequest("anything")));
        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Results_from_all_plugins_are_merged()
    {
        var alpha = new FakePlugin("alpha", new[]
        {
            new SearchResult("alpha", "alpha-1"),
            new SearchResult("alpha", "alpha-2"),
        });
        var beta = new FakePlugin("beta", new[]
        {
            new SearchResult("beta", "beta-1"),
        });

        var host = new SearchPluginHost(new ISearchPlugin[] { alpha, beta }, new NoopLog());
        var hits = await CollectAsync(host.SearchAsync(new SearchRequest("q")));

        hits.Select(h => h.Name).Should().BeEquivalentTo(new[] { "alpha-1", "alpha-2", "beta-1" });
    }

    [Fact]
    public async Task Plugin_filter_scopes_to_named_subset()
    {
        var alpha = new FakePlugin("alpha", new[] { new SearchResult("alpha", "x") });
        var beta = new FakePlugin("beta", new[] { new SearchResult("beta", "y") });
        var host = new SearchPluginHost(new ISearchPlugin[] { alpha, beta }, new NoopLog());

        var hits = await CollectAsync(host.SearchAsync(new SearchRequest("q"), pluginNames: new[] { "beta" }));
        hits.Select(h => h.Name).Should().Equal("y");
    }

    [Fact]
    public async Task Plugin_throw_is_isolated()
    {
        var healthy = new FakePlugin("healthy", new[] { new SearchResult("healthy", "ok") });
        var broken = new ThrowingPlugin("broken");
        var log = new RecordingLog();

        var host = new SearchPluginHost(new ISearchPlugin[] { broken, healthy }, log);
        var hits = await CollectAsync(host.SearchAsync(new SearchRequest("q")));

        hits.Select(h => h.Name).Should().Equal("ok");
        log.Messages.Should().Contain(m => m.Contains("'broken'") && m.Contains("boom"));
    }

    [Fact]
    public async Task Cancellation_stops_pumps_quickly()
    {
        var slow = new SlowPlugin("slow");
        var host = new SearchPluginHost(new ISearchPlugin[] { slow }, new NoopLog());

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var hits = new List<SearchResult>();
        try
        {
            await foreach (var h in host.SearchAsync(new SearchRequest("q"), ct: cts.Token))
            {
                hits.Add(h);
            }
        }
        catch (OperationCanceledException)
        {
        }

        // Plugin emits one hit immediately, then waits. Either we got the first and cancelled,
        // or we cancelled before it arrived — both are fine. The invariant is we didn't hang.
        hits.Count.Should().BeLessThan(10);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> stream)
    {
        var list = new List<T>();
        await foreach (var item in stream)
        {
            list.Add(item);
        }
        return list;
    }

    private sealed class FakePlugin : ISearchPlugin
    {
        private readonly IReadOnlyList<SearchResult> _results;
        public FakePlugin(string name, IReadOnlyList<SearchResult> results)
        {
            Name = name;
            _results = results;
        }
        public string Name { get; }
        public string DisplayName => Name;
        public IReadOnlyList<string> SupportedCategories => Array.Empty<string>();
        public async IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var r in _results)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                yield return r;
            }
        }
    }

    private sealed class ThrowingPlugin : ISearchPlugin
    {
        public ThrowingPlugin(string name) => Name = name;
        public string Name { get; }
        public string DisplayName => Name;
        public IReadOnlyList<string> SupportedCategories => Array.Empty<string>();
        public IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct) =>
            Throw();
        private static async IAsyncEnumerable<SearchResult> Throw()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class SlowPlugin : ISearchPlugin
    {
        public SlowPlugin(string name) => Name = name;
        public string Name { get; }
        public string DisplayName => Name;
        public IReadOnlyList<string> SupportedCategories => Array.Empty<string>();
        public async IAsyncEnumerable<SearchResult> SearchAsync(SearchRequest request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new SearchResult(Name, "first");
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }

    private sealed class RecordingLog : ILogService
    {
        public List<string> Messages { get; } = new();
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal)
        {
            lock (Messages) Messages.Add(message);
        }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
