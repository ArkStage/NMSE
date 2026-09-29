using System.Threading;
using NMSE.Core.Utilities;
using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// All pet battle (Xeno Arena) logic: the true class roll and core stat values, the backwards
/// seed solver, the Arena League reward species and the battle record resets. The game's stat
/// order is Health, Speed, Combat; the in-game screen displays the stats as Combat, Speed,
/// Health.
/// </summary>
internal static class PetBattleLogic
{
    /// <summary>A pet's three true core stat classes (0 = C, 1 = B, 2 = A, 3 = S).</summary>
    internal readonly record struct Classes(int Combat, int Speed, int Health)
    {
        /// <summary>
        /// The overall rating index from the sum of the three class values
        /// (0 = C, 1 = B, 2 = A, 3 = S), matching the game's rating thresholds.
        /// </summary>
        internal int Overall
        {
            get
            {
                int sum = Combat + Speed + Health;
                for (int i = MaxClassIndex; i > 0; i--)
                    if (RatingThresholds[i] <= sum) return i;
                return 0;
            }
        }
    }

    /// <summary>A pet's three predicted core stat values, in the game's stat order.</summary>
    internal readonly record struct Stats(int Health, int Speed, int Combat);

    /// <summary>A set of per-stat RNG stream rolls in [0,1), in the game's stat order.</summary>
    internal readonly record struct StreamRolls(float Health, float Speed, float Combat);

    /// <summary>
    /// Reachable stat value ranges for a class and level set with a free scale, including the
    /// stat variance on Health and Speed.
    /// </summary>
    internal readonly record struct StatRanges(
        float HealthMin, float HealthMax, float SpeedMin, float SpeedMax, float CombatMin, float CombatMax);

    /// <summary>Identifies one of the three core stats, in the game's stat order.</summary>
    internal enum StatKind
    {
        /// <summary>The MaxHealth stat.</summary>
        Health,

        /// <summary>The Speed stat.</summary>
        Speed,

        /// <summary>The CombatPotential stat.</summary>
        Combat,
    }

    /// <summary>A rewarded species with the fixed seeds its egg carries.</summary>
    internal readonly record struct RewardSpecies(string CreatureId, ulong SpeciesSeed, ulong GenusSeed);

    /// <summary>Default number of ranked candidates kept and returned by a solver search.</summary>
    internal const int DefaultMaxResults = 20;

    /// <summary>Default candidate pairs examined before a solver search gives up.</summary>
    internal const long DefaultMaxCandidates = 2_000_000;

    /// <summary>
    /// Community marker placed in the top 16 bits of generated species and genus seeds so
    /// tool-generated pet seeds are identifiable. Game-found seeds do not carry it.
    /// </summary>
    internal const ushort GeneratedSeedMarker = 0xC0D3;

    /// <summary>
    /// A solve request: required classes, optional exact stat targets (null = maximise) and the
    /// fixed context the search must respect (gene edit levels, current scale, stat weights).
    /// </summary>
    internal readonly record struct SolveRequest(
        Classes TargetClasses,
        float? TargetHealth,
        float? TargetSpeed,
        float? TargetCombat,
        double CurrentScale,
        bool AdjustScale,
        int HealthLevel,
        int SpeedLevel,
        int CombatLevel,
        int MaxResults = DefaultMaxResults,
        long MaxCandidates = DefaultMaxCandidates,
        ulong Salt = 0,
        float HealthWeight = 1f,
        float SpeedWeight = 1f,
        float CombatWeight = 1f);

    /// <summary>
    /// The best simultaneously achievable stat values for a request (ideal stream rolls at the
    /// best shared scale for the request's weights) plus the score those ideal values would
    /// reach. Used to show how close a candidate is to what the game can actually produce.
    /// </summary>
    internal readonly record struct SolveReference(float Health, float Speed, float Combat, double IdealScore);

    /// <summary>A ranked solver candidate.</summary>
    internal readonly record struct SolveCandidate(
        ulong SpeciesSeed,
        ulong GenusSeed,
        float Scale,
        Stats Stats,
        double Score);

    private const ulong MixConstant = unchecked(0UL - 0x622015F714C7D297UL);
    private const int MaxClassIndex = 3;
    private const int DefaultMaxLevel = 10;
    private const float RangeEpsilon = 1f / 65536f;

    // Packed data tables (see _RE_Docs for the layout and how to regenerate the blob).
    private static readonly byte[] Mask =
    [
        0x5A, 0xC3, 0x1F, 0x9E, 0x47, 0xB2, 0xE0, 0x31, 0x8D, 0x66, 0xF4, 0x0B, 0x72, 0xA9, 0x3C, 0xD8,
    ];

