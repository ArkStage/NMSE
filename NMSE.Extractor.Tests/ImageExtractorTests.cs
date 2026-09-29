using NMSE.Extractor.Data;

namespace NMSE.Extractor.Tests;

public class ImageExtractorTests
{
    [Theory]
    [InlineData("CASING", "CASING")]
    [InlineData("test/file", "test_file")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("", "unknown")]
    public void SanitizeFilename_HandlesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, ImageExtractor.SanitizeFilename(input));
    }

    [Fact]
    public void CollectIdIconPairs_EmptyDir_ReturnsEmpty()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = ImageExtractor.CollectIdIconPairs(tempDir);
            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectIdIconPairs_ReadsJsonFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            string json = """[{"Id":"TEST1","IconPath":"textures/test.dds"},{"Id":"TEST2","IconPath":"textures/test2.dds"}]""";
            File.WriteAllText(Path.Combine(tempDir, "Products.json"), json);

            var result = ImageExtractor.CollectIdIconPairs(tempDir);
            Assert.Equal(2, result.Count);
            Assert.Equal("TEST1", result[0].Id);
            Assert.Equal("textures/test.dds", result[0].IconPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectIdIconPairs_DeduplicatesById()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            string json1 = """[{"Id":"DUPE","IconPath":"textures/a.dds"}]""";
            string json2 = """[{"Id":"DUPE","IconPath":"textures/b.dds"}]""";
            File.WriteAllText(Path.Combine(tempDir, "Products.json"), json1);
            File.WriteAllText(Path.Combine(tempDir, "Technology.json"), json2);

            var result = ImageExtractor.CollectIdIconPairs(tempDir);
            Assert.Single(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Tests that NormalizeExtracted moves (not copies) files and removes original source.
    /// On case-sensitive filesystems, TEXTURES/ -> textures/ with lowercase paths.
    /// On Windows (case-insensitive), the method returns early since paths are equivalent.
    /// </summary>
    [Fact]
    public void NormalizeExtracted_MovesFilesOnCaseSensitiveFS()
    {
        // This test validates the behaviour on whatever filesystem we're running on.
        // On Windows: TEXTURES and textures are the same dir, method returns early.
        // On Linux: files are moved from TEXTURES/ to textures/ with lowercase paths.
        string tempRoot = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid()}");
        string extractedRoot = Path.Combine(tempRoot, "EXTRACTED");

        try
        {
            string texturesDir = Path.Combine(extractedRoot, "TEXTURES");
            string subDir = Path.Combine(texturesDir, "UI");
            Directory.CreateDirectory(subDir);

            // Create some test texture files
            File.WriteAllBytes(Path.Combine(subDir, "icon1.DDS"), [0x01, 0x02]);
            File.WriteAllBytes(Path.Combine(subDir, "ICON2.DDS"), [0x03, 0x04]);

            // Act
            ImageExtractor.NormalizeExtracted(extractedRoot);

            // On case-insensitive (Windows): method returns early, originals still there
            // On case-sensitive (Linux): files moved to textures/ui/icon1.dds, textures/ui/icon2.dds
            string lowerTextures = Path.Combine(extractedRoot, "textures");
            bool caseSensitive = !Path.GetFullPath(Path.Combine(extractedRoot, "TEXTURES"))
                .Equals(Path.GetFullPath(Path.Combine(extractedRoot, "textures")), StringComparison.OrdinalIgnoreCase);

            if (caseSensitive)
            {
                // Linux: files should be in lowercase paths
                Assert.True(File.Exists(Path.Combine(lowerTextures, "ui", "icon1.dds")));
                Assert.True(File.Exists(Path.Combine(lowerTextures, "ui", "icon2.dds")));
            }
            else
            {
                // Windows: original files should still exist (method returned early)
                Assert.True(File.Exists(Path.Combine(subDir, "icon1.DDS")));
                Assert.True(File.Exists(Path.Combine(subDir, "ICON2.DDS")));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CollectIdIconPairs_ScansNonPreferredJsonFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            string json = """[{"Id":"STA_00","IconPath":"textures/buildable.sta_00.dds"}]""";
            File.WriteAllText(Path.Combine(tempDir, "Station.json"), json);

            var result = ImageExtractor.CollectIdIconPairs(tempDir);
            Assert.Single(result);
            Assert.Equal("STA_00", result[0].Id);
            Assert.Equal("textures/buildable.sta_00.dds", result[0].IconPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectIdIconPairs_PreservesPreferredFilePriority()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // "Aaa.json" sorts before "Products.json" but Products.json is preferred, so
            // its icon must win for the duplicate ID.
            File.WriteAllText(Path.Combine(tempDir, "Aaa.json"),
                """[{"Id":"DUPE","IconPath":"textures/aaa.dds"}]""");
            File.WriteAllText(Path.Combine(tempDir, "Products.json"),
                """[{"Id":"DUPE","IconPath":"textures/products.dds"}]""");

            var result = ImageExtractor.CollectIdIconPairs(tempDir);
            Assert.Single(result);
            Assert.Equal("textures/products.dds", result[0].IconPath);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CollectCuratedIconPairs_IncludesProceduralMappings()
    {
        var result = ImageExtractor.CollectCuratedIconPairs(Path.GetTempPath());

        var loot = Assert.Single(result.Where(p => p.Id == "PROC_LOOT"));
        Assert.Equal("textures/ui/frontend/icons/update3/treasureprod.coincrate.dds", loot.IconPath);
        Assert.Contains(result, p => p.Id == "PROC_STAR");
        Assert.Contains(result, p => p.Id == "PROC_DARK");
    }

    [Fact]
    public void CollectCuratedIconPairs_EnumeratesTechBoxIcons()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid()}");
        try
        {
            string proctechDir = Path.Combine(tempRoot, "textures", "ui", "frontend", "icons", "u4products", "proctech");
            Directory.CreateDirectory(proctechDir);
            File.WriteAllBytes(Path.Combine(proctechDir, "techbox.binocs.dds"), [0x01]);
            File.WriteAllBytes(Path.Combine(proctechDir, "techbox.vehicleboost.dds"), [0x02]);
            File.WriteAllBytes(Path.Combine(proctechDir, "proctech.a.binocs.dds"), [0x03]);

            var result = ImageExtractor.CollectCuratedIconPairs(tempRoot);

            var binocs = Assert.Single(result.Where(p => p.Id == "TECHBOX_BINOCS"));
            Assert.Equal("textures/ui/frontend/icons/u4products/proctech/techbox.binocs.dds", binocs.IconPath);
            Assert.Contains(result, p => p.Id == "TECHBOX_VEHICLEBOOST");
            Assert.DoesNotContain(result, p => p.Id == "PROCTECH.A.BINOCS");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }
}
