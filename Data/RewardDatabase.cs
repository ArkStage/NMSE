namespace NMSE.Data;

/// <summary>Represents a single unlockable reward entry.</summary>
public class RewardEntry
{
    /// <summary>Unique reward identifier (e.g. "^VAULT_ARMOUR").</summary>
    public string Id { get; init; } = "";
    /// <summary>Human-readable reward name.</summary>
    public string Name { get; set; } = "";
    /// <summary>Reward category ("season", "twitch", or "platform").</summary>
    public string Category { get; init; } = "";
    /// <summary>Whether this reward requires explicit account-level unlocking
    /// (from MXML MustBeUnlocked). When false, the reward is auto-available.</summary>
    public bool Unlock { get; init; }
    /// <summary>Product ID used to look up item info (icon, description, name) from the game item database.
    /// May differ from Id for twitch/platform rewards (e.g. TwitchId vs ProductId).</summary>
    public string ProductId { get; init; } = "";

    /// <summary>The expedition/season number this reward belongs to (e.g. 21 for Expedition 21).
    /// -1 when not applicable or unknown. Season rewards only.</summary>
    public int SeasonId { get; init; } = -1;
    /// <summary>The progression stage within the expedition (-1 means no specific stage).
    /// Season rewards only.</summary>
    public int StageId { get; init; } = -1;

    /// <summary>Localisation lookup key for Name (e.g. "UI_EXPED_VAULT_ARMOUR_NAME"). Null when not available.</summary>
    public string? NameLocStr { get; init; }
    /// <summary>Localisation lookup key for Subtitle. Null when not available.</summary>
    public string? SubtitleLocStr { get; init; }

    /// <summary>Returns a string representation in "Name (Id)" format.</summary>
    public override string ToString() => $"{Name} ({Id})";
}

/// <summary>Database of all known expedition, Twitch, and platform rewards.
/// Loaded from Rewards.json at runtime; no hardcoded fallback.</summary>
public static class RewardDatabase
{
    private static readonly IReadOnlyList<RewardEntry> _empty = Array.Empty<RewardEntry>();

    // Dynamically loaded rewards from Rewards.json
    private static IReadOnlyList<RewardEntry>? _loadedRewards;
    private static IReadOnlyList<RewardEntry>? _loadedSeasonRewards;
    private static IReadOnlyList<RewardEntry>? _loadedTwitchRewards;
    private static IReadOnlyList<RewardEntry>? _loadedPlatformRewards;
    private static IReadOnlyList<RewardEntry>? _loadedEntitlementRewards;

    /// <summary>All known reward entries across all categories.</summary>
    public static IReadOnlyList<RewardEntry> Rewards => _loadedRewards ?? _empty;

    /// <summary>Rewards in the "season" category (expedition/seasonal unlocks).</summary>
    public static IEnumerable<RewardEntry> SeasonRewards => _loadedSeasonRewards ?? _empty;
    /// <summary>Rewards in the "twitch" category (Twitch drop unlocks).</summary>
    public static IEnumerable<RewardEntry> TwitchRewards => _loadedTwitchRewards ?? _empty;
    /// <summary>Rewards in the "platform" category (platform-specific unlocks).</summary>
    public static IEnumerable<RewardEntry> PlatformRewards => _loadedPlatformRewards ?? _empty;
    /// <summary>Rewards in the "entitlement" category (promotional/edition entitlements).
    /// These are NOT valid entries for UnlockedPlatformRewards; the game resolves them
    /// through their ProductId in the account-level UnlockedSpecials / Seen* arrays.</summary>
    public static IEnumerable<RewardEntry> EntitlementRewards => _loadedEntitlementRewards ?? _empty;
    /// <summary>Total number of reward entries in the database.</summary>
    public static int Count => Rewards.Count;

