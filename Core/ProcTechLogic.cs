using System.Globalization;
using System.Text.Json;
using NMSE.Core.Utilities;
using NMSE.Data;

namespace NMSE.Core;

/// <summary>
/// Procedural technology generation: seed mixing, weighting curves, stat rolls and the
/// generated display names. The save never stores these values; the game regenerates them
/// from the instance seed, so results are deterministic for a given template and seed.
/// </summary>
internal static class ProcTechLogic
{
    // Packed algorithm constants (see the private notes for the layout and regeneration recipe).
    private static readonly byte[] Mask =
    [
        0x2B, 0x77, 0xC4, 0x19, 0xE3, 0x5A, 0x8F, 0x40, 0x6D, 0xB1, 0x27, 0x9C, 0x54, 0xE0, 0x3A, 0x86,
    ];

    private static readonly byte[] Packed =
    [
        0xFC, 0x46, 0x79, 0x35, 0xAB, 0xDB, 0x52, 0x24, 0xFA, 0x98, 0x46, 0x8F, 0x92, 0x45, 0x50, 0x65,
        0x21, 0x77, 0xC4, 0x19, 0x79, 0xC3, 0x96, 0x7E, 0xF7, 0x28, 0x7E, 0xA3, 0x7D, 0xBC, 0x35, 0xB8,
        0x21, 0xA0, 0xE7, 0x25,
    ];

    private static readonly byte[] Raw = Unmask(Packed);

    private static readonly ulong NameMixA = BitConverter.ToUInt64(Raw, 0);
    private static readonly ulong NameMixB = BitConverter.ToUInt64(Raw, 8);
    private static readonly int ChancePercent = BitConverter.ToInt32(Raw, 16);
    private static readonly float BoostScaleLow = BitConverter.ToSingle(Raw, 20);
    private static readonly float BoostOffsetLow = BitConverter.ToSingle(Raw, 24);
    private static readonly float BoostScaleHigh = BitConverter.ToSingle(Raw, 28);
    private static readonly float BoostOffsetHigh = BitConverter.ToSingle(Raw, 32);

    private static byte[] Unmask(byte[] data)
    {
        var raw = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            raw[i] = (byte)(data[i] ^ Mask[i & 15]);
        return raw;
    }
    // --- Curves -------------------------------------------------------------

    /// <summary>Maps a weighting curve name (as stored in the item data) to its curve.</summary>
    internal static string WeightingCurveToCurve(string? weightingCurve) => weightingCurve switch
    {
        "NoWeighting" => "Linear",
        "MaxIsUncommon" => "EaseInQuad",
        "MaxIsRare" => "EaseInQuart",
        "MaxIsSuperRare" => "EaseInExpo",
        "MinIsUncommon" => "EaseOutQuad",
        "MinIsRare" => "EaseOutQuart",
        "MinIsSuperRare" => "EaseOutExpo",
        _ => weightingCurve ?? "Linear"
    };

