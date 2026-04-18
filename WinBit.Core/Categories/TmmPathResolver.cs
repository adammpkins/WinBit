namespace WinBit.Core.Categories;

/// <summary>
/// Ports qBittorrent's <c>SessionImpl::categorySavePath</c> (from
/// <c>qbittorrent/src/base/bittorrent/sessionimpl.cpp</c>) into idiomatic C#. Rules:
/// <list type="bullet">
///   <item>No category → global save path.</item>
///   <item>Category with explicit absolute save path → that path verbatim.</item>
///   <item>Category with explicit relative save path → combined with the resolved parent path.</item>
///   <item>Category with no save path → subcategory name appended to the parent's resolved path
///     (the "implicit save path" rule).</item>
///   <item>Nested categories use <c>/</c> as the separator; parent resolution is recursive.</item>
/// </list>
/// Parity test fixtures live in the next M5 deliverable.
/// </summary>
public static class TmmPathResolver
{
    public static string Resolve(string globalSavePath, string? categoryName, Func<string, Category?> lookup)
    {
        if (string.IsNullOrEmpty(categoryName))
        {
            return globalSavePath;
        }

        var options = lookup(categoryName);
        var path = options?.SavePath;
        var basePath = globalSavePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            // Implicit save path: append the leaf category name to the parent's resolved path.
            path = SubcategoryName(categoryName);
            basePath = Resolve(globalSavePath, ParentCategoryName(categoryName), lookup);
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(basePath, path);
    }

    public static string SubcategoryName(string categoryName)
    {
        var idx = categoryName.LastIndexOf('/');
        return idx < 0 ? categoryName : categoryName[(idx + 1)..];
    }

    public static string ParentCategoryName(string categoryName)
    {
        var idx = categoryName.LastIndexOf('/');
        return idx < 0 ? string.Empty : categoryName[..idx];
    }
}
