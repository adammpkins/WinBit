using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using WinBit.Core.Settings;

namespace WinBit.Core.WebUi;

public sealed class WebUiAuthService : IWebUiAuthService
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "adminadmin";

    private readonly ISettingsService _settings;
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.Ordinal);

    public WebUiAuthService(ISettingsService settings) => _settings = settings;

    public bool ValidateCredentials(string username, string password)
    {
        var webUi = _settings.Current.WebUi;
        var expectedUser = string.IsNullOrWhiteSpace(webUi.Username) ? DefaultUsername : webUi.Username;
        if (!string.Equals(username, expectedUser, StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(webUi.PasswordHash)
            ? string.Equals(password, DefaultPassword, StringComparison.Ordinal)
            : PasswordHasher.Verify(password, webUi.PasswordHash!);
    }

    public string StartSession()
    {
        Span<byte> buffer = stackalloc byte[24];
        RandomNumberGenerator.Fill(buffer);
        var sid = Convert.ToBase64String(buffer)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _sessions[sid] = DateTime.UtcNow;
        return sid;
    }

    public void EndSession(string sid)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            _sessions.TryRemove(sid, out _);
        }
    }

    public bool IsValidSession(string? sid) =>
        !string.IsNullOrEmpty(sid) && _sessions.ContainsKey(sid);

    public bool IsWhitelistedIp(IPAddress? remote)
    {
        if (remote is null)
        {
            return false;
        }

        var webUi = _settings.Current.WebUi;
        var subnets = webUi.WhitelistedSubnets;

        if (subnets is null || subnets.Count == 0)
        {
            return false;
        }

        // Normalize IPv4-mapped IPv6 so v4 CIDRs match loopback clients that arrive as ::1.
        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;

        foreach (var cidr in subnets)
        {
            if (string.IsNullOrWhiteSpace(cidr))
            {
                continue;
            }
            if (!IPNetwork.TryParse(cidr, out var network))
            {
                continue;
            }

            var candidate = network.BaseAddress.AddressFamily == address.AddressFamily
                ? address
                : (network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && remote.IsIPv4MappedToIPv6
                        ? remote.MapToIPv4()
                        : null);

            if (candidate is not null && network.Contains(candidate))
            {
                return true;
            }
        }
        return false;
    }

}
