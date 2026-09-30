using NMSE.IO;

namespace NMSE.Tests;

/// <summary>
/// Tests for the save directory auto-detection helpers: profile and Xbox container
/// enumeration, multi-store detection and parent-folder drill-down.
/// </summary>
public class SaveDirectoryDetectionTests : IDisposable
{
    private readonly string _root;

    public SaveDirectoryDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nmse_savedir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Dir(params string[] parts)
    {
        string path = Path.Combine(new[] { _root }.Concat(parts).ToArray());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string directory, string name)
        => File.WriteAllText(Path.Combine(directory, name), "");

    [Fact]
    public void EnumerateSteamProfiles_ReturnsProfilesNewestFirst_AndSkipsNonProfiles()
    {
        string nmsRoot = Dir("appdata", "HelloGames", "NMS");
        string defaultUser = Dir("appdata", "HelloGames", "NMS", "DefaultUser");
        string steamProfile = Dir("appdata", "HelloGames", "NMS", "st_12345");
        string cache = Dir("appdata", "HelloGames", "NMS", "cache");
        WriteFile(defaultUser, "accountdata.hg");
        WriteFile(steamProfile, "save.hg");
        WriteFile(cache, "SEASON_DATA_CACHE.JSON");
        File.SetLastWriteTimeUtc(Path.Combine(defaultUser, "accountdata.hg"),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(steamProfile, "save.hg"),
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        var profiles = SaveFileManager.EnumerateSteamProfiles(nmsRoot);

        Assert.Equal(2, profiles.Count);
        Assert.Equal(steamProfile, profiles[0]); // most recently written first
        Assert.Equal(defaultUser, profiles[1]);
        Assert.DoesNotContain(cache, profiles);
    }

    [Fact]
    public void EnumerateSteamProfiles_EmptyOrMissingRoot_ReturnsEmpty()
    {
        Assert.Empty(SaveFileManager.EnumerateSteamProfiles(Path.Combine(_root, "missing")));
        Assert.Empty(SaveFileManager.EnumerateSteamProfiles(Dir("emptyNms")));
    }

    [Fact]
    public void EnumerateXboxContainers_PrefersXgsContainerWithSlotsOverWgs()
    {
        string xgsContainer = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "AAAA_1111");
        WriteFile(Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "AAAA_1111", "AccountData"), "data");
        WriteFile(Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "AAAA_1111", "Slot1Auto"), "data");
        string wgsContainer = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "wgs", "AAAA_1111");
        WriteFile(wgsContainer, "containers.index");

        var containers = SaveFileManager.EnumerateXboxContainers(Path.Combine(_root, "Packages"));

        Assert.Single(containers);
        Assert.Equal(xgsContainer, containers[0]);
    }

    [Fact]
    public void EnumerateXboxContainers_FallsBackToWgsWhenXgsHasNoSaveSlots()
    {
        WriteFile(Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "BBBB_2222", "AccountData"), "data");
        string wgsContainer = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "wgs", "BBBB_2222");
        WriteFile(wgsContainer, "containers.index");

        var containers = SaveFileManager.EnumerateXboxContainers(Path.Combine(_root, "Packages"));

        Assert.Single(containers);
        Assert.Equal(wgsContainer, containers[0]);
    }

    [Fact]
    public void EnumerateXboxContainers_FromXgsFolderDirectly_ReturnsContainerWithSlots()
    {
        string xgsRoot = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs");
        string container = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "CCCC_3333");
        WriteFile(Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "CCCC_3333", "Slot2Manual"), "data");

        var containers = SaveFileManager.EnumerateXboxContainers(xgsRoot);

        Assert.Single(containers);
        Assert.Equal(container, containers[0]);
    }

    [Fact]
    public void HasSaveSlots_TrueOnlyWhenSlotFolderWithDataExists()
    {
        string container = Dir("xgs", "FFFF_6666");
        WriteFile(Dir("xgs", "FFFF_6666", "AccountData"), "data");

        Assert.False(XgsSaveManager.HasSaveSlots(container));

        WriteFile(Dir("xgs", "FFFF_6666", "Slot3Auto"), "data");
        Assert.True(XgsSaveManager.HasSaveSlots(container));
    }

    [Fact]
    public void ResolveSaveDirectory_DrillsIntoParentFolders()
    {
        // Xbox: the package root, SystemAppData and the xgs folder all resolve to the
        // container that holds save slots.
        string package = Dir("Packages", "HelloGames.NoMansSky_test");
        string systemAppData = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData");
        string xgsRoot = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs");
        string container = Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "DDDD_4444");
        WriteFile(Dir("Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "DDDD_4444", "Slot1Auto"), "data");

        Assert.Equal(container, SaveFileManager.ResolveSaveDirectory(package));
        Assert.Equal(container, SaveFileManager.ResolveSaveDirectory(systemAppData));
        Assert.Equal(container, SaveFileManager.ResolveSaveDirectory(xgsRoot));

        // Steam/GOG: the NMS root resolves to the most recent profile.
        string nmsRoot = Dir("appdata", "HelloGames", "NMS");
        string profile = Dir("appdata", "HelloGames", "NMS", "st_999");
        WriteFile(profile, "save.hg");
        Assert.Equal(profile, SaveFileManager.ResolveSaveDirectory(nmsRoot));

        // An already-valid directory is returned unchanged.
        Assert.Equal(profile, SaveFileManager.ResolveSaveDirectory(profile));
    }

    [Fact]
    public void FindDefaultSaveDirectories_ReturnsSteamProfileAndXboxContainerTogether()
    {
        string appData = Dir("appdata");
        string localAppData = Dir("localappdata");
        string home = Dir("home");

        string profile = Dir("appdata", "HelloGames", "NMS", "st_555");
        WriteFile(profile, "save.hg");
        string container = Dir("localappdata", "Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "EEEE_5555");
        WriteFile(Dir("localappdata", "Packages", "HelloGames.NoMansSky_test", "SystemAppData", "xgs", "EEEE_5555", "Slot1Manual"), "data");

        var dirs = SaveFileManager.FindDefaultSaveDirectories(appData, localAppData, home);

        Assert.Equal(2, dirs.Count);
        Assert.Equal(profile, dirs[0]);   // Steam profile first
        Assert.Equal(container, dirs[1]); // Xbox container also offered
    }

    [Fact]
    public void FindDefaultSaveDirectories_NothingInstalled_ReturnsEmpty()
    {
        var dirs = SaveFileManager.FindDefaultSaveDirectories(Dir("appdata"), Dir("localappdata"), Dir("home"));

        Assert.Empty(dirs);
    }
}