    private static readonly byte[] Packed =
    [
        0x5A, 0x43, 0xCB, 0xDD, 0x47, 0x32, 0x0D, 0x72, 0x8D, 0xE6, 0xFD, 0x4F, 0x72, 0x69, 0x33, 0x9C,
        0x5A, 0x83, 0x1C, 0xDA, 0x47, 0xF2, 0xFC, 0x75, 0x8D, 0xE6, 0xD6, 0x4F, 0x72, 0xE9, 0x09, 0x9C,
        0xF5, 0xC3, 0x1F, 0x9E, 0xA6, 0xB2, 0xE0, 0x31, 0x9E, 0x67, 0xF4, 0x0B, 0x37, 0xA8, 0x3C, 0xD8,
        0x5A, 0xC3, 0xBF, 0xDF, 0x47, 0xB2, 0xC0, 0x73, 0x8D, 0x66, 0xBC, 0x49, 0x72, 0xA9, 0x4C, 0x9A,
        0x5A, 0xC3, 0x57, 0xDC, 0x47, 0xB2, 0x90, 0x73, 0x8D, 0x66, 0x54, 0x49, 0x72, 0xA9, 0xF4, 0x9A,
        0x57, 0xC3, 0x1F, 0x9E, 0x51, 0xB2, 0xE0, 0x31, 0x94, 0x66, 0xF4, 0x0B, 0x6C, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0xD7, 0xDC, 0x47, 0xB2, 0x10, 0x73, 0x8D, 0x66, 0xD4, 0x48, 0x72, 0xA9, 0x08, 0x9B,
        0x5A, 0xC3, 0xEF, 0xDC, 0x47, 0xB2, 0xEC, 0x72, 0x8D, 0x66, 0xC0, 0x48, 0x72, 0xA9, 0x74, 0x9B,
        0x73, 0xC3, 0x1F, 0x9E, 0x77, 0xB2, 0xE0, 0x31, 0xB2, 0x66, 0xF4, 0x0B, 0x34, 0xA9, 0x3C, 0xD8,
        0x44, 0xC3, 0x1F, 0x9E, 0x59, 0xB2, 0xE0, 0x31, 0x94, 0x66, 0xF4, 0x0B, 0x7D, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0x9E, 0x46, 0xB2, 0xE0, 0x31, 0x89, 0x66, 0xF4, 0x0B, 0x75, 0xA9, 0x3C, 0xD8,
        0x97, 0x0F, 0x53, 0xA0, 0x8A, 0x7E, 0x2C, 0x0F, 0x8D, 0x66, 0x74, 0x4B, 0x31, 0xE6, 0x6E, 0x9D,
        0x05, 0x90, 0x4B, 0xDF, 0x13, 0xED, 0xA3, 0x7D, 0xCC, 0x35, 0xA7, 0x4E, 0x21, 0xEA, 0x73, 0x95,
        0x18, 0x82, 0x4B, 0xCD, 0x17, 0xF7, 0xA5, 0x75, 0xC5, 0x23, 0xB5, 0x47, 0x26, 0xE1, 0x7F, 0x97,
        0x08, 0x86, 0x40, 0xCD, 0x13, 0xF3, 0xB4, 0x6E, 0xCF, 0x27, 0xA7, 0x4E, 0x2D, 0xFF, 0x7D, 0x94,
        0x0F, 0x86, 0x4C, 0xC9, 0x06, 0xFE, 0xAB, 0x74, 0xDF, 0x39, 0xB7, 0x59, 0x33, 0xEB, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0x0C, 0x42, 0xB2, 0xE0, 0x31, 0x8D, 0x66, 0xF4, 0x98, 0x77, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0xD8, 0x0E, 0xE1, 0xA8, 0x73, 0xC2, 0x31, 0xB8, 0x54, 0x22, 0xEC, 0x68, 0xEB,
        0x5A, 0xC3, 0x1F, 0x57, 0x2F, 0xE7, 0x79, 0xF7, 0xCC, 0xC6, 0x3C, 0x62, 0x99, 0xA8, 0x30, 0x22,
        0xB1, 0x06, 0x4A, 0xD2, 0x06, 0xFC, 0xA4, 0x62, 0xDC, 0x33, 0xBD, 0x4F, 0x2D, 0xF9, 0x79, 0x8C,
        0x5A, 0xC3, 0x1F, 0x08, 0x42, 0xB2, 0xE0, 0x31, 0x8D, 0x66, 0xF4, 0x9C, 0x77, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0xCD, 0x17, 0xFB, 0xA4, 0x74, 0xDF, 0x37, 0xA1, 0x4A, 0x36, 0xF6, 0x6C, 0x9D,
        0x0E, 0xC3, 0x1F, 0x06, 0x42, 0xB2, 0xE0, 0x31, 0x8D, 0x66, 0xF4, 0x91, 0x77, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0xD6, 0x08, 0xE0, 0xB2, 0x7E, 0xDF, 0x39, 0xA4, 0x4E, 0x26, 0xA9, 0x3C, 0xD8,
        0x5A, 0xC3, 0x1F, 0x84, 0xC2, 0xA3, 0xA3, 0x28, 0x76, 0xD4, 0x3F, 0xBA, 0x8F, 0x81, 0x6D, 0xAC,
        0x1C, 0x38, 0xB6, 0xD0, 0x0A, 0xE1, 0xA5, 0x6E, 0xDF, 0x23, 0xAB, 0x46, 0x33, 0xFB, 0x77, 0x87,
        0x6A, 0xF3, 0x2E,
    ];

