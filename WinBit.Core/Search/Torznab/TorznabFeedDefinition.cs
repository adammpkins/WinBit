namespace WinBit.Core.Search.Torznab;

/// <summary>
/// A user-configured Torznab endpoint. <see cref="Url"/> is typically the Jackett indexer's
/// <c>/api?apikey=…&amp;t=search&amp;q=…</c> endpoint (without the query string). <see cref="Name"/>
/// is used as the <see cref="ISearchPlugin.Name"/>; pick something lowercase-no-spaces so the
/// Search page's plugin filter stays tidy.
/// </summary>
public sealed class TorznabFeedDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
}
