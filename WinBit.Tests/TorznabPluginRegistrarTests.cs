using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Networking;
using WinBit.Core.Search;
using WinBit.Core.Search.Torznab;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests;

public sealed class TorznabPluginRegistrarTests
{
    [Fact]
    public async Task Startup_registers_enabled_feeds_only()
    {
        var ctx = Build();
        ctx.Settings.Current.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "enabled", Url = "http://a", Enabled = true,
        });
        ctx.Settings.Current.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "disabled", Url = "http://b", Enabled = false,
        });

        await ctx.Registrar.StartAsync(CancellationToken.None);

        ctx.Host.Plugins.Select(p => p.Name).Should().Equal("enabled");
    }

    [Fact]
    public async Task Adding_a_feed_registers_it_on_settings_changed()
    {
        var ctx = Build();
        await ctx.Registrar.StartAsync(CancellationToken.None);

        await ctx.Settings.UpdateAsync(s => s.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "new", Url = "http://x", Enabled = true,
        }));

        ctx.Host.Plugins.Select(p => p.Name).Should().Equal("new");
    }

    [Fact]
    public async Task Removing_a_feed_unregisters_it()
    {
        var ctx = Build();
        ctx.Settings.Current.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "gone", Url = "http://a", Enabled = true,
        });
        await ctx.Registrar.StartAsync(CancellationToken.None);

        await ctx.Settings.UpdateAsync(s => s.Search.TorznabFeeds.Clear());

        ctx.Host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabling_a_feed_unregisters_it()
    {
        var ctx = Build();
        var feed = new TorznabFeedDefinition { Name = "toggle", Url = "http://a", Enabled = true };
        ctx.Settings.Current.Search.TorznabFeeds.Add(feed);
        await ctx.Registrar.StartAsync(CancellationToken.None);
        ctx.Host.Plugins.Should().HaveCount(1);

        await ctx.Settings.UpdateAsync(s => s.Search.TorznabFeeds[0].Enabled = false);

        ctx.Host.Plugins.Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_url_re_registers_with_fresh_plugin_instance()
    {
        var ctx = Build();
        ctx.Settings.Current.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "reconfig", Url = "http://old", Enabled = true,
        });
        await ctx.Registrar.StartAsync(CancellationToken.None);
        var before = (TorznabSearchPlugin)ctx.Host.Plugins.Single();
        before.BuildRequestUrl("q").Should().Be("http://old?t=search&q=q");

        await ctx.Settings.UpdateAsync(s => s.Search.TorznabFeeds[0].Url = "http://new");

        var after = (TorznabSearchPlugin)ctx.Host.Plugins.Single();
        after.Should().NotBeSameAs(before);
        after.BuildRequestUrl("q").Should().Be("http://new?t=search&q=q");
    }

    [Fact]
    public async Task Unchanged_feed_is_not_re_registered()
    {
        var ctx = Build();
        ctx.Settings.Current.Search.TorznabFeeds.Add(new TorznabFeedDefinition
        {
            Name = "stable", Url = "http://a", Enabled = true,
        });
        await ctx.Registrar.StartAsync(CancellationToken.None);
        var before = ctx.Host.Plugins.Single();

        // Settings fires without shape changes — the existing instance should stay put.
        await ctx.Settings.UpdateAsync(_ => { });

        ctx.Host.Plugins.Single().Should().BeSameAs(before);
    }

    private static TestContext Build()
    {
        var settings = new InMemorySettings();
        var host = new SearchPluginHost(Array.Empty<ISearchPlugin>(), new NoopLog());
        var registrar = new TorznabPluginRegistrar(host, settings, new NullHttp());
        return new TestContext(registrar, host, settings);
    }

    private sealed record TestContext(
        TorznabPluginRegistrar Registrar,
        SearchPluginHost Host,
        InMemorySettings Settings);

    private sealed class NullHttp : IHttpClientProvider
    {
        public HttpClient Get() => new();
    }

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }

    private sealed class InMemorySettings : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);
        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
        {
            mutate(Current);
            Changed?.Invoke(this, Current);
            return Task.CompletedTask;
        }
        public event EventHandler<AppSettings>? Changed;
    }
}