    /// <summary>Evaluates a named curve at t (expected in 0..1).</summary>
    internal static float Evaluate(float t, string? curve)
    {
        switch (curve)
        {
            case null:
            case "":
            case "Linear":
                return t;

            case "EaseInQuad":
            case "Squared":
                return t * t;

            case "EaseInQuart":
                return t * t * t * t;

            case "EaseInExpo":
                return t == 0f ? 0f : MathF.Pow(2f, (t - 1f) * 10f);

            case "EaseOutQuad":
                return -(t * (t - 2f));

            case "EaseOutQuart":
            {
                float f = t - 1f;
                return f * f * f * (1f - t) + 1f;
            }

            case "EaseOutExpo":
                return t == 1f ? 1f : 1f - MathF.Pow(2f, t * -10f);

            // Remaining curve variants (not used by weighting curves) use their
            // standard easing formulas.
            case "SmoothInOut":
            {
                float s = (3f - 2f * t) * t * t;
                return (3f - 2f * s) * s * s;
            }
            case "FastInSlowOut":
                return 1f - (1f - t) * (1f - t);
            case "BellSquared":
            {
                float d = 2f * t - 1f;
                return 1f - (d * d + 1f) * 0.5f;
            }
            case "Cubed":
                return t * t * t;
            case "Logarithmic":
            {
                float v = MathF.Log(t) + 1f;
                return v < 0f ? 0f : v;
            }
            case "SlowIn":
                return MathF.Sin(t * 1.5707964f);
            case "SlowOut":
                return 1f - MathF.Cos(t * 1.5707964f);
            case "ReallySlowOut":
                return 1f - MathF.Cos(t * t * 1.5707964f);
            case "SmootherStep":
                return ((t * 6f - 15f) * t + 10f) * t * t * t;
            case "SmoothFastInSlowOut":
            {
                float f = 1f - t;
                return 1f - (2.5f - f * f * 1.5f) * f * f * f;
            }
            case "SmoothSlowInFastOut":
                return (2.5f - t * t * 1.5f) * t * t * t;
            case "EaseInSine":
                return MathF.Sin((t - 1f) * 1.5707964f) + 1f;
            case "EaseOutSine":
                return MathF.Sin(t * 1.5707964f);
            case "EaseInOutSine":
                return (1f - MathF.Cos(t * 3.1415927f)) * 0.5f;
            case "EaseInOutQuad":
                return t < 0.5f ? 2f * t * t : t * 4f - 2f * t * t - 1f;
            case "EaseInOutQuart":
            {
                if (t < 0.5f)
                    return t * t * 8f * t * t;
                float f = t - 1f;
                return 1f - f * 8f * f * f * f;
            }
            case "EaseInQuint":
                return t * t * t * t * t;
            case "EaseOutQuint":
            {
                float f = t - 1f;
                return f * f * f * f * f + 1f;
            }
            case "EaseInOutQuint":
            {
                if (t < 0.5f)
                    return 16f * t * t * t * t * t;
                float f = 2f * t - 2f;
                return 0.5f * f * f * f * f * f + 1f;
            }
            case "EaseInExpoFull":
                return t == 0f ? 0f : MathF.Pow(2f, (t - 1f) * 10f);
            case "EaseInOutExpo":
            {
                if (t == 0f || t == 1f) return t;
                if (t < 0.5f)
                    return MathF.Pow(2f, t * 20f - 10f) * 0.5f;
                return (2f - MathF.Pow(2f, 10f - t * 20f)) * 0.5f;
            }
            case "EaseInCirc":
                return 1f - MathF.Sqrt(MathF.Max(0f, 1f - t * t));
            case "EaseOutCirc":
                return MathF.Sqrt(MathF.Max(0f, (2f - t) * t));
            case "EaseInOutCirc":
            {
                if (t < 0.5f)
                    return (1f - MathF.Sqrt(MathF.Max(0f, 1f - 4f * t * t))) * 0.5f;
                return (MathF.Sqrt(MathF.Max(0f, 1f - MathF.Pow(-2f * t + 2f, 2f))) + 1f) * 0.5f;
            }
            default:
                return t;
        }
    }

    // --- Stat rolls ---------------------------------------------------------

    /// <summary>A rolled stat bonus.</summary>
    internal sealed record ProcStatRoll(string Stat, string Name, int Level, float Value, float Min, float Max,
        bool LowerIsBetter = false)
    {
        /// <summary>Roll position within [Min, Max] as 0..1 (1 = the maximum roll).</summary>
        public float Normalised => Max > Min ? (Value - Min) / (Max - Min) : 1f;

        /// <summary>
        /// God-roll benefit for this stat as 0..1 (1 = best outcome). Inverted stats and
        /// Min* weighting curves reward the lowest roll, so their benefit is inverted.
        /// Flat stats (Min == Max) cannot be improved and always score 1.
        /// </summary>
        public float Benefit => Max > Min ? (LowerIsBetter ? 1f - Normalised : Normalised) : 1f;
    }

    /// <summary>Maps a quality name to the value used by the roll.</summary>
    internal static int QualityIndex(string? quality) => quality switch
    {
        "Rare" => 1,
        "Epic" => 2,
        "Legendary" => 3,
        "Illegal" => 4,
        "Sentinel" => 5,
        _ => 0
    };

