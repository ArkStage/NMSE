using NMSE.Core;
using NMSE.Data;
using NMSE.IO;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Collection that runs isolated from the parallel test classes because it resets the
/// global <see cref="UiStrings"/> tables to verify the English label fallbacks.
/// </summary>
[CollectionDefinition("UiStringsSerial", DisableParallelization = true)]
public sealed class UiStringsSerialCollection
{
}

/// <summary>
/// Tests for <see cref="ControlTokens"/>: control token resolution per input scheme,
/// unmapped token pass-through, and the save-platform to scheme mapping. Includes a data
/// guard that fails when the shipped item data gains a token the resolver does not know.
/// </summary>
[Collection("UiStringsSerial")]
public class ControlTokensTests
{
    private static readonly HashSet<string> MappedTokens = new(StringComparer.Ordinal)
    {
        "FE_ALT1", "FE_SELECT", "FE_BACK", "FE_DESTROY", "FE_TRANSFER", "FE_EXITMENU",
        "FE_OPTIONS", "FE_QUIT", "FE_MSGSKIP", "FE_UPLOAD_DISCOVERY",
        "FE_ASSIGN_CUSTOM_WONDER", "FE_TOUCHSCREENPRESS"
    };

    private static readonly HashSet<string> AllowListedTokens = new(StringComparer.Ordinal)
    {
        "FE_ALTSELECT", "FE_CURSOR", "FE_RETOUR"
    };

    private static readonly HashSet<string> AllKnownTokens = new(MappedTokens, StringComparer.Ordinal);

    [Fact]
    public void Resolve_KeyboardMouse_MapsKnownTokens()
    {
        UiStrings.Reset();
        ControlTokens.SetScheme(ControlScheme.KeyboardMouse);
        try
        {
            Assert.Equal("Use [E] to begin.", ControlTokens.Resolve("Use FE_ALT1 to begin."));
            Assert.Equal("Use [LMB] to select.", ControlTokens.Resolve("Use FE_SELECT to select."));
            Assert.Equal("[RMB]", ControlTokens.Resolve("FE_BACK"));
            Assert.Equal("[MMB]", ControlTokens.Resolve("FE_DESTROY"));
            Assert.Equal("[X]", ControlTokens.Resolve("FE_TRANSFER"));
            Assert.Equal("[Esc]", ControlTokens.Resolve("FE_OPTIONS"));
            Assert.Equal("[Tab]", ControlTokens.Resolve("FE_MSGSKIP"));
            Assert.Equal("[F]", ControlTokens.Resolve("FE_UPLOAD_DISCOVERY"));
            Assert.Equal("[Z]", ControlTokens.Resolve("FE_ASSIGN_CUSTOM_WONDER"));
        }
        finally
        {
            ControlTokens.SetScheme(ControlScheme.KeyboardMouse);
        }
    }

    [Fact]
    public void Resolve_GamepadSchemes_MapKnownTokens()
    {
        try
        {
            ControlTokens.SetScheme(ControlScheme.Xbox);
            Assert.Equal("[X]", ControlTokens.Resolve("FE_ALT1"));
            Assert.Equal("[A]", ControlTokens.Resolve("FE_SELECT"));
            Assert.Equal("[Y]", ControlTokens.Resolve("FE_TRANSFER"));
            Assert.Equal("[View]", ControlTokens.Resolve("FE_MSGSKIP"));
            Assert.Equal("[Menu]", ControlTokens.Resolve("FE_OPTIONS"));

            ControlTokens.SetScheme(ControlScheme.PlayStation);
            Assert.Equal("[Square]", ControlTokens.Resolve("FE_ALT1"));
            Assert.Equal("[Cross]", ControlTokens.Resolve("FE_SELECT"));
            Assert.Equal("[Triangle]", ControlTokens.Resolve("FE_TRANSFER"));
            Assert.Equal("[Touchpad]", ControlTokens.Resolve("FE_MSGSKIP"));
            Assert.Equal("[Options]", ControlTokens.Resolve("FE_OPTIONS"));

            ControlTokens.SetScheme(ControlScheme.Switch);
            Assert.Equal("[Y]", ControlTokens.Resolve("FE_ALT1"));
            Assert.Equal("[A]", ControlTokens.Resolve("FE_SELECT"));
            Assert.Equal("[X]", ControlTokens.Resolve("FE_TRANSFER"));
            Assert.Equal("[Minus]", ControlTokens.Resolve("FE_MSGSKIP"));
            Assert.Equal("[Plus]", ControlTokens.Resolve("FE_OPTIONS"));
        }
        finally
        {
            ControlTokens.SetScheme(ControlScheme.KeyboardMouse);
        }
    }

