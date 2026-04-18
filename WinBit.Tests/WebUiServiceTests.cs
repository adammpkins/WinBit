using FluentAssertions;
using WinBit.Core.Logging;
using WinBit.Core.Settings;
using WinBit.Core.WebUi;
using Xunit;

namespace WinBit.Tests;

public sealed class WebUiServiceTests
{
    [Fact]
    public async Task Starts_and_serves_version_endpoint_when_enabled()
    {
        var settings = new InMemorySettings();
        settings.Current.WebUi.Enabled = true;
        settings.Current.WebUi.Port = 0; // ephemeral

        var service = new WebUiService(settings, new NoopLog());
        await service.StartAsync(CancellationToken.None);
        try
        {
            service.IsRunning.Should().BeTrue();
            service.BoundPort.Should().NotBeNull().And.NotBe(0);

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{service.BoundPort}") };
            var body = await client.GetStringAsync("/api/v2/app/version");

            body.Should().Be(WebUiService.VersionString);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Does_nothing_when_disabled()
    {
        var settings = new InMemorySettings();
        settings.Current.WebUi.Enabled = false;
        settings.Current.WebUi.Port = 0;

        var service = new WebUiService(settings, new NoopLog());
        await service.StartAsync(CancellationToken.None);
        try
        {
            service.IsRunning.Should().BeFalse();
            service.BoundPort.Should().BeNull();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Stop_releases_port_so_next_start_binds_a_new_one()
    {
        var settings = new InMemorySettings();
        settings.Current.WebUi.Enabled = true;
        settings.Current.WebUi.Port = 0;

        var service = new WebUiService(settings, new NoopLog());
        await service.StartAsync(CancellationToken.None);
        var first = service.BoundPort;
        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
        service.BoundPort.Should().BeNull();

        // Restart and make sure it still works — port may differ.
        await service.StartAsync(CancellationToken.None);
        try
        {
            service.IsRunning.Should().BeTrue();
            service.BoundPort.Should().NotBeNull();
            first.Should().NotBeNull();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
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

    private sealed class NoopLog : ILogService
    {
        public IReadOnlyList<LogEntry> GetMessages(long afterId = -1, LogSeverity filter = LogSeverity.All) => Array.Empty<LogEntry>();
        public void Write(string message, LogSeverity severity = LogSeverity.Normal) { }
        public event EventHandler<LogEntry>? MessageLogged { add { } remove { } }
    }
}
