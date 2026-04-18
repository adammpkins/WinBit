namespace WinBit.Core.Updates;

/// <summary>Result of comparing the running build to the latest published GitHub release.</summary>
public sealed record UpdateInfo(
    Version Current,
    Version? Latest,
    string? LatestTag,
    string? ReleaseUrl,
    bool HasUpdate);