    private static readonly byte[] Raw = Unmask(Packed);

    private static readonly float[] HealthRangeMin = Floats(0);
    private static readonly float[] HealthRangeMax = Floats(16);
    private static readonly int[] HealthBoosts = Ints(32);
    private static readonly float[] SpeedRangeMin = Floats(48);
    private static readonly float[] SpeedRangeMax = Floats(64);
    private static readonly int[] SpeedBoosts = Ints(80);
    private static readonly float[] CombatRangeMin = Floats(96);
    private static readonly float[] CombatRangeMax = Floats(112);
    private static readonly int[] CombatBoosts = Ints(128);
    private static readonly int[] ClassWeights = Ints(144);
    private static readonly int[] RatingThresholds = Ints(160);
    private static readonly float ScaleVariance = Float(176);
    private static readonly float AgilityScaleMin = Float(180);
    private static readonly float AgilityScaleMax = Float(184);
    private static readonly string RootForkId = Text(188, 17);
    private static readonly string[] StreamForkIds = [Text(205, 6), Text(211, 5), Text(216, 6)];
    private static readonly string BaseValuesForkId = Text(222, 21);
    private static readonly RewardSpecies[] RewardSpeciesTable = Rewards(243);

    private static byte[] Unmask(byte[] data)
    {
        var raw = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            raw[i] = (byte)(data[i] ^ Mask[i & 15]);
        return raw;
    }

