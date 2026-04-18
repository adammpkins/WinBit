namespace WinBit.Core.WebUi;

/// <summary>
/// Session bookkeeping for the Web UI. In-memory by design — sessions should not survive a
/// host restart.
/// </summary>
public interface IWebUiAuthService
{
    /// <summary>Validates username + password against the current settings, falling back to the
    /// documented default (<c>admin</c> / <c>adminadmin</c>) when no hash is configured.</summary>
    bool ValidateCredentials(string username, string password);

    /// <summary>Creates a new session token; returns the opaque SID cookie value.</summary>
    string StartSession();

    /// <summary>Drops the session identified by the given SID. No-op if unknown.</summary>
    void EndSession(string sid);

    /// <summary>True when the SID currently maps to an active session.</summary>
    bool IsValidSession(string? sid);

    /// <summary>
    /// True when <paramref name="remote"/> falls inside any configured
    /// <c>AppSettings.WebUi.WhitelistedSubnets</c> entry. Implementations must tolerate both
    /// IPv4 and IPv4-mapped-IPv6 addresses (loopback clients often arrive as <c>::1</c> or
    /// <c>::ffff:127.0.0.1</c>).
    /// </summary>
    bool IsWhitelistedIp(System.Net.IPAddress? remote);
}
