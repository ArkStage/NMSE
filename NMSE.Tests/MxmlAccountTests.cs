using NMSE.Core;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for the macOS account data path: locating GcUserSettingsData.mxml next to the
/// save profile and loading its reward lists as account data.
/// </summary>
public class MxmlAccountTests
{
    private static string CreateTempMxml(string root)
    {
        string settings = Path.Combine(root, "NMS", "SETTINGS");
        Directory.CreateDirectory(settings);
        string path = Path.Combine(settings, "GcUserSettingsData.mxml");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <Data template="GcUserSettingsData">
              <Property name="UnlockedSeasonRewards">
                <Property name="UnlockedSeasonRewards" value="SEASON_A" _index="0" />
              </Property>
              <Property name="UnlockedTwitchRewards">
                <Property name="UnlockedTwitchRewards" value="TWITCH_001" _index="0" />
              </Property>
              <Property name="UnlockedPlatformRewards">
                <Property name="UnlockedPlatformRewards" value="SW_PREORDER" _index="0" />
              </Property>
              <Property name="UnlockedSpecials">
                <Property name="UnlockedSpecials" value="SPEC_XOHELMET" _index="0" />
              </Property>
            </Data>
            """);
        return path;
    }

    [Fact]
    public void FindMxmlPath_FindsSaveDirectorySiblingSettings()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid()}");
        try
        {
            string mxml = CreateTempMxml(root);
            string profile = Path.Combine(root, "NMS", "st_12345");
            Directory.CreateDirectory(profile);

            Assert.Equal(mxml, MxmlRewardEditor.FindMxmlPath(profile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMxmlPath_MatchesFileNameCaseInsensitively()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid()}");
        try
        {
            string settings = Path.Combine(root, "NMS", "SETTINGS");
            Directory.CreateDirectory(settings);
            string path = Path.Combine(settings, "GCUSERSETTINGSDATA.MXML");
            File.WriteAllText(path, "<Data template=\"GcUserSettingsData\" />");
            string profile = Path.Combine(root, "NMS", "st_12345");
            Directory.CreateDirectory(profile);

            Assert.Equal(path, MxmlRewardEditor.FindMxmlPath(profile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadMxmlAccountData_ReadsAllRewardLists()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nmse_test_{Guid.NewGuid()}");
        try
        {
            string mxml = CreateTempMxml(root);

            var data = AccountLogic.LoadMxmlAccountData(mxml);

            Assert.Null(data.ErrorMessage);
            Assert.True(data.IsMxmlSource);
            Assert.Equal(mxml, data.AccountFilePath);
            Assert.Contains("^SEASON_A", data.SeasonUnlocked);
            Assert.Contains("^TWITCH_001", data.TwitchUnlocked);
            Assert.Contains("^SW_PREORDER", data.PlatformUnlocked);
            Assert.Contains("^SPEC_XOHELMET", data.SpecialsUnlocked);
            Assert.NotNull(data.AccountObject);
            Assert.NotNull(data.AccountObject!.GetObject("UserSettingsData"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