    private static float[] Floats(int offset, int count = 4)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = BitConverter.ToSingle(Raw, offset + i * 4);
        return values;
    }

    private static int[] Ints(int offset, int count = 4)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++)
            values[i] = BitConverter.ToInt32(Raw, offset + i * 4);
        return values;
    }

    private static float Float(int offset) => BitConverter.ToSingle(Raw, offset);

    private static string Text(int offset, int length)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)Raw[offset + i];
        return new string(chars);
    }

    private static RewardSpecies[] Rewards(int offset)
    {
        var rewards = new RewardSpecies[5];
        for (int i = 0; i < rewards.Length; i++)
        {
            int entry = offset + i * 32;
            int length = 0;
            while (length < 16 && Raw[entry + length] != 0) length++;
            rewards[i] = new RewardSpecies(Text(entry, length),
                BitConverter.ToUInt64(Raw, entry + 16), BitConverter.ToUInt64(Raw, entry + 24));
        }
        return rewards;
    }

    /// <summary>Names of all battle-related keys stored in a companion JSON object.</summary>
    internal static readonly string[] BattleKeys =
    {
        "PetBattlerUseCoreStatClassOverrides",
        "PetBattlerCoreStatClassOverrides",
        "PetBattlerTreatsEaten",
        "PetBattlerTreatsAvailable",
        "PetBattleProgressToTreat",
        "PetBattlerVictories",
        "PetBattlerMoveList",
        "PetBattlerMoves",
    };

    // --- True roll ----------------------------------------------------------

    /// <summary>Returns the letter for a class value (0..3).</summary>
    internal static char ClassLetter(int value) => ClassLetters[Math.Clamp(value, 0, MaxClassIndex)];

    private static readonly char[] ClassLetters = ['C', 'B', 'A', 'S'];

    /// <summary>Mixes the species and genus seeds into the pet battle seed.</summary>
    internal static ulong MixSeeds(ulong speciesSeed, ulong genusSeed)
    {
        ulong hash = unchecked((speciesSeed ^ genusSeed) * MixConstant);
        hash = unchecked(((hash >> 47) ^ speciesSeed ^ hash) * MixConstant);
        return unchecked(((hash >> 47) ^ hash) * MixConstant);
    }

    /// <summary>
    /// Rolls the three true stat classes from the pet's species and genus seeds.
    /// </summary>
    internal static Classes RollClasses(ulong speciesSeed, ulong genusSeed)
    {
        var seed = PRNG.FromSeed(MixSeeds(speciesSeed, genusSeed));
        seed = PRNG.ForkChar(seed, RootForkId);

        var classes = new int[3];
        for (int stream = 0; stream < 3; stream++)
        {
            var fork = PRNG.ForkTkId(seed, StreamForkIds[stream]);
            uint roll = PRNG.NextValue(fork);
            long scaled = (long)((ulong)roll * 100UL) >> 32;
            int cumulative = 0;
            int chosen = MaxClassIndex;
            for (int i = 0; i < 4; i++)
            {
                cumulative += ClassWeights[i];
                if (scaled < cumulative)
                {
                    chosen = i;
                    break;
                }
            }
            classes[stream] = chosen;
        }

        return new Classes(Combat: classes[2], Speed: classes[1], Health: classes[0]);
    }

    /// <summary>
    /// Draws the three per-stat value stream rolls in [0,1) from the pet's species and genus
    /// seeds. The streams are independent of the class roll streams.
    /// </summary>
    internal static StreamRolls RollStreams(ulong speciesSeed, ulong genusSeed)
    {
        var seed = PRNG.FromSeed(MixSeeds(speciesSeed, genusSeed));
        return new StreamRolls(
            NextStreamRoll(seed, StreamForkIds[0]),
            NextStreamRoll(seed, StreamForkIds[1]),
            NextStreamRoll(seed, StreamForkIds[2]));
    }

    private static float NextStreamRoll(PRNG.SeedPair seed, string streamForkId)
    {
        var fork = PRNG.ForkCharAndTkId(seed, BaseValuesForkId, streamForkId);
        return PRNG.Fraction(PRNG.NextValue(fork));
    }

    /// <summary>
    /// Deterministic seed search for a species/genus seed pair that rolls the target classes.
    /// Returns null when no pair was found within the attempt limit.
    /// </summary>
    internal static (ulong SpeciesSeed, ulong GenusSeed)? FindSeed(
        Classes target, int maxAttempts = 200000, ulong salt = 0)
    {
        ulong state = unchecked(SplitMix64.Gamma + salt);
        for (int i = 0; i < maxAttempts; i++)
        {
            ulong speciesSeed = ((ulong)GeneratedSeedMarker << 48) | (SplitMix64.Next(ref state) & 0x0000FFFFFFFFFFFFUL);
            ulong genusSeed = ((ulong)GeneratedSeedMarker << 48) | (SplitMix64.Next(ref state) & 0x0000FFFFFFFFFFFFUL);
            if (RollClasses(speciesSeed, genusSeed) == target)
                return (speciesSeed, genusSeed);
        }
        return null;
    }

    // --- Core stat values ---------------------------------------------------

    /// <summary>
    /// Predicts the three core stat values for a pet. Health and Speed use the class range
    /// lerped by the pet's size factor with a random variance; Combat is a plain random lerp of
    /// its range. Each stat then gains level * class per-level boost.
    /// </summary>
    internal static Stats PredictCoreStats(
        ulong speciesSeed, ulong genusSeed, float scale, Classes classes,
        int healthLevel, int speedLevel, int combatLevel, int maxLevel = DefaultMaxLevel)
    {
        var rolls = RollStreams(speciesSeed, genusSeed);
        return PredictCoreStatsFromRolls(classes, rolls, CurveSizeFactor(scale),
            healthLevel, speedLevel, combatLevel, maxLevel);
    }

    /// <summary>
    /// Predicts the core stat values from already-drawn stream rolls and the raw size curve
    /// value (the same maths as <see cref="PredictCoreStats"/>, for callers that search or
    /// optimise the scale themselves).
    /// </summary>
    internal static Stats PredictCoreStatsFromRolls(
        Classes classes, StreamRolls rolls, float sizeCurve,
        int healthLevel, int speedLevel, int combatLevel, int maxLevel = DefaultMaxLevel)
    {
        float healthFactor = Clamp01(sizeCurve);
        float speedFactor = Clamp01(1f - sizeCurve);

        int health = ComputeStat(HealthRangeMin, HealthRangeMax, HealthBoosts,
            classes.Health, healthLevel, maxLevel, useVariance: true, healthFactor, rolls.Health);
        int speed = ComputeStat(SpeedRangeMin, SpeedRangeMax, SpeedBoosts,
            classes.Speed, speedLevel, maxLevel, useVariance: true, speedFactor, rolls.Speed);
        int combat = ComputeStat(CombatRangeMin, CombatRangeMax, CombatBoosts,
            classes.Combat, combatLevel, maxLevel, useVariance: false, 0f, rolls.Combat);

        return new Stats(health, speed, combat);
    }

    /// <summary>
    /// Calculates one stat value from an explicit stream roll and size curve value (the raw
    /// curve in [0,1], not the scale). Exposed for the seed solver's reference calculations.
    /// </summary>
    internal static int StatValue(
        StatKind kind, Classes classes, int level, int maxLevel,
        float roll01, float sizeCurve)
    {
        return kind switch
        {
            StatKind.Health => ComputeStat(HealthRangeMin, HealthRangeMax, HealthBoosts,
                classes.Health, level, maxLevel, useVariance: true, Clamp01(sizeCurve), roll01),
            StatKind.Speed => ComputeStat(SpeedRangeMin, SpeedRangeMax, SpeedBoosts,
                classes.Speed, level, maxLevel, useVariance: true, Clamp01(1f - sizeCurve), roll01),
            _ => ComputeStat(CombatRangeMin, CombatRangeMax, CombatBoosts,
                classes.Combat, level, maxLevel, useVariance: false, 0f, roll01),
        };
    }

    /// <summary>
    /// Returns the reachable value range per stat for the given classes and levels with a
    /// free scale (Health and Speed include the stat variance).
    /// </summary>
    internal static StatRanges GetReachableRanges(
        Classes classes, int healthLevel, int speedLevel, int combatLevel, int maxLevel = DefaultMaxLevel)
    {
        int healthLevelClamped = healthLevel < maxLevel ? healthLevel : maxLevel;
        int speedLevelClamped = speedLevel < maxLevel ? speedLevel : maxLevel;
        int combatLevelClamped = combatLevel < maxLevel ? combatLevel : maxLevel;

        float healthBoost = healthLevelClamped * HealthBoosts[classes.Health];
        float speedBoost = speedLevelClamped * SpeedBoosts[classes.Speed];
        float combatBoost = combatLevelClamped * CombatBoosts[classes.Combat];

        return new StatRanges(
            HealthRangeMin[classes.Health] * (1f - ScaleVariance) + healthBoost,
            HealthRangeMax[classes.Health] * (1f + ScaleVariance) + healthBoost,
            SpeedRangeMin[classes.Speed] * (1f - ScaleVariance) + speedBoost,
            SpeedRangeMax[classes.Speed] * (1f + ScaleVariance) + speedBoost,
            CombatRangeMin[classes.Combat] + combatBoost,
            CombatRangeMax[classes.Combat] + combatBoost);
    }

    /// <summary>
    /// Computes the curved size factor: the pet's Scale normalised over the agility scale range
    /// (0.4..4.0), passed through a circular ease out curve (sqrt((2 - t) * t)).
    /// </summary>
    internal static float CurveSizeFactor(float scale)
    {
        float t = 1f;
        float range = AgilityScaleMax - AgilityScaleMin;
        if (RangeEpsilon < MathF.Abs(range))
        {
            float raw = (scale - AgilityScaleMin) / range;
            if (raw < 0f) raw = 0f;
            if (raw <= 1f) t = raw;
        }
        return MathF.Sqrt((2f - t) * t);
    }

    /// <summary>
    /// Returns the floor and ceiling the game uses for the pet screen's stat bars. The bars
    /// show progress from the weakest possible value (class C minimum) to the strongest value
    /// achievable at the maximum gene level (class S maximum at maximum variance plus the class
    /// S level boosts), so bars stay small until the pet has gene edit levels.
    /// </summary>
    internal static (float Floor, float Ceiling) GetGameBarRange(StatKind kind)
    {
        float maxLevelBoosts = DefaultMaxLevel;
        return kind switch
        {
            StatKind.Health => (
                HealthRangeMin[0],
                HealthRangeMax[MaxClassIndex] + HealthBoosts[MaxClassIndex] * maxLevelBoosts),
            StatKind.Speed => (
                SpeedRangeMin[0] * (1f - ScaleVariance),
                SpeedRangeMax[MaxClassIndex] * (1f + ScaleVariance) + SpeedBoosts[MaxClassIndex] * maxLevelBoosts),
            _ => (
                CombatRangeMin[0],
                CombatRangeMax[MaxClassIndex] + CombatBoosts[MaxClassIndex] * maxLevelBoosts),
        };
    }

    /// <summary>
    /// Returns the pet screen stat bar fill fraction the game computes for a stat value
    /// (clamped to 0..1). This is what the in-game bars actually display.
    /// </summary>
    internal static float GetGameBarFraction(StatKind kind, int value)
    {
        var (floor, ceiling) = GetGameBarRange(kind);
        float span = ceiling - floor;
        if (!(span > 0f)) return 0f;
        return Clamp01((value - floor) / span);
    }

    /// <summary>Clamps a value to the 0..1 range.</summary>
    internal static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    /// <summary>Computes one stat value from its stream roll, range, size factor and level boost.</summary>
    private static int ComputeStat(
        float[] rangeMin, float[] rangeMax, int[] boosts, int classIndex, int level, int maxLevel,
        bool useVariance, float factor, float roll01)
    {
        float min = rangeMin[classIndex];
        float max = rangeMax[classIndex];

        float baseValue;
        if (useVariance)
        {
            float variance = (ScaleVariance * 2f) * roll01 - ScaleVariance;
            float minVariance = variance * min + min;
            float maxVariance = variance * max + max;
            baseValue = (maxVariance - minVariance) * factor + minVariance;
        }
        else
        {
            baseValue = (max - min) * roll01 + min;
        }

        int clampedLevel = level < maxLevel ? level : maxLevel;
        return (int)baseValue + clampedLevel * boosts[classIndex];
    }

    // --- Backwards seed/scale solver ----------------------------------------

    /// <summary>
    /// Runs the search until the candidate budget is exhausted or the token is signalled,
    /// returning the best candidates ranked by score (1 = every exact target hit exactly).
    /// Progress reports the number of seed pairs examined.
    /// </summary>
    internal static IReadOnlyList<SolveCandidate> Solve(
        SolveRequest request, CancellationToken cancellationToken, IProgress<long>? progress = null)
    {
        var classes = request.TargetClasses;
        var ranges = GetReachableRanges(classes, request.HealthLevel, request.SpeedLevel, request.CombatLevel);
        double healthSpan = Math.Max(1.0, ranges.HealthMax - ranges.HealthMin);
        double speedSpan = Math.Max(1.0, ranges.SpeedMax - ranges.SpeedMin);
        double combatSpan = Math.Max(1.0, ranges.CombatMax - ranges.CombatMin);

        float fixedFactor = CurveSizeFactor((float)request.CurrentScale);
        bool allTargetsExact = request.TargetHealth != null && request.TargetSpeed != null && request.TargetCombat != null;

        var best = new List<SolveCandidate>(request.MaxResults + 1);
        int perfectCount = 0;
        int maxResults = Math.Max(1, request.MaxResults);

        ulong rng = unchecked(SplitMix64.Gamma + request.Salt);
        long examined = 0;
        long nextProgress = 0;

        while (examined < request.MaxCandidates)
        {
            if ((examined & 0xFFF) == 0)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (examined >= nextProgress)
                {
                    progress?.Report(examined);
                    nextProgress = examined + 65536;
                }
            }

            ulong speciesSeed = ((ulong)GeneratedSeedMarker << 48) | (SplitMix64.Next(ref rng) & 0x0000FFFFFFFFFFFFUL);
            ulong genusSeed = ((ulong)GeneratedSeedMarker << 48) | (SplitMix64.Next(ref rng) & 0x0000FFFFFFFFFFFFUL);
            examined++;

            if (RollClasses(speciesSeed, genusSeed) != classes)
                continue;

            var rolls = RollStreams(speciesSeed, genusSeed);

            float factor = fixedFactor;
            float scale = (float)request.CurrentScale;
            if (request.AdjustScale)
            {
                factor = BestSizeFactor(request, rolls, ranges, healthSpan, speedSpan);
                scale = ScaleFromFactor(factor);
                factor = CurveSizeFactor(scale);
            }

            var stats = PredictCoreStatsFromRolls(
                classes, rolls, factor, request.HealthLevel, request.SpeedLevel, request.CombatLevel);
            double score = Score(request, stats, ranges, healthSpan, speedSpan, combatSpan);

            bool perfect = IsPerfect(request, stats);
            bool inserted = Insert(best, new SolveCandidate(speciesSeed, genusSeed, scale, stats, score),
                maxResults, request.CurrentScale);
            if (inserted && perfect) perfectCount++;

            if (allTargetsExact && perfectCount >= maxResults)
                break;
        }

        progress?.Report(examined);
        return best;
    }

    /// <summary>
    /// Computes the best simultaneously achievable stat values and the ideal score for a
    /// request. Maximised stats use ideal rolls (maximum variance and combat roll) at the best
    /// shared scale; exact targets are treated as ideally hit.
    /// </summary>
    internal static SolveReference GetReference(SolveRequest request)
    {
        var classes = request.TargetClasses;
        var ranges = GetReachableRanges(classes, request.HealthLevel, request.SpeedLevel, request.CombatLevel);
        double healthSpan = Math.Max(1.0, ranges.HealthMax - ranges.HealthMin);
        double speedSpan = Math.Max(1.0, ranges.SpeedMax - ranges.SpeedMin);
        double combatSpan = Math.Max(1.0, ranges.CombatMax - ranges.CombatMin);

        float bestFactor = 0f;
        if (request.TargetHealth == null || request.TargetSpeed == null)
        {
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < 48; i++)
            {
                float third = (high - low) / 3f;
                float mid1 = low + third;
                float mid2 = high - third;
                if (IdealObjective(mid1) <= IdealObjective(mid2)) high = mid2;
                else low = mid1;
            }
            bestFactor = (low + high) * 0.5f;
        }

        float health = request.TargetHealth ?? StatValue(
            StatKind.Health, classes, request.HealthLevel, DefaultMaxLevel, 1f, bestFactor);
        float speed = request.TargetSpeed ?? StatValue(
            StatKind.Speed, classes, request.SpeedLevel, DefaultMaxLevel, 1f, bestFactor);
        float combat = request.TargetCombat ?? StatValue(
            StatKind.Combat, classes, request.CombatLevel, DefaultMaxLevel, 1f, 0f);

        double healthError = request.TargetHealth != null ? 0.0
            : Math.Clamp((ranges.HealthMax - health) / healthSpan, 0.0, 1.0);
        double speedError = request.TargetSpeed != null ? 0.0
            : Math.Clamp((ranges.SpeedMax - speed) / speedSpan, 0.0, 1.0);
        double combatError = request.TargetCombat != null ? 0.0
            : Math.Clamp((ranges.CombatMax - combat) / combatSpan, 0.0, 1.0);
        double idealScore = 1.0 - (HealthWeight(request) * healthError
            + SpeedWeight(request) * speedError + CombatWeight(request) * combatError) / TotalWeight(request);
        return new SolveReference(health, speed, combat, Math.Clamp(idealScore, 0.05, 1.0));

        double IdealObjective(float factor)
        {
            int healthValue = StatValue(
                StatKind.Health, classes, request.HealthLevel, DefaultMaxLevel, 1f, factor);
            int speedValue = StatValue(
                StatKind.Speed, classes, request.SpeedLevel, DefaultMaxLevel, 1f, factor);
            double healthError = request.TargetHealth != null ? 0.0
                : Math.Clamp((ranges.HealthMax - healthValue) / healthSpan, 0.0, 1.0);
            double speedError = request.TargetSpeed != null ? 0.0
                : Math.Clamp((ranges.SpeedMax - speedValue) / speedSpan, 0.0, 1.0);
            return HealthWeight(request) * healthError + SpeedWeight(request) * speedError;
        }
    }

    /// <summary>
    /// Finds the size curve value in [0,1] that minimises the weighted Health/Speed error for
    /// a candidate's rolls. The objective is convex, so a ternary search is exact enough.
    /// </summary>
    private static float BestSizeFactor(
        SolveRequest request, StreamRolls rolls, StatRanges ranges, double healthSpan, double speedSpan)
    {
        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 48; i++)
        {
            float third = (high - low) / 3f;
            float mid1 = low + third;
            float mid2 = high - third;
            if (Objective(mid1) <= Objective(mid2)) high = mid2;
            else low = mid1;
        }
        return (low + high) * 0.5f;

        double Objective(float factor)
        {
            var stats = PredictCoreStatsFromRolls(
                request.TargetClasses, rolls, factor,
                request.HealthLevel, request.SpeedLevel, request.CombatLevel);
            double health = StatError(stats.Health, request.TargetHealth, ranges.HealthMax, healthSpan);
            double speed = StatError(stats.Speed, request.TargetSpeed, ranges.SpeedMax, speedSpan);
            return HealthWeight(request) * health + SpeedWeight(request) * speed;
        }
    }

    /// <summary>Converts a size curve value back into the scale that produces it.</summary>
    private static float ScaleFromFactor(float factor)
    {
        float clamped = Clamp01(factor);
        float t = 1f - MathF.Sqrt(MathF.Max(0f, 1f - clamped * clamped));
        return AgilityScaleMin + (AgilityScaleMax - AgilityScaleMin) * t;
    }

    /// <summary>Computes a candidate's weighted score in [0,1] from the normalised per-stat errors.</summary>
    private static double Score(
        SolveRequest request, Stats stats, StatRanges ranges,
        double healthSpan, double speedSpan, double combatSpan)
    {
        double health = StatError(stats.Health, request.TargetHealth, ranges.HealthMax, healthSpan);
        double speed = StatError(stats.Speed, request.TargetSpeed, ranges.SpeedMax, speedSpan);
        double combat = StatError(stats.Combat, request.TargetCombat, ranges.CombatMax, combatSpan);
        double weighted = HealthWeight(request) * health
            + SpeedWeight(request) * speed + CombatWeight(request) * combat;
        return 1.0 - weighted / TotalWeight(request);
    }

    /// <summary>
    /// Normalised error for one stat: distance to an exact target, or distance from the
    /// maximum reachable value when maximising.
    /// </summary>
    private static double StatError(int value, float? target, float maxReachable, double span)
    {
        double error = target is float exact ? Math.Abs(value - exact) : maxReachable - value;
        return Math.Clamp(error / span, 0.0, 1.0);
    }

    /// <summary>True when every exact target is hit to the nearest integer.</summary>
    private static bool IsPerfect(SolveRequest request, Stats stats) =>
        (request.TargetHealth == null || Math.Abs(stats.Health - request.TargetHealth.Value) <= 0.5) &&
        (request.TargetSpeed == null || Math.Abs(stats.Speed - request.TargetSpeed.Value) <= 0.5) &&
        (request.TargetCombat == null || Math.Abs(stats.Combat - request.TargetCombat.Value) <= 0.5);

    /// <summary>
    /// Inserts a candidate into the ranked list (score descending, then scale closest to the
    /// current scale, then seed order). Returns false when the candidate did not rank.
    /// </summary>
    private static bool Insert(
        List<SolveCandidate> best, SolveCandidate candidate, int maxResults, double currentScale)
    {
        int index = best.Count;
        for (int i = 0; i < best.Count; i++)
        {
            if (Compare(candidate, best[i], currentScale) < 0)
            {
                index = i;
                break;
            }
        }
        if (index >= maxResults) return false;

        best.Insert(index, candidate);
        if (best.Count > maxResults) best.RemoveAt(best.Count - 1);
        return true;
    }

    private static int Compare(SolveCandidate a, SolveCandidate b, double currentScale)
    {
        int byScore = b.Score.CompareTo(a.Score);
        if (byScore != 0) return byScore;

        double distanceA = Math.Abs(a.Scale - currentScale);
        double distanceB = Math.Abs(b.Scale - currentScale);
        int byScale = distanceA.CompareTo(distanceB);
        if (byScale != 0) return byScale;

        int bySpecies = a.SpeciesSeed.CompareTo(b.SpeciesSeed);
        if (bySpecies != 0) return bySpecies;
        return a.GenusSeed.CompareTo(b.GenusSeed);
    }

    private static double HealthWeight(SolveRequest request) => Math.Max(0f, request.HealthWeight);

    private static double SpeedWeight(SolveRequest request) => Math.Max(0f, request.SpeedWeight);

    private static double CombatWeight(SolveRequest request) => Math.Max(0f, request.CombatWeight);

    private static double TotalWeight(SolveRequest request)
    {
        double total = HealthWeight(request) + SpeedWeight(request) + CombatWeight(request);
        return total > 0.0 ? total : 3.0;
    }

    // --- Arena League reward species ----------------------------------------

    /// <summary>The forced class set granted to reward pets (S/S/S).</summary>
    internal static Classes RewardClasses => new(3, 3, 3);

    /// <summary>Returns true when the creature id is one of the five rewarded species.</summary>
    internal static bool IsRewardSpecies(string? creatureId) => TryGetSpecies(creatureId, out _);

    /// <summary>Looks up the reward entry for a creature id (a leading "^" is optional).</summary>
    internal static bool TryGetSpecies(string? creatureId, out RewardSpecies species)
    {
        species = default;
        if (string.IsNullOrWhiteSpace(creatureId)) return false;

        var id = creatureId.Trim();
        if (id.StartsWith("^", StringComparison.Ordinal)) id = id[1..];

        foreach (var entry in RewardSpeciesTable)
        {
            if (string.Equals(entry.CreatureId, id, StringComparison.OrdinalIgnoreCase))
            {
                species = entry;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Applies the class override state a reset or repair should leave behind: reward species
    /// keep their granted S/S/S overrides (flag enabled); all other species return to the
    /// default state (flag disabled, classes C).
    /// </summary>
    internal static void ApplyResetClassOverrides(JsonObject companion)
    {
        bool reward = IsRewardSpecies(companion.GetString("CreatureID"));
        try { companion.Set("PetBattlerUseCoreStatClassOverrides", reward); } catch { }

        try
        {
            var overrides = companion.GetArray("PetBattlerCoreStatClassOverrides");
            if (overrides == null) return;
            string value = reward ? "S" : "C";
            for (int i = 0; i < overrides.Length; i++)
                overrides.GetObject(i)?.Set("InventoryClass", value);
        }
        catch { }
    }

    // --- Battle record resets -----------------------------------------------

    /// <summary>
    /// Resets all pet battle data fields to their default state (used when clearing a slot,
    /// so the chosen abilities are cleared too).
    /// </summary>
    internal static void ResetBattleData(JsonObject companion) => ResetBattleCore(companion, clearAbilities: true);

    /// <summary>
    /// Resets the pet battle record (gene edits, unspent edits, progress, victories and the
    /// legacy move list) while keeping the chosen abilities. Reward species keep their granted
    /// S/S/S class overrides; other species return to the default class state.
    /// </summary>
    internal static void ResetBattleRecord(JsonObject companion) => ResetBattleCore(companion, clearAbilities: false);

    /// <summary>Shared implementation of the battle data resets.</summary>
    private static void ResetBattleCore(JsonObject companion, bool clearAbilities)
    {
        // Class overrides: reward species keep their granted S/S/S set, everything else
        // returns to the default (flag off, classes C).
        ApplyResetClassOverrides(companion);

        // PetBattlerTreatsEaten: array of 3 integers -> 0
        try
        {
            var treats = companion.GetArray("PetBattlerTreatsEaten");
            if (treats != null)
                for (int i = 0; i < treats.Length; i++)
                    treats.Set(i, 0);
        }
        catch { }

        try { companion.Set("PetBattlerTreatsAvailable", 0); } catch { }
        try { companion.Set("PetBattleProgressToTreat", 0.0); } catch { }
        try { companion.Set("PetBattlerVictories", 0); } catch { }

        // PetBattlerMoveList: array of 5 move objects -> reset MoveTemplateID/Cooldown/ScoreBoost
        // (legacy key, no longer used by the game but still present in saves)
        try
        {
            var moveList = companion.GetArray("PetBattlerMoveList");
            if (moveList != null)
                for (int i = 0; i < moveList.Length; i++)
                {
                    var obj = moveList.GetObject(i);
                    if (obj != null)
                    {
                        obj.Set("MoveTemplateID", "^");
                        obj.Set("Cooldown", 0);
                        obj.Set("ScoreBoost", 0.0);
                    }
                }
        }
        catch { }

        // PetBattlerMoves: array of 5 move ID strings -> reset to "^" (slot clears only)
        if (!clearAbilities) return;
        try
        {
            var moves = companion.GetArray("PetBattlerMoves");
            if (moves != null)
                for (int i = 0; i < moves.Length; i++)
                    moves.Set(i, "^");
        }
        catch { }
    }
}