    /// <summary>Maps a weighting curve name to its roll index.</summary>
    internal static int WeightingIndex(string? weightingCurve) => weightingCurve switch
    {
        "MaxIsUncommon" => 1,
        "MaxIsRare" => 2,
        "MaxIsSuperRare" => 3,
        "MinIsUncommon" => 4,
        "MinIsRare" => 5,
        "MinIsSuperRare" => 6,
        _ => 0
    };

    /// <summary>
    /// Rolls the stat bonuses for a procedural technology instance.
    /// </summary>
    /// <param name="template">The procedural technology template (with StatLevels).</param>
    /// <param name="seed">The technology seed (0..99999).</param>
    /// <param name="highGrade">
    /// True to force the high-grade illegal boost (X-class); otherwise the seed's chance
    /// roll decides, as in the game.
    /// </param>
    /// <param name="lucky">True to force the player-luck maximum roll.</param>
    internal static IReadOnlyList<ProcStatRoll> Roll(GameItem? template, uint seed,
        bool highGrade = false, bool lucky = false)
    {
        if (template == null || template.StatLevels.Count == 0)
            return Array.Empty<ProcStatRoll>();

        int quality = QualityIndex(template.Quality);
        ulong state = PRNG.Next(PRNG.InitialState(seed));

        // Quality 4/5 (Illegal/Sentinel) roll the high-grade illegal chance, consuming
        // one PRNG step before the stat count roll.
        bool boosted = false;
        if (quality >= 4)
        {
            state = PRNG.Next(state);
            int chanceRoll = (int)(((ulong)(uint)state * 100UL) >> 32);
            boosted = highGrade || chanceRoll < ChancePercent;
        }

        // Number of stats to roll, weighted by the template's curve.
        state = PRNG.Next(state);
        float countRoll = Evaluate(PRNG.Fraction(state), WeightingCurveToCurve(template.WeightingCurve));
        if (lucky || boosted)
            countRoll = 1f;
        int statCount = RoundHalfAway((template.NumStatsMax - template.NumStatsMin) * countRoll + template.NumStatsMin);

        var always = new List<GameItem.ProceduralStatLevel>();
        var pool = new List<GameItem.ProceduralStatLevel>();
        foreach (var level in template.StatLevels)
            (level.AlwaysChoose ? always : pool).Add(level);

        // Fisher-Yates shuffle of the random pool (one PRNG step per iteration).
        for (int i = pool.Count - 1; i >= 1; i--)
        {
            state = PRNG.Next(state);
            int swapIndex = (int)(((ulong)(i + 1) * (uint)state) >> 32);
            (pool[i], pool[swapIndex]) = (pool[swapIndex], pool[i]);
        }

        int total = always.Count + statCount;
        if (total > template.StatLevels.Count) total = template.StatLevels.Count;
        if (total > 4) total = 4;

        var rolls = new List<ProcStatRoll>(total);
        foreach (var level in always)
        {
            if (rolls.Count >= total) break;
            rolls.Add(RollValue(level, ref state, quality, boosted, lucky));
        }
        foreach (var level in pool)
        {
            if (rolls.Count >= total) break;
            rolls.Add(RollValue(level, ref state, quality, boosted, lucky));
        }
        return rolls;
    }

    /// <summary>
    /// Searches all five-digit seeds for the "god roll": the highest total of per-stat
    /// benefits, tie-broken by the highest lowest benefit. Inverted stats (drain, fuel
    /// spending, cool time) and Min* weighting curves reward the lowest roll.
    /// </summary>
    internal static (uint Seed, IReadOnlyList<ProcStatRoll> Rolls, float Score) FindBestSeed(
        GameItem? template, bool highGrade = false)
    {
        uint bestSeed = 0;
        float bestScore = -1f;
        float bestMinimum = -1f;
        IReadOnlyList<ProcStatRoll> bestRolls = Array.Empty<ProcStatRoll>();

        for (uint seed = 0; seed < 100000; seed++)
        {
            var rolls = Roll(template, seed, highGrade);
            if (rolls.Count == 0)
                return (0, rolls, 0f);

            float score = 0f;
            float minimum = 1f;
            foreach (var roll in rolls)
            {
                float benefit = roll.Benefit;
                score += benefit;
                if (benefit < minimum)
                    minimum = benefit;
            }

            if (score > bestScore || (score == bestScore && minimum > bestMinimum))
            {
                bestScore = score;
                bestMinimum = minimum;
                bestSeed = seed;
                bestRolls = rolls;
            }
        }
        return (bestSeed, bestRolls, bestScore);
    }

