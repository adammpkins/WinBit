namespace WinBit.Core.Networking;

/// <summary>
/// Supplies a shared <see cref="HttpClient"/> configured from the current proxy settings.
/// Rebuilds the instance when the settings hash changes so the UrlDownloader (and any future
/// HTTP callers) transparently pick up proxy edits.
/// </summary>
public interface IHttpClientProvider
{
    HttpClient Get();
}
