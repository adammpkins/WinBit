using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using WinBit.Core.Settings;
using Xunit;

namespace WinBit.Tests.Settings;

public sealed class SettingsServiceTests
{
    /// <summary>
    /// In-memory <see cref="ISettingsStore"/> that deep-clones on each save/load via JSON
    /// round-trip, ensuring tests exercise real persistence semantics without debounce races.
    /// </summary>
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
        };

        private string? _json;

        public Task<AppSettings?> LoadAsync(CancellationToken ct = default)
        {
            if (_json is null)
            {
                return Task.FromResult<AppSettings?>(null);
            }

            var result = JsonSerializer.Deserialize<AppSettings>(_json, JsonOptions);
            return Task.FromResult(result);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            // Clone immediately so the stored snapshot is independent of the live object.
            _json = JsonSerializer.Serialize(settings, JsonOptions);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LoadAsync_returns_default_when_store_is_empty()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Should().NotBeNull();
        service.Current.Downloads.Should().NotBeNull();
        service.Current.Connection.Should().NotBeNull();
        service.Current.Connection.ListenPort.Should().Be(6881);
        service.Current.Speed.Should().NotBeNull();
        service.Current.BitTorrent.Should().NotBeNull();
        service.Current.Rss.Should().NotBeNull();
        service.Current.WebUi.Should().NotBeNull();
        service.Current.Advanced.Should().NotBeNull();
        service.Current.UiState.Should().NotBeNull();
        service.Current.Behavior.Should().NotBeNull();
    }

    [Fact]
    public async Task LoadAsync_fires_Changed_event()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        var fireCount = 0;
        AppSettings? received = null;
        service.Changed += (_, s) =>
        {
            fireCount++;
            received = s;
        };

        await service.LoadAsync();

        fireCount.Should().Be(1);
        received.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_applies_mutation_to_Current()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        await service.UpdateAsync(s => s.Connection.ListenPort = 9999);

        service.Current.Connection.ListenPort.Should().Be(9999);
    }

    [Fact]
    public async Task UpdateAsync_fires_Changed_event_after_save()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        var fired = false;
        service.Changed += (_, _) => fired = true;

        await service.UpdateAsync(s => s.Connection.ListenPort = 1234);

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_persists_across_reload()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        await service.UpdateAsync(s => s.Connection.ListenPort = 7777);

        // A fresh service on the same store should see the persisted value.
        var service2 = new SettingsService(store);
        await service2.LoadAsync();

        service2.Current.Connection.ListenPort.Should().Be(7777);
    }

    [Fact]
    public async Task SaveAsync_and_reload_round_trips_scalar_value()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        await service.UpdateAsync(s => s.WebUi.Port = 9090);
        await service.SaveAsync();

        var service2 = new SettingsService(store);
        await service2.LoadAsync();

        service2.Current.WebUi.Port.Should().Be(9090);
    }

    [Fact]
    public async Task Changed_event_fires_for_all_subscribers()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        var firstFired = false;
        var secondFired = false;
        service.Changed += (_, _) => firstFired = true;
        service.Changed += (_, _) => secondFired = true;

        await service.UpdateAsync(s => s.Connection.ListenPort = 5050);

        firstFired.Should().BeTrue();
        secondFired.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_UpdateAsync_calls_serialize_without_corruption()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();

        // Each task sets a distinct port value (i+1000) so any tear or lost write is visible.
        var tasks = Enumerable.Range(0, 20)
            .Select(i => service.UpdateAsync(s => s.Connection.ListenPort = i + 1000))
            .ToArray();

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        // The final value must be one of the 20 valid ports — no torn state.
        var finalPort = service.Current.Connection.ListenPort;
        finalPort.Should().BeInRange(1000, 1019);
    }
}
