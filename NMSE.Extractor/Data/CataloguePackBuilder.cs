using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace NMSE.Extractor.Data;

/// <summary>
/// Builds the derived catalogue completion data from the game's catalogue tables
/// and parsed data tables, writing it to <c>Catalogue Pack.json</c> in the JSON
/// output directory. Values that cannot be derived from game files (save-observed
/// wonder data, discovery stat targets, story completers and the account Seen
/// lists) are compiled into the editor instead.
/// </summary>
public static class CataloguePackBuilder
{
    /// <summary>Number of race slots in a save's KnownWordGroups Races array.</summary>
    private const int WordRaceCount = 9;

    /// <summary>Valid race ordinals; the save indexes the race flags by ordinal.</summary>
    private static readonly int[] WordRaceOrdinals = [0, 1, 2, 4, 8];

    private static readonly Dictionary<string, string> CatalogueTableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CatalogueMaterials"] = "cataloguematerials.MXML",
        ["CatalogueBuilding"] = "cataloguebuilding.MXML",
        ["CatalogueCrafting"] = "cataloguecrafting.MXML",
    };

    /// <summary>
    /// Writes the derived catalogue pack file. Lists that cannot be derived are
    /// omitted so the editor falls back to its own known values.
    /// </summary>
    /// <param name="jsonDir">The extractor JSON output directory.</param>
    /// <param name="baseData">Parsed game data keyed by source name.</param>
    /// <param name="mbinDir">Directory containing converted MXML files.</param>
    public static void WriteCataloguePack(
        string jsonDir,
        Dictionary<string, List<Dictionary<string, object?>>> baseData,
        string mbinDir)
    {
        var root = new Dictionary<string, object?>
        {
            ["KnownRefinerRecipes"] = ReadIds(baseData, "Recipes"),
            ["KnownWordGroups"] = BuildWordGroups(baseData),
            ["Fossils"] = BuildFossils(baseData),
            ["KnownPortalRunes"] = 65535,
        };

        foreach (var (key, fileName) in CatalogueTableKeys)
            root[key] = ReadCatalogueItems(Path.Combine(mbinDir, fileName));

        root["StoryPages"] = ReadStoryPages(Path.Combine(mbinDir, "storiestable.MXML"));

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string path = Path.Combine(jsonDir, "Catalogue Pack.json");
        File.WriteAllText(path, JsonSerializer.Serialize(root, options));
        Console.WriteLine($"[OK] Catalogue Pack: {((List<string>)root["KnownRefinerRecipes"]!).Count} recipes, " +
            $"{((List<Dictionary<string, object?>>)root["KnownWordGroups"]!).Count} word groups, " +
            $"{((List<string>)root["Fossils"]!).Count} fossils, " +
            $"{((List<string>)root["CatalogueMaterials"]!).Count} catalogue materials, " +
            $"{((List<string>)root["CatalogueBuilding"]!).Count} catalogue building, " +
            $"{((List<string>)root["CatalogueCrafting"]!).Count} catalogue technology, " +
            $"{((List<Dictionary<string, object?>>)root["StoryPages"]!).Count} story pages");
    }

    /// <summary>
    /// Reads the game's story page table. The category order maps to the save's
    /// SeenStories slot and the page order within a category to the page index, which
    /// lets the editor derive Collected Knowledge completion targets for the current
    /// game build (entry counts change between updates).
    /// </summary>
    /// <param name="mxmlPath">Path to the converted storiestable MXML.</param>
    /// <returns>One entry per story page with slot, page index, entry count, stat and bitmask flag.</returns>
    private static List<Dictionary<string, object?>> ReadStoryPages(string mxmlPath)
    {
        var result = new List<Dictionary<string, object?>>();
        if (!File.Exists(mxmlPath)) return result;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            var table = doc.Root?.Element("Property");
            if (table == null) return result;

            int slot = 0;
            foreach (var category in table.Elements("Property"))
            {
                var pagesContainer = category.Elements("Property")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == "Pages");
                if (pagesContainer != null)
                {
                    int pageIndex = 0;
                    foreach (var page in pagesContainer.Elements("Property"))
                    {
                        var entriesContainer = page.Elements("Property")
                            .FirstOrDefault(e => e.Attribute("name")?.Value == "Entries");
                        int entries = entriesContainer?.Elements("Property").Count() ?? 0;
                        string stat = page.Elements("Property")
                            .FirstOrDefault(e => e.Attribute("name")?.Value == "Stat")?.Attribute("value")?.Value ?? "";
                        bool bitmask = string.Equals(
                            page.Elements("Property")
                                .FirstOrDefault(e => e.Attribute("name")?.Value == "StatIsBitmask")?.Attribute("value")?.Value,
                            "true", StringComparison.OrdinalIgnoreCase);
                        string id = page.Attribute("_id")?.Value
                            ?? page.Elements("Property")
                                .FirstOrDefault(e => e.Attribute("name")?.Value == "ID")?.Attribute("value")?.Value ?? "";

                        result.Add(new Dictionary<string, object?>
                        {
                            ["Slot"] = slot,
                            ["Page"] = pageIndex,
                            ["Id"] = id,
                            ["Entries"] = entries,
                            ["Stat"] = stat,
                            ["Bitmask"] = bitmask,
                        });
                        pageIndex++;
                    }
                }
                slot++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Story table {Path.GetFileName(mxmlPath)}: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Reads the explicit item lists of a catalogue table (the custom list categories).
    /// </summary>
    private static List<string> ReadCatalogueItems(string mxmlPath)
    {
        var result = new List<string>();
        if (!File.Exists(mxmlPath)) return result;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            foreach (var category in doc.Descendants("Property")
                .Where(e => e.Attribute("name")?.Value == "Categories"))
            {
                foreach (var items in category.Elements("Property")
                    .Where(e => e.Attribute("name")?.Value == "Items"))
                {
                    foreach (var item in items.Elements("Property"))
                    {
                        string? id = item.Attribute("value")?.Value;
                        if (!string.IsNullOrEmpty(id) && !result.Contains(id, StringComparer.OrdinalIgnoreCase))
                            result.Add(id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Catalogue table {Path.GetFileName(mxmlPath)}: {ex.Message}");
        }
        return result;
    }

    private static List<string> ReadIds(
        Dictionary<string, List<Dictionary<string, object?>>> baseData, string key)
    {
        var ids = new List<string>();
        if (!baseData.TryGetValue(key, out var entries)) return ids;

        foreach (var entry in entries)
        {
            string id = entry.GetValueOrDefault("Id")?.ToString() ?? "";
            if (id.Length > 0 && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                ids.Add(id);
        }
        return ids;
    }

    private static List<string> BuildFossils(
        Dictionary<string, List<Dictionary<string, object?>>> baseData)
    {
        var fossils = new List<string>();
        if (!baseData.TryGetValue("ShipComponents", out var components)) return fossils;

        foreach (var component in components)
        {
            string id = component.GetValueOrDefault("Id")?.ToString() ?? "";
            if (id.StartsWith("FOS_", StringComparison.OrdinalIgnoreCase) && !fossils.Contains(id, StringComparer.OrdinalIgnoreCase))
                fossils.Add(id);
        }
        return fossils;
    }

    private static List<Dictionary<string, object?>> BuildWordGroups(
        Dictionary<string, List<Dictionary<string, object?>>> baseData)
    {
        // Collect every group with the race ordinals that map to it.
        var raceFlags = new Dictionary<string, bool[]>(StringComparer.OrdinalIgnoreCase);
        if (baseData.TryGetValue("Words", out var words))
        {
            foreach (var word in words)
            {
                if (word.GetValueOrDefault("Groups") is not Dictionary<string, object?> groups) continue;
                foreach (var (group, ordinalValue) in groups)
                {
                    string groupId = group.TrimStart('^');
                    if (groupId.Length == 0) continue;

                    if (!raceFlags.TryGetValue(groupId, out var flags))
                    {
                        flags = new bool[WordRaceCount];
                        raceFlags[groupId] = flags;
                    }

                    int ordinal = ordinalValue switch
                    {
                        int i => i,
                        long l => (int)l,
                        _ => -1
                    };
                    if (ordinal >= 0 && ordinal < WordRaceCount && Array.IndexOf(WordRaceOrdinals, ordinal) >= 0)
                        flags[ordinal] = true;
                }
            }
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var (group, flags) in raceFlags.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new Dictionary<string, object?>
            {
                ["Group"] = "^" + group,
                ["Races"] = flags,
            });
        }
        return result;
    }
}
