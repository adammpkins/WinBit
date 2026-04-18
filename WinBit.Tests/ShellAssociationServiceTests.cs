using FluentAssertions;
using WinBit.Core.Shell;
using Xunit;

namespace WinBit.Tests;

public sealed class ShellAssociationServiceTests
{
    [Fact]
    public async Task RegisterAsync_writes_both_torrent_and_magnet_when_requested()
    {
        var writer = new FakeWriter();
        var service = new ShellAssociationService(writer, @"C:\Program Files\WinBit\WinBit.exe");

        await service.RegisterAsync(torrent: true, magnet: true);

        writer.Defaults.Should().ContainKey(".torrent").WhoseValue.Should().Be("WinBit.Torrent");
        writer.Defaults.Should().ContainKey(@"magnet\shell\open\command");
        writer.NamedValues.Should().ContainKey(("magnet", "URL Protocol"));
    }

    [Fact]
    public async Task RegisterAsync_respects_flags_independently()
    {
        var writer = new FakeWriter();
        var service = new ShellAssociationService(writer, @"C:\WinBit.exe");

        await service.RegisterAsync(torrent: true, magnet: false);

        writer.Defaults.Should().ContainKey(".torrent");
        writer.Defaults.Should().NotContainKey("magnet");
    }

    [Fact]
    public async Task UnregisterAsync_removes_the_top_level_class_keys()
    {
        var writer = new FakeWriter();
        var service = new ShellAssociationService(writer, @"C:\WinBit.exe");
        await service.RegisterAsync(true, true);

        await service.UnregisterAsync(true, true);

        writer.Deleted.Should().Contain(".torrent");
        writer.Deleted.Should().Contain("WinBit.Torrent");
        writer.Deleted.Should().Contain("magnet");
        writer.Deleted.Should().Contain("WinBit.Magnet");
    }

    [Fact]
    public async Task GetStatus_reports_true_only_when_both_class_entries_point_at_our_exe()
    {
        var writer = new FakeWriter();
        var exe = @"C:\WinBit\WinBit.exe";
        var service = new ShellAssociationService(writer, exe);

        service.GetStatus().Should().Be(new ShellAssociationStatus(false, false));

        await service.RegisterAsync(true, true);
        service.GetStatus().Should().Be(new ShellAssociationStatus(true, true));

        // If some other app stomps the shell\open\command value, magnet should flip back to false.
        writer.Defaults[@"magnet\shell\open\command"] = @"""C:\Other\other.exe"" ""%1""";
        service.GetStatus().MagnetProtocol.Should().BeFalse();
    }

    [Fact]
    public async Task Shell_open_command_quotes_the_exe_path()
    {
        var writer = new FakeWriter();
        var service = new ShellAssociationService(writer, @"C:\My App\WinBit.exe");
        await service.RegisterAsync(true, true);

        writer.Defaults[@"WinBit.Torrent\shell\open\command"]
            .Should().Be("\"C:\\My App\\WinBit.exe\" \"%1\"");
        writer.Defaults[@"magnet\shell\open\command"]
            .Should().Be("\"C:\\My App\\WinBit.exe\" \"%1\"");
    }

    private sealed class FakeWriter : IAssociationRegistryWriter
    {
        public Dictionary<string, string> Defaults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<(string Key, string Name), string> NamedValues { get; } = new();
        public List<string> Deleted { get; } = new();

        public string? ReadClassDefault(string key) =>
            Defaults.TryGetValue(key, out var v) ? v : null;

        public void WriteClassDefault(string key, string value) => Defaults[key] = value;

        public void WriteClassValue(string key, string name, string value) =>
            NamedValues[(key, name)] = value;

        public void DeleteClassKey(string key)
        {
            Deleted.Add(key);
            Defaults.Remove(key);
            foreach (var k in NamedValues.Keys.Where(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                NamedValues.Remove(k);
            }
        }
    }
}
