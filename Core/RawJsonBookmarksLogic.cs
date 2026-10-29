using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// A named Raw JSON Editor path. Bookmarks are scoped to a save or account file
/// and can optionally restore Split view with Isolate Node enabled.
/// </summary>
public sealed class RawJsonBookmark
{
    /// <summary>Gets or sets the user-visible bookmark name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the raw tree path segments.</summary>
    public string[] Path { get; set; } = [];

    /// <summary>Gets or sets whether the bookmark opens the node in Split view with isolation.</summary>
    public bool Isolated { get; set; }

    /// <summary>Returns the bookmark name for display in list controls.</summary>
    public override string ToString() => Name;

    /// <summary>Creates an independent copy of the bookmark.</summary>
    public RawJsonBookmark Clone() => new()
    {
        Name = Name,
        Path = [.. Path],
        Isolated = Isolated
    };
}

/// <summary>
/// Parsing, validation and serialisation for Raw JSON Editor bookmarks.
/// No UI dependencies.
/// </summary>
public static class RawJsonBookmarksLogic
{
    /// <summary>Parses a compact JSON bookmark array. Malformed entries are ignored.</summary>
    /// <param name="json">The stored JSON value, or null/empty.</param>
    /// <returns>A de-duplicated bookmark list in stored order.</returns>
    public static List<RawJsonBookmark> Parse(string? json)
    {
        var result = new List<RawJsonBookmark>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        JsonArray array;
        try
        {
            array = JsonArray.Parse(json);
        }
        catch
        {
            return result;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < array.Length; i++)
        {
            if (array.Get(i) is not JsonObject obj)
                continue;

            string? name = obj.GetString("name")?.Trim();
            if (string.IsNullOrEmpty(name) || !names.Add(name))
                continue;

            var pathArray = obj.GetArray("path");
            if (pathArray == null || pathArray.Length == 0)
                continue;

            var path = new string[pathArray.Length];
            bool valid = true;
            for (int p = 0; p < pathArray.Length; p++)
            {
                if (pathArray.Get(p) is not string segment)
                {
                    valid = false;
                    break;
                }
                path[p] = segment;
            }
            if (!valid)
                continue;

            bool isolated = false;
            try
            {
                isolated = obj.GetBool("isolated");
            }
            catch
            {
            }

            result.Add(new RawJsonBookmark { Name = name, Path = path, Isolated = isolated });
        }

        return result;
    }

    /// <summary>Serialises bookmarks to the compact JSON stored in AppConfig.</summary>
    /// <param name="bookmarks">The bookmarks to serialise.</param>
    /// <returns>A compact JSON array.</returns>
    public static string Serialize(IEnumerable<RawJsonBookmark> bookmarks)
    {
        var array = new JsonArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bookmark in bookmarks)
        {
            string name = (bookmark.Name ?? "").Trim();
            if (string.IsNullOrEmpty(name) || !names.Add(name)
                || bookmark.Path == null || bookmark.Path.Length == 0)
            {
                continue;
            }

            var path = new JsonArray();
            foreach (string segment in bookmark.Path)
                path.Add(segment);

            var obj = new JsonObject();
            obj.Set("name", name);
            obj.Set("path", path);
            obj.Set("isolated", bookmark.Isolated);
            array.Add(obj);
        }
        return array.ToString();
    }

    /// <summary>Gets whether a name is non-empty and not already used.</summary>
    public static bool IsNameAvailable(string? name, IEnumerable<RawJsonBookmark> bookmarks)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            return false;
        return !bookmarks.Any(b =>
            string.Equals(b.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a bookmark by name, case-insensitively.</summary>
    public static RawJsonBookmark? FindByName(IEnumerable<RawJsonBookmark> bookmarks, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return bookmarks.FirstOrDefault(b =>
            string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Creates an independent copy of a bookmark list.</summary>
    public static List<RawJsonBookmark> CloneAll(IEnumerable<RawJsonBookmark> bookmarks) =>
        bookmarks.Select(b => b.Clone()).ToList();
}
