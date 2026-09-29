using NMSE.Core;
using NMSE.Data;
using Xunit;

namespace NMSE.Tests;

/// <summary>
/// Tests for <see cref="ProcTechLogic"/> (seed mixing, weighting curves, stat rolls,
/// formatting and generated names), the embedded <see cref="ProcTechData"/> tables
/// (stat types, base stat amounts, best seeds) and the fixed technology stat bonuses.
/// </summary>
public class ProcTechLogicTests
{    private static string? FindGameLangDir()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "Resources", "json", "lang");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }

    private static GameItem SentinelFragment => new()
    {
        Id = "UP_SNSUIT",
        Name = "Exosuit",
        NameLocStr = "UP_SENTSUIT",
        Quality = "Sentinel",
        IsProcedural = true
    };

    [Fact]
    public void GenerateTechName_MatchesKnownInGameName()
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        string? name = ProcTechLogic.GenerateTechName(SentinelFragment, "95049", null);

        Assert.Equal("Writhing Energy Field", name);
    }

    [Fact]
    public void ComputeIndices_MatchesKnownSeed()
    {
        var indices = ProcTechLogic.ComputeIndices(95049);

        Assert.Equal(19, indices.Adj20);  // UP_TECH_SENT_ADJ_19 = Writhing
        Assert.Equal(1, indices.Noun20);  // UP_SENTSUIT_NOUN_1 = Energy Field
    }

    [Fact]
    public void GeneratePackName_AppendsPackageSuffix()
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        string? techName = ProcTechLogic.GenerateTechName(SentinelFragment, "95049", null);
        string? packName = ProcTechLogic.GeneratePackName(techName, SentinelFragment, null);

        Assert.Equal("Writhing Energy Field Package", packName);
    }

    [Fact]
    public void GenerateTechName_NormalQuality_UsesAdjectiveComponentFormat()
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        var tech = new GameItem
        {
            Id = "UP_HYPERDRIVE1",
            Name = "Hyperdrive Upgrade",
            NameLocStr = "UP_HYPERDRIVE",
            Quality = "Normal",
            IsProcedural = true
        };

        string? name = ProcTechLogic.GenerateTechName(tech, "12345", null);

        Assert.NotNull(name);
        Assert.NotEqual("UP_HYPERDRIVE", name);
        Assert.Contains(" ", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UP_SHLD3", "UP_SHIELDBOOST", "Epic", "77222", "Nuclear Padding")]
    [InlineData("UP_RAD1", "UP_RAD", "Rare", "65790", "Lead-Based DNA Repair System")]
    [InlineData("UP_HOT2", "UP_HOTPROT", "Epic", "35942", "Composite Heat Sink")]
    public void GenerateTechName_MatchesKnownNonSentinelNames(
        string id, string nameLocStr, string quality, string seed, string expected)
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        var tech = new GameItem
        {
            Id = id,
            NameLocStr = nameLocStr,
            Quality = quality,
            IsProcedural = true
        };

        Assert.Equal(expected, ProcTechLogic.GenerateTechName(tech, seed, null));
    }

    [Theory]
    [InlineData("UP_S_SHLX", "UP_SHIPSHIELD", "88176", "Pirate Bionic Padding")]
    [InlineData("UP_SGUNX", "UP_SHIPGUN", "21060", "Risky Cadmium De-limiter")]
    [InlineData("UP_JETX", "UP_JETBOOST", "76842", "'Irregular' Catalyst Diffuser")]
    public void GenerateTechName_MatchesKnownIllegalNames(
        string id, string nameLocStr, string seed, string expected)
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        var tech = new GameItem
        {
            Id = id,
            NameLocStr = nameLocStr,
            Quality = "Illegal",
            IsProcedural = true
        };

        Assert.Equal(expected, ProcTechLogic.GenerateTechName(tech, seed, null));
    }

    [Fact]
    public void LookupGameString_ResolvesStatDisplayNames()
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        Assert.Equal("Solar Panel Power", ProcTechLogic.LookupGameString(null, "SUIT_ENERGY_REGEN"));
        Assert.Equal("Shield Strength", ProcTechLogic.LookupGameString(null, "SUIT_ARMOUR_SHIELD_STRENGTH"));
    }

    [Fact]
    public void GenerateTechName_UnknownPrefix_ReturnsNull()
    {
        var langDir = FindGameLangDir();
        if (langDir == null) return;
        ProcTechLogic.SetLanguageDirectory(langDir);

        var tech = new GameItem
        {
            Id = "UP_UNKNOWN",
            Name = "Unknown",
            NameLocStr = "UP_NO_SUCH_WORDLIST",
            Quality = "Normal",
            IsProcedural = true
        };

        Assert.Null(ProcTechLogic.GenerateTechName(tech, "12345", null));
    }

    [Theory]
    [InlineData("Linear", 0.25f, 0.25f)]
    [InlineData("EaseInQuad", 0.5f, 0.25f)]
    [InlineData("EaseInQuart", 0.5f, 0.0625f)]
    [InlineData("EaseInExpo", 0.5f, 0.03125f)]
    [InlineData("EaseOutQuad", 0.5f, 0.75f)]
    [InlineData("EaseOutQuart", 0.5f, 0.9375f)]
    [InlineData("EaseOutExpo", 0.5f, 0.96875f)]
    public void CurveFunctions_MatchGameFormulas(string curve, float t, float expected)
    {
        Assert.Equal(expected, ProcTechLogic.Evaluate(t, curve), 5);
    }

    [Fact]
    public void WeightingCurves_MapToExpectedCurves()
    {
        Assert.Equal("Linear", ProcTechLogic.WeightingCurveToCurve("NoWeighting"));
        Assert.Equal("EaseInQuad", ProcTechLogic.WeightingCurveToCurve("MaxIsUncommon"));
        Assert.Equal("EaseInQuart", ProcTechLogic.WeightingCurveToCurve("MaxIsRare"));
        Assert.Equal("EaseInExpo", ProcTechLogic.WeightingCurveToCurve("MaxIsSuperRare"));
        Assert.Equal("EaseOutQuad", ProcTechLogic.WeightingCurveToCurve("MinIsUncommon"));
        Assert.Equal("EaseOutQuart", ProcTechLogic.WeightingCurveToCurve("MinIsRare"));
        Assert.Equal("EaseOutExpo", ProcTechLogic.WeightingCurveToCurve("MinIsSuperRare"));
    }

    private static GameItem CreateTemplate(int numStatsMin, int numStatsMax, string quality = "Epic")
    {
        var template = new GameItem
        {
            Id = "UP_TEST",
            Name = "Test Upgrade",
            Quality = quality,
            IsProcedural = true,
            NumStatsMin = numStatsMin,
            NumStatsMax = numStatsMax,
            WeightingCurve = "MaxIsRare"
        };
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Stat_A", Name = "Alpha", ValueMin = 1f, ValueMax = 2f,
            WeightingCurve = "MaxIsRare"
        });
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Stat_B", Name = "Beta", ValueMin = 1f, ValueMax = 2f,
            WeightingCurve = "MaxIsUncommon"
        });
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Stat_C", Name = "Gamma", ValueMin = 1f, ValueMax = 2f,
            WeightingCurve = "MinIsRare"
        });
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Stat_D", Name = "Delta", ValueMin = 1f, ValueMax = 2f,
            WeightingCurve = "NoWeighting", AlwaysChoose = true
        });
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Stat_E", Name = "Epsilon", ValueMin = 1f, ValueMax = 2f,
            WeightingCurve = "MaxIsSuperRare"
        });
        return template;
    }

    [Fact]
    public void Roll_IsDeterministic()
    {
        var template = CreateTemplate(2, 4);

        var first = ProcTechLogic.Roll(template, 12345);
        var second = ProcTechLogic.Roll(template, 12345);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Stat, second[i].Stat);
            Assert.Equal(first[i].Value, second[i].Value);
        }
    }

    [Fact]
    public void Roll_RespectsCountsAndAlwaysChoose()
    {
        var template = CreateTemplate(2, 4);

        for (uint seed = 0; seed < 200; seed++)
        {
            var rolls = ProcTechLogic.Roll(template, seed);

            // Always-choose entries are always present and come first.
            Assert.True(rolls.Count >= 1);
            Assert.Equal("Stat_D", rolls[0].Stat);

            // At most 4 stats and never more than available levels.
            Assert.InRange(rolls.Count, 1, 4);
            Assert.True(rolls.Count <= template.StatLevels.Count);
        }
    }

    [Fact]
    public void Roll_StatCountWithinConfiguredRange()
    {
        var template = CreateTemplate(3, 3);

        for (uint seed = 0; seed < 100; seed++)
        {
            var rolls = ProcTechLogic.Roll(template, seed);
            // 3 random stats plus the always-choose entry, capped at 4.
            Assert.Equal(4, rolls.Count);
        }
    }

    [Fact]
    public void Roll_LuckyMaximisesValues()
    {
        var template = CreateTemplate(2, 4);

        var rolls = ProcTechLogic.Roll(template, 999, lucky: true);

        Assert.Equal(4, rolls.Count);
        foreach (var roll in rolls)
        {
            // Max* curves roll to the maximum; the Min* curve is forced to its minimum.
            if (roll.Stat == "Stat_C")
                Assert.Equal(roll.Min, roll.Value, 5);
            else
                Assert.Equal(roll.Max, roll.Value, 5);
        }
    }

    [Fact]
    public void FormatValue_MatchesGameFormatting()
    {
        // Multiply stat: ceil((value - 1) * 100).
        Assert.Equal("+12%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Suit_Energy_Regen", "Suit Energy Regen", 6, 1.119838f, 1.01f, 1.75f)));

        // Add stat with unit base: truncated (int)(value * 100) -> 19.991 becomes +19%.
        Assert.Equal("+19%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Suit_Armour_Shield_Strength", "Shield", 6, 0.19991f, 0.1f, 0.35f),
            ProcTechData.GetBaseStatAmount("Suit_Armour_Shield_Strength", "Suit_Armour_Shield")));

        // Multiply drain stat: inverted sign, floored -> 0.9462 becomes +6%.
        Assert.Equal("+6%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Suit_Jetpack_Drain", "Drain", 6, 0.946248f, 0.7f, 0.95f)));

        // Add stat with unit base above 1: truncated value -> +107%.
        Assert.Equal("+107%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Suit_Energy", "Energy", 6, 1.074272f, 0.05f, 1.1f),
            ProcTechData.GetBaseStatAmount("Suit_Energy", "Suit_Energy"), baseKnown: true));

        // Add stat with a base stat amount: 20 of 60 hit points -> +33%.
        float healthBase = ProcTechData.GetBaseStatAmount("Suit_Armour_Health", "Suit_Armour_Shield");
        Assert.Equal(60f, healthBase);
        Assert.Equal("+33%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Suit_Armour_Health", "Health", 6, 20f, 20f, 20f),
            healthBase, baseKnown: true));

        // Add stat with an explicit base of 1 (scanner discovery): 86.576485 -> "+8,657%".
        Assert.True(ProcTechData.TryGetBaseStatAmount("Weapon_Scan_Discovery_Mineral", "Weapon_Scan", out float scanBase));
        Assert.Equal(1f, scanBase);
        Assert.Equal("+8,657%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Weapon_Scan_Discovery_Mineral", "Mineral", 6, 86.576485f, 65f, 100f),
            scanBase, baseKnown: true));

        // Ship Multiply stats: Ship_Boost 1.1054586 -> "+11%".
        Assert.Equal("+11%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Ship_Boost", "Boost", 4, 1.1054586f, 1.1f, 1.25f)));

        // Pulse drive fuel spending is inverted: 0.75 -> -25 -> "+25%".
        Assert.Equal("+25%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Ship_PulseDrive_MiniJumpFuelSpending", "Fuel", 4, 0.75f, 0.8f, 0.8f)));

        // Freighter fleet fuel is inverted too.
        Assert.Equal("+25%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Freighter_Fleet_Fuel", "Fuel", 4, 0.75f, 0.75f, 0.75f)));

        // Hyperdrive jump distance is shown in lightyears.
        Assert.Equal("+230 ly", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll("Ship_Hyperdrive_JumpDistance", "Range", 4, 230f, 100f, 300f),
            lightYearTemplate: "%DISTANCE% ly"));

        // Hidden stats are not shown in the game's stat section.
        Assert.False(ProcTechData.IsStatDisplayable("Ship_Maneuverability"));
        Assert.True(ProcTechData.IsStatDisplayable("Ship_Boost"));
    }

    [Fact]
    public void Roll_HighGradeChanceMatchesGame()
    {
        var jsonDir = FindJsonDir();
        if (jsonDir == null) return;
        var database = new GameItemDatabase();
        database.LoadItemsFromJsonDirectory(jsonDir);

        // UP_BOLTX#54371: chance roll 8 of 100 < 10 -> boosted, matching the in-game readout
        // (Damage +2%, Reload Time -8%, Burst Cooldown -19%, Fire Rate +18%).
        var boltx = database.GetItem("UP_BOLTX");
        if (boltx == null || boltx.StatLevels.Count == 0) return;
        var boltxRolls = ProcTechLogic.Roll(boltx, 54371);
        Assert.Equal(4, boltxRolls.Count);
        Assert.Equal(4.513852f, boltxRolls.First(r => r.Stat == "Weapon_Projectile_Damage").Value, 4);
        Assert.Equal(1.1724583f, boltxRolls.First(r => r.Stat == "Weapon_Projectile_Rate").Value, 4);

        // UP_SNSUIT#95049: chance roll 84 -> not boosted (2 stats, verified in game).
        var snsuit = database.GetItem("UP_SNSUIT");
        if (snsuit == null || snsuit.StatLevels.Count == 0) return;
        Assert.Equal(2, ProcTechLogic.Roll(snsuit, 95049).Count);

        // UP_SCAN4#03495: discovery stats use their base of 1 and display as percentages.
        var scan = database.GetItem("UP_SCAN4");
        if (scan == null || scan.StatLevels.Count == 0) return;
        var mineral = ProcTechLogic.Roll(scan, 3495).First(r => r.Stat == "Weapon_Scan_Discovery_Mineral");
        Assert.True(ProcTechData.TryGetBaseStatAmount(mineral.Stat, scan.BaseStat, out float mineralBase));
        Assert.Equal("+8,657%", ProcTechLogic.FormatValue(mineral, mineralBase, baseKnown: true));
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

    [Fact]
    public void FindBestSeed_ScoresAtLeastSampledSeeds()
    {
        var template = CreateTemplate(2, 4);

        var best = ProcTechLogic.FindBestSeed(template);

        Assert.InRange(best.Seed, 0u, 99999u);
        Assert.NotEmpty(best.Rolls);

        for (uint seed = 0; seed < 25; seed++)
        {
            var sample = ProcTechLogic.Roll(template, seed);
            float sampleScore = sample.Sum(r => r.Benefit);
            Assert.True(best.Score >= sampleScore - 0.0001f);
        }
    }

    [Fact]
    public void FindBestSeed_PrefersLowestRollForInvertedStats()
    {
        var template = new GameItem
        {
            Id = "UP_LOW",
            Name = "Test Upgrade",
            Quality = "Epic",
            IsProcedural = true,
            NumStatsMin = 1,
            NumStatsMax = 1,
            WeightingCurve = "NoWeighting"
        };
        template.StatLevels.Add(new GameItem.ProceduralStatLevel
        {
            Stat = "Suit_Jetpack_Drain", Name = "Drain", ValueMin = 0.9f, ValueMax = 0.95f,
            WeightingCurve = "MinIsUncommon", AlwaysChoose = true
        });

        var best = ProcTechLogic.FindBestSeed(template);

        Assert.Single(best.Rolls);
        Assert.True(best.Rolls[0].LowerIsBetter);
        Assert.True(best.Rolls[0].Benefit > 0.9f);
        Assert.True(best.Rolls[0].Normalised < 0.1f);
    }



    [Fact]
    public void BestSeeds_ArePopulated()
    {
        Assert.True(ProcTechData.TryGetBestSeed("UP_SNSUIT", out uint seed));
        Assert.InRange(seed, 0u, 99999u);
        Assert.True(ProcTechData.TryGetBestSeed("UP_BOLTX", out _));
        Assert.False(ProcTechData.TryGetBestSeed("NOT_A_TEMPLATE", out _));
    }

    [Fact]
    public void BestSeed_MatchesGeneratorRoll()
    {
        var jsonDir = FindJsonDir();
        if (jsonDir == null) return;

        var database = new GameItemDatabase();
        database.LoadItemsFromJsonDirectory(jsonDir);
        var template = database.GetItem("UP_SNSUIT");
        if (template == null || template.StatLevels.Count == 0) return;

        Assert.True(ProcTechData.TryGetBestSeed("UP_SNSUIT", out uint seed));
        var rolls = ProcTechLogic.Roll(template, seed);
        Assert.NotEmpty(rolls);

        // The stored seed is the generator's best for this template.
        var best = ProcTechLogic.FindBestSeed(template);
        Assert.Equal(best.Seed, seed);
    }



    [Fact]
    public void FixedBonuses_AreParsedFromDatabase()
    {
        var jsonDir = FindJsonDir();
        if (jsonDir == null) return;
        var database = new GameItemDatabase();
        database.LoadItemsFromJsonDirectory(jsonDir);

        var cold = database.GetItem("UT_COLD");
        Assert.NotNull(cold);
        Assert.Equal("Suit_Protection", cold.BaseStat);
        var coldBonus = Assert.Single(cold.StatBonuses);
        Assert.Equal("Suit_Protection_ColdDrain", coldBonus.Stat);
        Assert.Equal(1.2f, coldBonus.Bonus, 5);
        Assert.Equal(1, coldBonus.Level);

        var jet = database.GetItem("UT_JET");
        Assert.NotNull(jet);
        Assert.Contains(jet.StatBonuses, b => b.Stat == "Suit_Jetpack_Refill");
        Assert.Contains(jet.StatBonuses, b => b.Stat == "Suit_Jetpack_Tank");
    }

    [Fact]
    public void FixedBonuses_FormatLikeGame()
    {
        var jsonDir = FindJsonDir();
        if (jsonDir == null) return;
        var database = new GameItemDatabase();
        database.LoadItemsFromJsonDirectory(jsonDir);

        // UT_COLD: Cold Resistance (Suit_Protection_ColdDrain 1.2, Multiply) -> ceil(20.000004) = +21%.
        var cold = database.GetItem("UT_COLD");
        if (cold == null) return;
        var coldBonus = cold.StatBonuses.First(b => b.Stat == "Suit_Protection_ColdDrain");
        float coldBase = ProcTechData.GetBaseStatAmount(coldBonus.Stat, cold.BaseStat);
        Assert.Equal("+21%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll(coldBonus.Stat, coldBonus.Stat, coldBonus.Level,
                coldBonus.Bonus, coldBonus.Bonus, coldBonus.Bonus), coldBase));

        // UT_JET: Jetpack Refill 1.1 -> +11%, Tank 0.25 Add -> +25%.
        var jet = database.GetItem("UT_JET");
        if (jet == null) return;
        var refill = jet.StatBonuses.First(b => b.Stat == "Suit_Jetpack_Refill");
        Assert.Equal("+11%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll(refill.Stat, refill.Stat, refill.Level,
                refill.Bonus, refill.Bonus, refill.Bonus)));

        var tank = jet.StatBonuses.First(b => b.Stat == "Suit_Jetpack_Tank");
        float tankBase = ProcTechData.GetBaseStatAmount(tank.Stat, jet.BaseStat);
        Assert.Equal("+25%", ProcTechLogic.FormatValue(
            new ProcTechLogic.ProcStatRoll(tank.Stat, tank.Stat, tank.Level,
                tank.Bonus, tank.Bonus, tank.Bonus), tankBase));
    }

    // --- Stat-target seed search ---

    [Fact]
    public void Search_IsDeterministic()
    {
        var template = CreateTemplate(2, 4);
        var criteria = new List<ProcTechLogic.StatCriterion>
        {
            new("Stat_A", ProcTechLogic.StatMatchMode.Maximise),
            new("Stat_C", ProcTechLogic.StatMatchMode.Maximise),
        };

        var first = ProcTechLogic.Search(template, criteria);
        var second = ProcTechLogic.Search(template, criteria);

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Seed, second[i].Seed);
            Assert.Equal(first[i].Score, second[i].Score);
        }
    }

    [Fact]
    public void Search_Maximise_BeatsSampledSeeds()
    {
        var template = CreateTemplate(2, 4);
        var criteria = new List<ProcTechLogic.StatCriterion>
        {
            new("Stat_A", ProcTechLogic.StatMatchMode.Maximise),
        };

        var results = ProcTechLogic.Search(template, criteria);
        Assert.NotEmpty(results);
        float best = BenefitOf(results[0].Rolls, "Stat_A");

        // The search is exhaustive, so no sampled seed can beat it.
        for (uint seed = 0; seed < 500; seed++)
            Assert.True(BenefitOf(ProcTechLogic.Roll(template, seed), "Stat_A") <= best);
    }

    [Fact]
    public void Search_Target_FindsTheExactValue()
    {
        var template = CreateTemplate(2, 4);
        var reference = ProcTechLogic.Roll(template, 12345).First(r => r.Stat == "Stat_A");

        var criteria = new List<ProcTechLogic.StatCriterion>
        {
            new("Stat_A", ProcTechLogic.StatMatchMode.Target, reference.Value),
        };
        var results = ProcTechLogic.Search(template, criteria);

        Assert.NotEmpty(results);
        var top = results[0].Rolls.First(r => r.Stat == "Stat_A");

        // The reference seed itself is in the search space, so a perfect match must exist.
        Assert.Equal(reference.Value, top.Value);
    }

    [Fact]
    public void Search_Priority_PrefersTheChosenStat()
    {
        var template = CreateTemplate(1, 1);
        var criteria = new List<ProcTechLogic.StatCriterion>
        {
            new("Stat_A", ProcTechLogic.StatMatchMode.Maximise, 0f, Priority: true),
            new("Stat_B", ProcTechLogic.StatMatchMode.Maximise),
        };

        var results = ProcTechLogic.Search(template, criteria);

        Assert.NotEmpty(results);
        Assert.Contains(results[0].Rolls, r => r.Stat == "Stat_A");
        Assert.True(BenefitOf(results[0].Rolls, "Stat_A") >= 0.5f);
    }

    [Fact]
    public void Search_AllMaximise_BeatsOrMatchesThePrecomputedBestSeed()
    {
        var jsonDir = FindJsonDir();
        if (jsonDir == null) return;
        var database = new GameItemDatabase();
        database.LoadItemsFromJsonDirectory(jsonDir);

        var template = database.GetItem("UP_SNSUIT");
        if (template == null || template.StatLevels.Count == 0) return;
        if (!ProcTechData.TryGetBestSeed(template.Id, out uint precomputed)) return;

        var criteria = new List<ProcTechLogic.StatCriterion>();
        foreach (var level in template.StatLevels)
            criteria.Add(new ProcTechLogic.StatCriterion(level.Stat, ProcTechLogic.StatMatchMode.Maximise));

        var results = ProcTechLogic.Search(template, criteria);
        Assert.NotEmpty(results);

        // The exhaustive search must be at least as good as the precomputed best seed,
        // comparing the sum of benefits over the same criteria.
        float precomputedSum = SumBenefits(ProcTechLogic.Roll(template, precomputed), criteria);
        float topSum = SumBenefits(results[0].Rolls, criteria);
        Assert.True(topSum >= precomputedSum - 1e-5f);
    }

    private static float BenefitOf(IReadOnlyList<ProcTechLogic.ProcStatRoll> rolls, string stat)
    {
        foreach (var roll in rolls)
            if (string.Equals(roll.Stat, stat, StringComparison.OrdinalIgnoreCase))
                return roll.Benefit;
        return 0f;
    }

    private static float SumBenefits(IReadOnlyList<ProcTechLogic.ProcStatRoll> rolls,
        IReadOnlyList<ProcTechLogic.StatCriterion> criteria)
    {
        float sum = 0f;
        foreach (var criterion in criteria)
            sum += BenefitOf(rolls, criterion.Stat);
        return sum;
    }

}
