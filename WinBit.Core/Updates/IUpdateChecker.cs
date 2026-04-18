namespace WinBit.Core.Updates;

/// <summary>
/// Queries the upstream release feed and reports whether a newer build is published. The
/// default <see cref="GitHubUpdateChecker"/> hits the GitHub REST releases endpoint; tests swap
/// in a stub.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateInfo> CheckAsync(CancellationToken ct = default);
}
