using NMSE.Config;
using NMSE.Core;

namespace NMSE.Tests;

/// <summary>
/// Tests for Raw JSON Editor bookmark parsing and per-save configuration storage.
/// </summary>
public class RawJsonBookmarksTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsNamesPathsAndIsolation()
    {
        var bookmarks = new List<RawJsonBookmark>
        {
            new()
            {
                Name = "Starship \"main\"",
                Path = ["PlayerStateData", "ShipOwnership", "[0]"],
                Isolated = false
            },
            new()
            {
                Name = "Exosuit tech",
                Path = ["PlayerStateData", "Inventory", "Tech"],
                Isolated = true
            }
        };

        string json = RawJsonBookmarksLogic.Serialize(bookmarks);
        var parsed = RawJsonBookmarksLogic.Parse(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Starship \"main\"", parsed[0].Name);
        Assert.Equal(bookmarks[0].Path, parsed[0].Path);
        Assert.False(parsed[0].Isolated);
        Assert.True(parsed[1].Isolated);
    }

    [Fact]
    public void Parse_MalformedOrIncompleteEntries_AreIgnored()
    {
        string json = """
            [
              { "name": "", "path": ["A"] },
              { "name": "No path", "path": [] },
              { "name": "Bad path", "path": [1, 2] },
              { "name": "Valid", "path": ["A", "[0]"], "isolated": true },
              { "name": "valid", "path": ["B"] }
            ]
            """;

        var parsed = RawJsonBookmarksLogic.Parse(json);

        Assert.Single(parsed);
        Assert.Equal("Valid", parsed[0].Name);
        Assert.True(parsed[0].Isolated);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(RawJsonBookmarksLogic.Parse("{ not json"));
    }

    [Fact]
    public void AppConfig_BookmarksAreScopedPerDocument()
    {
        var config = new AppConfig();
        var saveABookmarks = new List<RawJsonBookmark>
        {
            new() { Name = "Save A", Path = ["A"] }
        };
        var saveBBookmarks = new List<RawJsonBookmark>
        {
            new() { Name = "Save B", Path = ["B"] }
        };

        config.SetRawJsonBookmarks("save-a", saveABookmarks);
        config.SetRawJsonBookmarks("save-b", saveBBookmarks);

        Assert.Equal("Save A", Assert.Single(config.GetRawJsonBookmarks("save-a")).Name);
        Assert.Equal("Save B", Assert.Single(config.GetRawJsonBookmarks("save-b")).Name);
    }

    [Fact]
    public void AppConfig_RemovingAllBookmarks_ClearsStoredValue()
    {
        var config = new AppConfig();
        config.SetRawJsonBookmarks("save-a", [new RawJsonBookmark { Name = "Test", Path = ["A"] }]);
        Assert.NotEmpty(config.GetRawJsonBookmarks("save-a"));

        config.SetRawJsonBookmarks("save-a", []);
        Assert.Empty(config.GetRawJsonBookmarks("save-a"));
        Assert.Null(config.GetProperty("RawJson.Bookmarks.save-a"));
    }

    [Fact]
    public void IsNameAvailable_IsCaseInsensitive()
    {
        var bookmarks = new List<RawJsonBookmark>
        {
            new() { Name = "Main base", Path = ["A"] }
        };

        Assert.False(RawJsonBookmarksLogic.IsNameAvailable("main BASE", bookmarks));
        Assert.True(RawJsonBookmarksLogic.IsNameAvailable("Other", bookmarks));
        Assert.False(RawJsonBookmarksLogic.IsNameAvailable("  ", bookmarks));
    }
}