    // --- Seed search by desired stats ---------------------------------------

    /// <summary>How a stat should be matched when searching procedural seeds.</summary>
    internal enum StatMatchMode
    {
        /// <summary>Ignore the stat.</summary>
        Ignore,

        /// <summary>Prefer the highest benefit (inversion-aware).</summary>
        Maximise,

        /// <summary>Prefer a specific value.</summary>
        Target,
    }

    /// <summary>A per-stat search criterion.</summary>
    internal sealed record StatCriterion(string Stat, StatMatchMode Mode, float Target = 0f, bool Priority = false);

    /// <summary>A ranked procedural seed search result.</summary>
    internal sealed record SeedCandidate(uint Seed, IReadOnlyList<ProcStatRoll> Rolls, float Score);

    /// <summary>
    /// Searches every five-digit procedural seed (0..99999) for the best match to the given
    /// criteria. The search is exhaustive and deterministic: the same template and criteria
    /// always produce the same ranked list (score descending, then lowest seed). A selected stat
    /// that does not roll in a seed scores as the worst outcome for that stat.
    /// </summary>
    internal static IReadOnlyList<SeedCandidate> Search(
        GameItem? template, IReadOnlyList<StatCriterion> criteria, int maxResults = 20, bool highGrade = false,
        IProgress<int>? progress = null)
    {
        if (template == null || template.StatLevels.Count == 0 || criteria.Count == 0)
            return Array.Empty<SeedCandidate>();

        var active = new List<StatCriterion>();
        foreach (var criterion in criteria)
            if (criterion.Mode != StatMatchMode.Ignore)
                active.Add(criterion);
        if (active.Count == 0)
            return Array.Empty<SeedCandidate>();

        var best = new List<SeedCandidate>(maxResults + 1);
        for (uint seed = 0; seed < 100000; seed++)
        {
            if ((seed & 0x3FFF) == 0)
                progress?.Report((int)seed);

            var rolls = Roll(template, seed, highGrade);
            if (rolls.Count == 0)
                continue;

            float score = ScoreSeed(rolls, active);
            int index = best.Count;
            for (int i = 0; i < best.Count; i++)
            {
                if (score > best[i].Score)
                {
                    index = i;
                    break;
                }
            }
            if (index >= maxResults)
                continue;

            best.Insert(index, new SeedCandidate(seed, rolls, score));
            if (best.Count > maxResults)
                best.RemoveAt(best.Count - 1);
        }
        return best;
    }

    /// <summary>Scores one seed's rolls against the criteria (weighted average, 0..1).</summary>
    private static float ScoreSeed(IReadOnlyList<ProcStatRoll> rolls, IReadOnlyList<StatCriterion> criteria)
    {
        float weighted = 0f;
        float totalWeight = 0f;
        foreach (var criterion in criteria)
        {
            float weight = criterion.Priority ? 4f : 1f;
            totalWeight += weight;

            ProcStatRoll? match = null;
            foreach (var roll in rolls)
            {
                if (string.Equals(roll.Stat, criterion.Stat, StringComparison.OrdinalIgnoreCase))
                {
                    match = roll;
                    break;
                }
            }

            float component;
            if (match == null)
            {
                component = 0f;
            }
            else if (criterion.Mode == StatMatchMode.Target)
            {
                float span = match.Max - match.Min;
                float distance = MathF.Abs(match.Value - criterion.Target);
                component = span > 0f ? 1f - Math.Clamp(distance / span, 0f, 1f) : (distance <= 0f ? 1f : 0f);
            }
            else
            {
                component = match.Benefit;
            }
            weighted += weight * component;
        }
        return totalWeight > 0f ? weighted / totalWeight : 0f;
    }