    /// <summary>
    /// Loads rewards from a Rewards.json file in the specified directory.
    /// Returns true if rewards were loaded from JSON.
    /// </summary>
    public static bool LoadFromJsonDirectory(string jsonDirectory)
    {
        string path = Path.Combine(jsonDirectory, "Rewards.json");
        if (!File.Exists(path)) return false;

        try
        {
            var entries = LoadRewardsFromJson(path);
            if (entries.Count == 0) return false;

            _loadedRewards = entries;
            _loadedSeasonRewards = entries.Where(r => r.Category == "season").ToList();
            _loadedTwitchRewards = entries.Where(r => r.Category == "twitch").ToList();
            _loadedPlatformRewards = entries.Where(r => r.Category is "platform" or "entitlement").ToList();
            _loadedEntitlementRewards = entries.Where(r => r.Category == "entitlement").ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a Rewards.json file into a list of RewardEntry objects.
    /// Each JSON element must have "Id", "Name", and "Category" properties.
    /// Optionally reads "ProductId" for item database lookups.
    /// </summary>
    internal static List<RewardEntry> LoadRewardsFromJson(string jsonPath)
    {
        var entries = new List<RewardEntry>();
        var content = File.ReadAllBytes(jsonPath);
        using var doc = System.Text.Json.JsonDocument.Parse(content);

        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            return entries;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (!element.TryGetProperty("Id", out var idProp)) continue;
            string id = idProp.GetString() ?? "";
            if (string.IsNullOrEmpty(id)) continue;

            string name = element.TryGetProperty("Name", out var nameProp)
                ? nameProp.GetString() ?? "" : "";
            string category = element.TryGetProperty("Category", out var catProp)
                ? catProp.GetString() ?? "" : "";
            string productId = element.TryGetProperty("ProductId", out var pidProp)
                ? pidProp.GetString() ?? "" : "";

            entries.Add(new RewardEntry
            {
                Id = id,
                Name = name,
                Category = category,
                ProductId = productId,
                Unlock = element.TryGetProperty("MustBeUnlocked", out var muProp) && muProp.ValueKind == System.Text.Json.JsonValueKind.True,
                SeasonId = element.TryGetProperty("SeasonId", out var siProp) && siProp.TryGetInt32(out int si) ? si : -1,
                StageId = element.TryGetProperty("StageId", out var stProp) && stProp.TryGetInt32(out int st) ? st : -1,
                NameLocStr = element.TryGetProperty("Name_LocStr", out var nls) ? nls.GetString() : null,
                SubtitleLocStr = element.TryGetProperty("Subtitle_LocStr", out var sls) ? sls.GetString() : null,
            });
        }

        return entries;
    }

    /// <summary>
    /// Resets the database to its unloaded state. Used for testing.
    /// </summary>
    internal static void Reset()
    {
        _loadedRewards = null;
        _loadedSeasonRewards = null;
        _loadedTwitchRewards = null;
        _loadedPlatformRewards = null;
        _loadedEntitlementRewards = null;
        _englishNameBackup.Clear();
    }

    private static readonly Dictionary<string, string> _englishNameBackup = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies localised display names from the specified localisation service to all rewards.
    /// Backs up the original English names so they can be restored via <see cref="RevertLocalisation"/>.
    /// </summary>
    /// <param name="service">The localisation service with a loaded language.</param>
    /// <returns>The number of rewards that had their name localised.</returns>
    public static int ApplyLocalisation(LocalisationService service)
    {
        if (!service.IsActive || _loadedRewards == null)
        {
            RevertLocalisation();
            return 0;
        }

        int count = 0;
        foreach (var reward in _loadedRewards)
        {
            if (!_englishNameBackup.ContainsKey(reward.Id))
                _englishNameBackup[reward.Id] = reward.Name;
            reward.Name = _englishNameBackup[reward.Id]; // Restore baseline first

            if (!string.IsNullOrEmpty(reward.NameLocStr))
            {
                var loc = service.Lookup(reward.NameLocStr);
                if (loc != null) { reward.Name = loc; count++; }
            }
        }

        return count;
    }

    /// <summary>
    /// Restores all reward names to their original English defaults.
    /// </summary>
    public static void RevertLocalisation()
    {
        if (_loadedRewards != null)
        {
            foreach (var reward in _loadedRewards)
            {
                if (_englishNameBackup.TryGetValue(reward.Id, out var englishName))
                    reward.Name = englishName;
            }
        }

        _englishNameBackup.Clear();
    }
}
