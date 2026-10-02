using System.Text.Json;
using NMSE.Extractor.Data;

namespace NMSE.Extractor.Tests;

public class CataloguePackBuilderTests
{
    [Fact]
    public void WriteCataloguePack_EmitsStoryPageEntries()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nmse_pack_story_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "storiestable.MXML"), """
<?xml version="1.0" encoding="utf-8"?>
<Data template="cGcStoriesTable">
  <Property name="Table">
    <Property name="Traders" value="GcStoryCategory">
      <Property name="Pages">
        <Property name="Pages" value="GcStoryPage" _id="UI_PORTAL_TRA_PLAQUE_TITLE">
          <Property name="Stat" value="" />
          <Property name="StatIsBitmask" value="false" />
          <Property name="Entries">
            <Property name="Entries" value="GcStoryEntry" _index="0" />
            <Property name="Entries" value="GcStoryEntry" _index="1" />
          </Property>
        </Property>
      </Property>
    </Property>
    <Property name="Atlas" value="GcStoryCategory">
      <Property name="Pages">
        <Property name="Pages" value="GcStoryPage" _id="INTRCT_ATLASSTATION">
          <Property name="Stat" value="ATLAS_LORE" />
          <Property name="StatIsBitmask" value="false" />
          <Property name="Entries">
            <Property name="Entries" value="GcStoryEntry" _index="0" />
          </Property>
        </Property>
      </Property>
    </Property>
  </Property>
</Data>
""");

            CataloguePackBuilder.WriteCataloguePack(dir, new Dictionary<string, List<Dictionary<string, object?>>>(), dir);

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Catalogue Pack.json")));
            var pages = document.RootElement.GetProperty("StoryPages");
            Assert.Equal(2, pages.GetArrayLength());

            Assert.Equal(0, pages[0].GetProperty("Slot").GetInt32());
            Assert.Equal(0, pages[0].GetProperty("Page").GetInt32());
            Assert.Equal("UI_PORTAL_TRA_PLAQUE_TITLE", pages[0].GetProperty("Id").GetString());
            Assert.Equal(2, pages[0].GetProperty("Entries").GetInt32());
            Assert.Equal("", pages[0].GetProperty("Stat").GetString());
            Assert.False(pages[0].GetProperty("Bitmask").GetBoolean());

            Assert.Equal(1, pages[1].GetProperty("Slot").GetInt32());
            Assert.Equal("INTRCT_ATLASSTATION", pages[1].GetProperty("Id").GetString());
            Assert.Equal("ATLAS_LORE", pages[1].GetProperty("Stat").GetString());
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void WriteCataloguePack_DerivesRecipesFossilsAndWordGroups()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"nmse_pack_builder_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var baseData = new Dictionary<string, List<Dictionary<string, object?>>>
            {
                ["Recipes"] =
                [
                    new() { ["Id"] = "RECIPE_1" },
                    new() { ["Id"] = "RECIPE_2" },
                ],
                ["ShipComponents"] =
                [
                    new() { ["Id"] = "FOS_HEAD_AA" },
                    new() { ["Id"] = "FIGHT_COCKAA" },
                ],
                ["Words"] =
                [
                    new()
                    {
                        ["Groups"] = new Dictionary<string, object?>
                        {
                            ["^TRA_A"] = 0,
                            ["^TRA_B"] = 4,
                        },
                    },
                ],
            };

            File.WriteAllText(Path.Combine(dir, "cataloguematerials.MXML"), """
<?xml version="1.0" encoding="utf-8"?>
<Data template="cGcWiki">
  <Property name="Categories">
    <Property name="Categories" value="GcWikiCategory" _index="0">
      <Property name="Items">
        <Property name="Items" value="MAT_A" _index="0" />
        <Property name="Items" value="MAT_B" _index="1" />
      </Property>
    </Property>
  </Property>
</Data>
""");

            CataloguePackBuilder.WriteCataloguePack(dir, baseData, dir);

            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Catalogue Pack.json")));
            var root = document.RootElement;

            Assert.Equal(2, root.GetProperty("KnownRefinerRecipes").GetArrayLength());
            Assert.Equal(65535, root.GetProperty("KnownPortalRunes").GetInt32());

            var materials = root.GetProperty("CatalogueMaterials");
            Assert.Equal(2, materials.GetArrayLength());
            Assert.Equal("MAT_A", materials[0].GetString());
            Assert.Equal("MAT_B", materials[1].GetString());
            Assert.Equal(0, root.GetProperty("CatalogueBuilding").GetArrayLength());
            Assert.Equal(0, root.GetProperty("CatalogueCrafting").GetArrayLength());

            var fossils = root.GetProperty("Fossils");
            Assert.Equal(1, fossils.GetArrayLength());
            Assert.Equal("FOS_HEAD_AA", fossils[0].GetString());

            var groups = root.GetProperty("KnownWordGroups");
            Assert.Equal("^TRA_A", groups[0].GetProperty("Group").GetString());
            Assert.Equal(9, groups[0].GetProperty("Races").GetArrayLength());
            Assert.True(groups[0].GetProperty("Races")[0].GetBoolean());
            Assert.Equal("^TRA_B", groups[1].GetProperty("Group").GetString());
            Assert.Equal(9, groups[1].GetProperty("Races").GetArrayLength());
            Assert.True(groups[1].GetProperty("Races")[4].GetBoolean()); // Atlas ordinal 4 -> index 4
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