    /// <summary>Rolls one stat value from the current PRNG state.</summary>
    private static ProcStatRoll RollValue(GameItem.ProceduralStatLevel level, ref ulong state,
        int quality, bool boosted, bool lucky)
    {
        state = PRNG.Next(state);
        int weighting = WeightingIndex(level.WeightingCurve);
        float value;
        if (lucky)
        {
            // Forced best: maximum value, except Min* curves which want the minimum.
            value = weighting > 3 ? 0f : 1f;
        }
        else if (boosted)
        {
            // High-grade illegal: a fresh roll mapped into a boosted range.
            state = PRNG.Next(state);
            float t = PRNG.Fraction(state);
            value = weighting < 5 ? t * BoostScaleLow + BoostOffsetLow : t * BoostScaleHigh + BoostOffsetHigh;
        }
        else
        {
            value = Evaluate(PRNG.Fraction(state), WeightingCurveToCurve(level.WeightingCurve));
        }

        float bonus = level.ValueMin + (level.ValueMax - level.ValueMin) * value;
        bool lowerIsBetter = IsInvertedStat(level.Stat) || weighting >= 4;
        return new ProcStatRoll(level.Stat, level.Name, quality + 1, bonus, level.ValueMin, level.ValueMax,
            lowerIsBetter);
    }

    /// <summary>
    /// Formats a rolled stat value the way the game displays it, using the
    /// stat's display function from the game's stat type lookup table:
    /// <list type="bullet">
    /// <item>Hyperdrive jump distance is shown as a whole lightyear distance, e.g.
    /// "+230 ly" (the number is inserted into a localised unit template).</item>
    /// <item>Multiply: signed percentage of (value - 1), ceiled for positives and floored
    /// for negatives, with drain/fuel stats negated. E.g. Suit_Energy_Regen 1.6985 ->
    /// "+70%", Ship_Boost 1.11 -> "+11%", Ship_PulseDrive_MiniJumpFuelSpending 0.8 ->
    /// "+20%".</item>
    /// <item>Add: truncated (int)(value / base * 100) percent relative to the base stat
    /// amount (e.g. Core Health 20 of 60 hit points -> "+33%").</item>
    /// <item>Set: whole numbers.</item>
    /// </list>
    /// </summary>
    /// <param name="roll">The rolled stat.</param>
    /// <param name="baseAmount">Base stat amount for Add stats.</param>
    /// <param name="baseKnown">
    /// True when the base amount came from the game data. Add stats with a known base
    /// (including a base of 1) always display as a percentage; unknown bases only do so
    /// for unit-range stats.
    /// </param>
    /// <param name="lightYearTemplate">
    /// Localised lightyear unit containing "%DISTANCE%".
    /// </param>
    internal static string FormatValue(ProcStatRoll roll, float baseAmount = 1f,
        bool baseKnown = false, string? lightYearTemplate = null)
    {
        if (IsLightYearStat(roll.Stat))
            return FormatLightYears(roll.Value, lightYearTemplate);

        string? function = ProcTechData.GetStatFunction(roll.Stat);
        if (string.Equals(function, "Multiply", StringComparison.OrdinalIgnoreCase))
            return FormatMultiply(roll);
        if (string.Equals(function, "Add", StringComparison.OrdinalIgnoreCase))
            return FormatAdd(roll, baseAmount, baseKnown);
        if (string.Equals(function, "Set", StringComparison.OrdinalIgnoreCase))
            return FormatInteger((int)MathF.Round(roll.Value));

        // Fallback range heuristic when the stat is not in the table.
        if (roll.Min >= 0.99f && roll.Min <= 1.05f && roll.Max > 1f && roll.Max <= 3f)
            return FormatMultiply(roll);
        if (roll.Min >= 0f && roll.Max <= 1f)
            return FormatInteger((int)(roll.Value * 100f)) + "%";
        return FormatInteger((int)MathF.Round(roll.Value));
    }