    [Fact]
    public void Resolve_UnmappedTokens_AreUnchanged()
    {
        try
        {
            foreach (var scheme in new[]
                     { ControlScheme.KeyboardMouse, ControlScheme.Xbox, ControlScheme.PlayStation, ControlScheme.Switch })
            {
                ControlTokens.SetScheme(scheme);
                Assert.Equal("Select with FE_ALTSELECT", ControlTokens.Resolve("Select with FE_ALTSELECT"));
                Assert.Equal("FE_CURSOR", ControlTokens.Resolve("FE_CURSOR"));
                Assert.Equal("FE_RETOUR", ControlTokens.Resolve("FE_RETOUR"));
            }
        }
        finally
        {
            ControlTokens.SetScheme(ControlScheme.KeyboardMouse);
        }
    }

    [Fact]
    public void Resolve_LeavesOtherTextUntouched()
    {
        ControlTokens.SetScheme(ControlScheme.KeyboardMouse);

        Assert.Equal("Use SCAN to activate.", ControlTokens.Resolve("Use SCAN to activate."));
        Assert.Equal("fe_alt1", ControlTokens.Resolve("fe_alt1"));
        Assert.Equal("REFE_ALT1X", ControlTokens.Resolve("REFE_ALT1X"));
        Assert.Equal("FE_ALT1X", ControlTokens.Resolve("FE_ALT1X"));
    }

    [Fact]
    public void Resolve_MarkupWrappedToken_ReplacesTokenOnly()
    {
        UiStrings.Reset();
        ControlTokens.SetScheme(ControlScheme.KeyboardMouse);

        Assert.Equal("<IMG>[E]<>", ControlTokens.Resolve("<IMG>FE_ALT1<>"));
        Assert.Equal("Press <IMG>[E]</IMG> to report.", ControlTokens.Resolve("Press <IMG>FE_ALT1</IMG> to report."));
    }

    [Fact]
    public void Resolve_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", ControlTokens.Resolve(null));
        Assert.Equal("", ControlTokens.Resolve(""));
    }

    [Fact]
    public void SchemeForPlatform_MapsAllPlatforms()
    {
        Assert.Equal(ControlScheme.KeyboardMouse, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.Steam));
        Assert.Equal(ControlScheme.KeyboardMouse, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.GOG));
        Assert.Equal(ControlScheme.KeyboardMouse, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.Unknown));
        Assert.Equal(ControlScheme.Xbox, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.XboxGamePass));
        Assert.Equal(ControlScheme.PlayStation, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.PS4));
        Assert.Equal(ControlScheme.Switch, ControlTokens.SchemeForPlatform(SaveFileManager.Platform.Switch));
    }

    [Fact]
    public void ItemData_ContainsOnlyKnownControlTokens()
    {
        string? jsonDir = FindJsonDir();
        if (jsonDir == null) return;

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        string langSegment = $"{Path.DirectorySeparatorChar}lang{Path.DirectorySeparatorChar}";
        foreach (string file in Directory.EnumerateFiles(jsonDir, "*.json", SearchOption.AllDirectories))
        {
            if (file.Contains(langSegment, StringComparison.OrdinalIgnoreCase))
                continue;

            string text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, @"\bFE_[A-Z0-9_]+\b"))
            {
                if (!AllKnownTokens.Contains(match.Value) && !AllowListedTokens.Contains(match.Value))
                    unknown.Add(match.Value);
            }
        }

        Assert.True(unknown.Count == 0,
            $"Unknown FE_ tokens in item data (add them to ControlTokens or the allow-list): {string.Join(", ", unknown)}");
    }

    private static string? FindJsonDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "json");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
