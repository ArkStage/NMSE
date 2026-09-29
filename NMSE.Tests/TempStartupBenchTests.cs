using System.Diagnostics;
using NMSE.Data;
using Xunit;
using Xunit.Abstractions;

namespace NMSE.Tests;

public class TempStartupBenchTests
{
    private readonly ITestOutputHelper _output;

    public TempStartupBenchTests(ITestOutputHelper output) => _output = output;

    private static string? FindBaseDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "json");
            if (Directory.Exists(candidate)) return Path.GetDirectoryName(candidate);
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    [Fact]
    public void LanguageApplyBench()
    {
        string? baseDir = FindBaseDir();
        if (baseDir == null) return;
        string jsonDir = Path.Combine(baseDir, "json");
        string uiLangDir = Path.Combine(baseDir, "ui", "lang");

        var sw = Stopwatch.StartNew();

        UiStrings.SetDirectory(uiLangDir);
        UiStrings.Load("en-GB");
        _output.WriteLine($"UiStrings.Load(en-GB): {sw.ElapsedMilliseconds} ms, {UiStrings.TotalKeyCount} keys");
        sw.Restart();

        var db = new GameItemDatabase();
        db.LoadItemsFromJsonDirectory(jsonDir);
        _output.WriteLine($"ItemDatabase.Load: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        var loc = new LocalisationService();
        loc.SetLangDirectory(Path.Combine(jsonDir, "lang"));
        loc.LoadLanguage("en-GB");
        _output.WriteLine($"LocalisationService.LoadLanguage: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        db.ApplyLocalisation(loc);
        _output.WriteLine($"GameItemDatabase.ApplyLocalisation: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        RewardDatabase.LoadFromJsonDirectory(jsonDir);
        _output.WriteLine($"RewardDatabase.Load: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        RewardDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"RewardDatabase.ApplyLocalisation: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        var wordDb = new WordDatabase();
        wordDb.LoadFromFile(Path.Combine(jsonDir, "Words.json"));
        wordDb.ApplyLocalisation(loc);
        _output.WriteLine($"WordDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        var recipeDb = new RecipeDatabase();
        recipeDb.LoadFromFile(Path.Combine(jsonDir, "Recipes.json"));
        recipeDb.ApplyLocalisation(loc);
        _output.WriteLine($"RecipeDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        TitleDatabase.LoadFromFile(Path.Combine(jsonDir, "Titles.json"));
        TitleDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"TitleDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        FrigateTraitDatabase.LoadFromFile(Path.Combine(jsonDir, "Frigate Traits.json"));
        FrigateTraitDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"FrigateTraitDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        SettlementDatabase.LoadFromFile(Path.Combine(jsonDir, "Settlement Perks.json"));
        SettlementDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"SettlementDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        WikiGuideDatabase.LoadFromFile(Path.Combine(jsonDir, "Wiki Guide.json"));
        WikiGuideDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"WikiGuideDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        CompanionAccessoryDatabase.LoadFromFile(Path.Combine(jsonDir, "Companion Accessories.json"));
        CompanionAccessoryDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"CompanionAccessoryDatabase load+apply: {sw.ElapsedMilliseconds} ms");
        sw.Restart();

        SpacePoiTableDatabase.LoadFromFile(Path.Combine(jsonDir, "Space POI.json"));
        SpacePoiTableDatabase.ApplyLocalisation(loc);
        _output.WriteLine($"SpacePoiTableDatabase load+apply: {sw.ElapsedMilliseconds} ms");

        Assert.True(true, "bench complete");
    }
}