    /// <summary>Formats an integer with the game's thousands separators and sign.</summary>
    private static string FormatInteger(int value) =>
        value.ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture);

    /// <summary>Formats a Multiply stat: the game ceils positives and floors negatives.</summary>
    private static string FormatMultiply(ProcStatRoll roll)
    {
        float pct = (roll.Value - 1f) * 100f;
        int shown;
        if (pct > 0f)
            shown = (int)MathF.Ceiling(pct < 1f ? 1f : pct);
        else if (pct < 0f)
            shown = (int)MathF.Floor(pct > -1f ? -1f : pct);
        else
            shown = 0;

        // Drain-style stats invert the sign (a lower drain multiplier is a bonus).
        if (IsInvertedStat(roll.Stat))
            shown = -shown;

        return FormatInteger(shown) + "%";
    }

    /// <summary>
    /// Formats an Add stat: the game displays (int)(value / base * 100) percent, where the
    /// base is the player's base stat amount. When no base is known and
    /// the stat has a large range, the flat bonus is shown instead.
    /// </summary>
    private static string FormatAdd(ProcStatRoll roll, float baseAmount, bool baseKnown)
    {
        float baseValue = baseAmount > 0f ? baseAmount : 1f;
        if (baseKnown || roll.Max <= 1.25f)
        {
            int pct = (int)(roll.Value / baseValue * 100f);
            return FormatInteger(pct) + "%";
        }
        return FormatInteger((int)MathF.Round(roll.Value));
    }

    /// <summary>
    /// Stats whose displayed sign is inverted:
    /// mining speed, projectile dispersion, vehicle heat/rate, jetpack drain, ship weapon
    /// cooldown/dispersion, pulse drive fuel spending and freighter fleet fuel (for all of
    /// these a lower multiplier is an improvement).
    /// </summary>
    private static bool IsInvertedStat(string? stat) => stat is
        "Weapon_Laser_Mining_Speed" or "Weapon_Projectile_Dispersion"
        or "Vehicle_LaserHeatTime" or "Vehicle_GunHeatTime" or "Vehicle_GunRate"
        or "Suit_Jetpack_Drain" or "Ship_Weapons_Guns_CoolTime"
        or "Ship_Weapons_Guns_Dispersion" or "Ship_PulseDrive_MiniJumpFuelSpending"
        or "Freighter_Fleet_Fuel";

    /// <summary>Stats displayed as a lightyear distance rather than a base-relative value.</summary>
    private static bool IsLightYearStat(string? stat) => stat is
        "Ship_Hyperdrive_JumpDistance" or "Freighter_Hyperdrive_JumpDistance";

    /// <summary>
    /// Formats a lightyear distance: whole number with grouping, inserted into the
    /// localised unit template (default "%DISTANCE% ly").
    /// </summary>
    private static string FormatLightYears(float value, string? unitTemplate)
    {
        string number = ((int)MathF.Round(value)).ToString("+#,##0;-#,##0;0", CultureInfo.InvariantCulture);
        string template = string.IsNullOrEmpty(unitTemplate) ? "%DISTANCE% ly" : unitTemplate;
        return template.Replace("%DISTANCE%", number, StringComparison.Ordinal);
    }

    /// <summary>Rounds half away from zero, matching the game's float-to-int cast.</summary>
    private static int RoundHalfAway(float value) =>
        (int)(value + MathF.CopySign(0.5f, value));

    /// <summary>
    /// Returns the display name for a stat: the uppercase stat id (truncated to 31 characters)
    /// resolved through the game localisation, falling back to the supplied name.
    /// </summary>
    internal static string GetStatDisplayName(LocalisationService? service, string statId, string fallback)
    {
        string key = statId.ToUpperInvariant();
        if (key.Length > 31) key = key[..31];
        return LookupGameString(service, key) ?? fallback;
    }

    /// <summary>Formats a rolled stat for display: "Name: +value" using the game's display rules.</summary>
    internal static string FormatRollLine(ProcStatRoll roll, string? itemBaseStat,
        LocalisationService? service, string? lightYearTemplate)
    {
        bool baseKnown = ProcTechData.TryGetBaseStatAmount(roll.Stat, itemBaseStat, out float baseAmount);
        return GetStatDisplayName(service, roll.Stat, roll.Name) + ": "
            + FormatValue(roll, baseAmount, baseKnown, lightYearTemplate);
    }

    // --- Generated names ----------------------------------------------------

    private static readonly object EnglishLock = new();
    private static string? _languageDirectory;
    private static Dictionary<string, string>? _english;

    /// <summary>
    /// Sets the directory containing the extracted game localisation JSON files
    /// (Resources/json/lang). Used as the English fallback when no language is active.
    /// </summary>
    internal static void SetLanguageDirectory(string langDirectory)
    {
        lock (EnglishLock)
        {
            _languageDirectory = langDirectory;
            _english = null;
        }
    }

    /// <summary>
    /// Generates the localised display name for a procedural technology item.
    /// Returns null when the technology has no word lists for its quality (caller keeps
    /// the database name in that case).
    /// </summary>
    /// <param name="tech">The procedural technology item (uses NameLocStr and Quality).</param>
    /// <param name="seedText">The technology seed digits, e.g. "95049".</param>
    /// <param name="service">The active localisation service, or null for English.</param>
    internal static string? GenerateTechName(GameItem? tech, string? seedText, LocalisationService? service)
    {
        if (tech == null || string.IsNullOrEmpty(seedText) || string.IsNullOrEmpty(tech.NameLocStr))
            return null;
        if (!uint.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
            return null;

        var indices = ComputeIndices(seed);
        string prefix = tech.NameLocStr;

        if (string.Equals(tech.Quality, "Sentinel", StringComparison.OrdinalIgnoreCase))
        {
            string? adj = Lookup(service, string.Format(CultureInfo.InvariantCulture, "UP_TECH_SENT_ADJ_{0}", indices.Adj20));
            string? noun = Lookup(service, string.Format(CultureInfo.InvariantCulture, "{0}_NOUN_{1}", prefix, indices.Noun20));
            return Format(Lookup(service, "UI_SENTINEL_TECH_FORMAT"), adj, noun,
                ("%SENT_ADJ%", "%SENT_NOUN%"));
        }

        if (string.Equals(tech.Quality, "Illegal", StringComparison.OrdinalIgnoreCase))
        {
            // The illegal wrapper takes the fully composed normal name as %TECHNAME%:
            // UP_TECH_ILLEGAL_ADJ_n + (RARE adjective + component).
            string? illegal = Lookup(service, string.Format(CultureInfo.InvariantCulture, "UP_TECH_ILLEGAL_ADJ_{0}", indices.IllegalAdj));
            string? rareAdjective = Lookup(service, string.Format(CultureInfo.InvariantCulture, "{0}_RARE_ADJ_{1}", prefix, indices.Adj5));
            string? innerComponent = Lookup(service, string.Format(CultureInfo.InvariantCulture, "{0}_COMP_{1}", prefix, indices.Comp));
            string? inner = Format(Lookup(service, "UP_NAME_FORMAT"), rareAdjective, innerComponent,
                ("%ADJECTIVE%", "%COMPONENT%"));
            return Format(Lookup(service, "UI_ILLEGAL_TECH_FORMAT"), illegal, inner,
                ("%ILLEGAL%", "%TECHNAME%"));
        }

        string? rarityClass = RarityClass(tech.Quality);
        if (rarityClass == null)
            return null;

        string? adjective = Lookup(service, string.Format(CultureInfo.InvariantCulture, "{0}_{1}_ADJ_{2}", prefix, rarityClass, indices.Adj5));
        string? component = Lookup(service, string.Format(CultureInfo.InvariantCulture, "{0}_COMP_{1}", prefix, indices.Comp));
        return Format(Lookup(service, "UP_NAME_FORMAT"), adjective, component,
            ("%ADJECTIVE%", "%COMPONENT%"));
    }

    /// <summary>
    /// Maps a quality name to the adjective word-list suffix used in the game's
    /// "{prefix}_{class}_ADJ_%i" keys.
    /// </summary>
    private static string? RarityClass(string? quality) => quality switch
    {
        "Normal" => "COMMON",
        "Rare" => "RARE",
        "Epic" => "EPIC",
        "Legendary" => "SCLASS",
        _ => null
    };

    /// <summary>
    /// Appends the game's package suffix to a generated technology name, e.g.
    /// "Writhing Energy Field Package" or "... Node" for sentient-ship technologies.
    /// </summary>
    internal static string? GeneratePackName(string? techName, GameItem? tech, LocalisationService? service)
    {
        if (string.IsNullOrEmpty(techName))
            return null;

        bool alien = string.Equals(tech?.TechnologyCategory, "AlienShip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(tech?.Category, "AlienShip", StringComparison.OrdinalIgnoreCase);
        string? format = Lookup(service, alien ? "UP_TECH_PACK_ALIEN_NAME" : "UP_TECH_PACK_NAME");
        if (string.IsNullOrEmpty(format))
            return null;

        return format
            .Replace("%name%", techName, StringComparison.Ordinal)
            .Replace("%NAME%", techName, StringComparison.Ordinal);
    }

    /// <summary>Fills a localised format string's two placeholders with the given words.</summary>
    private static string? Format(string? format, string? first, string? second,
        (string First, string Second) placeholders)
    {
        if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
            return null;

        return format
            .Replace(placeholders.First, first, StringComparison.Ordinal)
            .Replace(placeholders.Second, second, StringComparison.Ordinal);
    }

    /// <summary>Looks a key up in the active language, falling back to bundled English.</summary>
    private static string? Lookup(LocalisationService? service, string key)
    {
        string? value = service?.Lookup(key);
        if (!string.IsNullOrEmpty(value))
            return value;

        lock (EnglishLock)
        {
            if (_english == null)
                _english = LoadEnglish();
            return _english.TryGetValue(key, out string? fallback) && !string.IsNullOrEmpty(fallback) ? fallback : null;
        }
    }

    /// <summary>
    /// Looks up an arbitrary game localisation string (e.g. a stat display name) using the
    /// active language, falling back to bundled English when no language is selected.
    /// </summary>
    internal static string? LookupGameString(LocalisationService? service, string key) =>
        Lookup(service, key);

    /// <summary>Loads the extracted English game localisation file for fallback lookups.</summary>
    private static Dictionary<string, string> LoadEnglish()
    {
        if (string.IsNullOrEmpty(_languageDirectory))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string path = Path.Combine(_languageDirectory, "en-GB.json");
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.Ordinal);
            return JsonSerializer.Deserialize(File.ReadAllText(path), AppJsonContext.Default.DictionaryStringString)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Word indices derived from a technology seed, in generation order.</summary>
    internal readonly record struct ProcNameIndices(int Comp, int Adj20, int Noun20, int Adj5, int IllegalAdj);

    /// <summary>
    /// Replicates the seed mixing of the generator followed by its name PRNG to derive the
    /// word indices.
    /// </summary>
    internal static ProcNameIndices ComputeIndices(uint seed)
    {
        // GenerateProceduralTechnology: murmur-style mix of the parsed seed.
        ulong rA = PRNG.InitialState(seed);

        // Two PRNG steps before the name-seed transform.
        uint lowA = (uint)rA;
        ulong rB = PRNG.Next(rA);

        ulong t = unchecked((((rB & 0xffffffffUL) >> 1) ^ ((rB << 0x20) | lowA)) * NameMixA);
        t = unchecked(((t >> 0x21) ^ t) * NameMixB);
        ulong nameSeed = (t >> 0x21) ^ t;

        // GenerateProcTechName prologue mix, then the initial PRNG update.
        ulong a = unchecked(((nameSeed >> 0x21) ^ nameSeed) * NameMixA);
        ulong b = unchecked(((a >> 0x21) ^ a) * NameMixB);
        ulong r0 = (b >> 0x21) ^ b;

        uint low0 = (uint)r0;
        uint high0 = (uint)(r0 >> 32);
        uint adjustedLow = low0 + (low0 == 0 ? 1u : 0u);
        uint mix = (low0 << 16 | low0 >> 16) ^ high0 ^ low0;
        ulong state = unchecked((ulong)adjustedLow * 0x5a76f899UL + mix);

        int comp = Index(state, 10);
        state = PRNG.Next(state);
        int adj20 = Index(state, 20);
        int adj5 = Index(state, 5);
        state = PRNG.Next(state);
        int noun20 = Index(state, 20);
        int illegalAdj = Index(state, 15);
        return new ProcNameIndices(comp, adj20, noun20, adj5, illegalAdj);
    }

    /// <summary>Maps the PRNG state to a 1-based word index for a list of the given size.</summary>
    private static int Index(ulong state, uint listSize) =>
        (int)(((uint)state * (ulong)listSize) >> 32) + 1;
}
