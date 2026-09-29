using System.Globalization;
using System.Xml.Linq;

namespace NMSE.Core;

/// <summary>
/// Reads and writes reward list entries in GCUSERSETTINGSDATA.MXML.
/// The MXML file uses a container Property element with child Property elements:
/// <code>
///   &lt;Property name="UnlockedPlatformRewards"&gt;
///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER" _index="0" /&gt;
///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER2" _index="1" /&gt;
///   &lt;/Property&gt;
/// </code>
/// Supported lists:
/// <list type="bullet">
///   <item><c>UnlockedSeasonRewards</c> - season/expedition reward IDs.</item>
///   <item><c>UnlockedTwitchRewards</c> - Twitch drop reward IDs.</item>
///   <item><c>UnlockedPlatformRewards</c> - platform unlock IDs (TGA_SHIP1, SW_PREORDER, ...).</item>
///   <item><c>UnlockedSpecials</c> - special product IDs (BANNER_*, SPEC_XOHELMET, ...). The game
///         manages hundreds of entries in this list, so writes preserve unmanaged values.</item>
///   <item><c>SeenTechnologies</c> - technology product IDs. Used by the special reward
///         collection path for technology entitlements (e.g. ENT_BOLTCASTER -> BOLT_SM).</item>
/// </list>
/// On macOS there is no accountdata.hg; this file is the account data source
/// (~/Library/Application Support/HelloGames/NMS/SETTINGS/GcUserSettingsData.mxml).
/// </summary>
internal static class MxmlRewardEditor
{
    private const string PropertyElementName = "Property";
    private const string SeasonRewardPropertyName = "UnlockedSeasonRewards";
    private const string TwitchRewardPropertyName = "UnlockedTwitchRewards";
    private const string PlatformRewardPropertyName = "UnlockedPlatformRewards";
    private const string SpecialsPropertyName = "UnlockedSpecials";
    private const string SeenTechnologiesPropertyName = "SeenTechnologies";
    private const string MxmlFileName = "GCUSERSETTINGSDATA.MXML";

    /// <summary>
    /// The expected relative path from the Steam install directory to the MXML settings file.
    /// </summary>
    internal const string SteamRelativePath = @"steamapps\common\No Man's Sky\Binaries\SETTINGS\" + MxmlFileName;

    /// <summary>
    /// Attempts to auto-detect the full path to GCUSERSETTINGSDATA.MXML via Steam registry.
    /// </summary>
    /// <returns>The full path if found and the file exists, otherwise null.</returns>
    internal static string? AutoDetectMxmlPath() => FindMxmlPath();

    /// <summary>
    /// Finds the GcUserSettingsData MXML file for the current platform. The save directory
    /// is checked first (macOS stores SETTINGS as a sibling of the profile folder), then the
    /// platform-specific locations (Steam install on Windows, Application Support on macOS,
    /// default Steam libraries on Linux). File name matching is case-insensitive because the
    /// game uses GCUSERSETTINGSDATA.MXML on Windows and GcUserSettingsData.mxml on macOS.
    /// </summary>
    /// <param name="saveDirectory">The loaded save profile directory, or null.</param>
    /// <returns>The full path to the MXML file, or null when not found.</returns>
    internal static string? FindMxmlPath(string? saveDirectory = null)
    {
        foreach (string settingsDir in EnumerateSettingsDirectories(saveDirectory))
        {
            string? path = FindMxmlInDirectory(settingsDir);
            if (path != null)
                return path;
        }
        return null;
    }

    /// <summary>Enumerates candidate SETTINGS directories in priority order.</summary>
    private static IEnumerable<string> EnumerateSettingsDirectories(string? saveDirectory)
    {
        // 1. Sibling of the save profile: <NMS>/<profile>/../SETTINGS (macOS layout).
        if (!string.IsNullOrEmpty(saveDirectory))
        {
            string trimmed = saveDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrEmpty(parent))
                yield return Path.Combine(parent, "SETTINGS");
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 2. Windows: the Steam install's Binaries\SETTINGS folder.
        if (OperatingSystem.IsWindows())
        {
            string? steamPath = GetSteamInstallPath();
            if (!string.IsNullOrEmpty(steamPath))
                yield return Path.Combine(steamPath, SteamRelativePath);
        }

        // 3. macOS: ~/Library/Application Support/HelloGames/NMS/SETTINGS
        if (OperatingSystem.IsMacOS())
        {
            yield return Path.Combine(home, "Library", "Application Support", "HelloGames", "NMS", "SETTINGS");
        }

        // 4. Linux/Proton: default Steam libraries and the Flatpak data directory.
        if (OperatingSystem.IsLinux())
        {
            yield return Path.Combine(home, ".local", "share", "Steam", "steamapps", "common",
                "No Man's Sky", "Binaries", "SETTINGS");
            yield return Path.Combine(home, ".steam", "steam", "steamapps", "common",
                "No Man's Sky", "Binaries", "SETTINGS");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam",
                "steamapps", "common", "No Man's Sky", "Binaries", "SETTINGS");
        }
    }

    /// <summary>Finds the MXML file inside a directory using a case-insensitive name match.</summary>
    private static string? FindMxmlInDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(file), MxmlFileName, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        catch
        {
            // Ignore inaccessible directories.
        }
        return null;
    }

    /// <summary>
    /// Reads the Steam install path from the Windows registry.
    /// </summary>
    private static string? GetSteamInstallPath()
    {
        // On non-Windows platforms (build/test), registry is not available.
        if (!OperatingSystem.IsWindows())
            return null;

        return ReadRegistryValue(@"SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath")
            ?? ReadRegistryValue(@"SOFTWARE\Valve\Steam", "InstallPath");
    }

    /// <summary>
    /// Reads a string value from the Windows registry (HKLM).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? ReadRegistryValue(string subKey, string valueName)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the currently unlocked season reward IDs from the MXML file.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of reward value strings (with ^ prefix to match RewardDatabase IDs), or empty set on error.</returns>
    internal static HashSet<string> ReadUnlockedSeasonRewards(string mxmlPath) =>
        ReadRewardValues(mxmlPath, SeasonRewardPropertyName);

    /// <summary>
    /// Reads the currently unlocked Twitch reward IDs from the MXML file.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of reward value strings (with ^ prefix to match RewardDatabase IDs), or empty set on error.</returns>
    internal static HashSet<string> ReadUnlockedTwitchRewards(string mxmlPath) =>
        ReadRewardValues(mxmlPath, TwitchRewardPropertyName);

    /// <summary>
    /// Reads the currently unlocked platform reward IDs from the MXML file.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of reward value strings (with ^ prefix to match RewardDatabase IDs), or empty set on error.</returns>
    internal static HashSet<string> ReadUnlockedRewards(string mxmlPath) =>
        ReadRewardValues(mxmlPath, PlatformRewardPropertyName);

    /// <summary>
    /// Reads the currently unlocked special product IDs from the MXML file.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of product ID strings (with ^ prefix to match RewardDatabase ProductIds), or empty set on error.</returns>
    internal static HashSet<string> ReadUnlockedSpecials(string mxmlPath) =>
        ReadRewardValues(mxmlPath, SpecialsPropertyName);

    /// <summary>
    /// Reads the currently seen technology product IDs from the MXML file.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <returns>A set of product ID strings (with ^ prefix), or empty set on error.</returns>
    internal static HashSet<string> ReadSeenTechnologies(string mxmlPath) =>
        ReadRewardValues(mxmlPath, SeenTechnologiesPropertyName);

    /// <summary>
    /// Reads the values of a reward list container from the MXML file.
    /// Supports the correct nested format (child entries inside a container element)
    /// as well as legacy flat format for backwards compatibility.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="propertyName">The Property name of the list to read.</param>
    /// <returns>A set of value strings with a caret prefix added, or empty set on error.</returns>
    private static HashSet<string> ReadRewardValues(string mxmlPath, string propertyName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(mxmlPath) || !File.Exists(mxmlPath))
            return result;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            if (doc.Root == null) return result;

            foreach (var prop in doc.Root.Descendants(PropertyElementName))
            {
                var nameAttr = prop.Attribute("name");
                var valueAttr = prop.Attribute("value");
                if (nameAttr?.Value == propertyName && valueAttr != null
                    && !string.IsNullOrEmpty(valueAttr.Value))
                {
                    // Store as ^VALUE to match the reward database ID format
                    string val = valueAttr.Value;
                    if (!val.StartsWith('^'))
                        val = "^" + val;
                    result.Add(val);
                }
            }
        }
        catch
        {
            // Graceful failure - return what we have
        }

        return result;
    }

    /// <summary>
    /// Writes unlocked platform rewards to the MXML file. Managed IDs passed with
    /// false are removed; unmanaged entries are preserved.
    /// Uses the correct nested format with a container Property element wrapping child entries:
    /// <code>
    ///   &lt;Property name="UnlockedPlatformRewards"&gt;
    ///     &lt;Property name="UnlockedPlatformRewards" value="SW_PREORDER" _index="0" /&gt;
    ///   &lt;/Property&gt;
    /// </code>
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The reward IDs (with ^ prefix) and their unlock state.</param>
    /// <returns>True if the file was written successfully, false otherwise.</returns>
    internal static bool WriteUnlockedRewards(string mxmlPath, List<(string Id, bool Unlocked)> rewards)
    {
        var rows = new List<(string Id, bool Present)>(rewards.Count);
        foreach (var (id, unlocked) in rewards)
            rows.Add((id, unlocked));
        return WriteManagedRewardValues(mxmlPath, PlatformRewardPropertyName, rows);
    }

    /// <summary>
    /// Writes special product IDs to the <c>UnlockedSpecials</c> list. Managed IDs
    /// passed with false are removed; all game-managed entries are preserved.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The product IDs and their desired presence state.</param>
    /// <returns>True if the file was written successfully, false otherwise.</returns>
    internal static bool WriteUnlockedSpecials(string mxmlPath, List<(string Id, bool Present)> rewards) =>
        WriteManagedRewardValues(mxmlPath, SpecialsPropertyName, rewards);

    /// <summary>
    /// Writes technology product IDs to the <c>SeenTechnologies</c> list. Managed IDs
    /// passed with false are removed; all other entries are preserved.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The product IDs and their desired presence state.</param>
    /// <returns>True if the file was written successfully, false otherwise.</returns>
    internal static bool WriteSeenTechnologies(string mxmlPath, List<(string Id, bool Present)> rewards) =>
        WriteManagedRewardValues(mxmlPath, SeenTechnologiesPropertyName, rewards);

    /// <summary>
    /// Writes a managed reward list to the MXML file, preserving entries that are not
    /// managed by the caller. Managed IDs that are no longer present are removed,
    /// newly-present managed IDs are appended, and all entries are re-indexed
    /// sequentially so the game can deserialise the list correctly.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="propertyName">The Property name of the list to write.</param>
    /// <param name="rewards">The managed IDs with their desired presence state (may include ^ prefix).</param>
    /// <returns>True if the file was written successfully, false otherwise.</returns>
    private static bool WriteManagedRewardValues(string mxmlPath, string propertyName,
        List<(string Id, bool Present)> rewards)
    {
        if (string.IsNullOrEmpty(mxmlPath) || !File.Exists(mxmlPath))
            return false;

        try
        {
            var doc = XDocument.Load(mxmlPath);
            if (doc.Root == null) return false;

            var container = FindOrCreateRewardContainer(doc.Root, propertyName);

            // Build the managed and desired sets, stripping the ^ prefix for MXML values.
            var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var desired = new List<string>();
            foreach (var (id, present) in rewards)
            {
                if (string.IsNullOrEmpty(id)) continue;
                string val = id.StartsWith('^') ? id[1..] : id;
                if (!managed.Add(val)) continue;
                if (present) desired.Add(val);
            }
            var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);

            // Remove managed entries that should no longer be present, preserving
            // every unmanaged entry in the container.
            var children = container.Elements(PropertyElementName)
                .Where(e => e.Attribute("name")?.Value == propertyName)
                .ToList();
            foreach (var child in children)
            {
                string? val = child.Attribute("value")?.Value;
                if (string.IsNullOrEmpty(val)) continue;
                if (managed.Contains(val) && !desiredSet.Contains(val))
                    child.Remove();
            }

            // Append newly-present managed entries that are not already in the container.
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in container.Elements(PropertyElementName))
            {
                string? val = child.Attribute("value")?.Value;
                if (!string.IsNullOrEmpty(val)) current.Add(val);
            }
            foreach (var val in desired)
            {
                if (current.Contains(val)) continue;
                container.Add(new XElement(PropertyElementName,
                    new XAttribute("name", propertyName),
                    new XAttribute("value", val)));
                current.Add(val);
            }

            // Re-index all entries sequentially (the game expects _index to match position).
            int index = 0;
            foreach (var child in container.Elements(PropertyElementName))
                child.SetAttributeValue("_index", (index++).ToString(CultureInfo.InvariantCulture));

            doc.Save(mxmlPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the container &lt;Property name="..."&gt; element that holds the child reward
    /// entries, or creates one if it doesn't exist. Also handles migrating legacy flat
    /// format (entries as direct children of root) into the correct nested container format.
    /// </summary>
    /// <param name="root">The MXML document root element.</param>
    /// <param name="propertyName">The Property name of the list.</param>
    private static XElement FindOrCreateRewardContainer(XElement root, string propertyName)
    {
        // Look for a container element: a Property with the matching name
        // that does NOT have a value attribute (i.e. it's a parent container, not an entry).
        var container = root.Elements(PropertyElementName)
            .FirstOrDefault(e => e.Attribute("name")?.Value == propertyName
                              && e.Attribute("value") == null);

        if (container != null)
            return container;

        // Handle legacy flat format: remove any flat entries from root before
        // creating the container (they will be re-added as children).
        var legacyFlat = root.Elements(PropertyElementName)
            .Where(e => e.Attribute("name")?.Value == propertyName
                     && e.Attribute("value") != null)
            .ToList();
        foreach (var el in legacyFlat)
            el.Remove();

        // Create a new container element
        container = new XElement(PropertyElementName,
            new XAttribute("name", propertyName));
        root.Add(container);
        return container;
    }

    /// <summary>
    /// Reads unlocked rewards from MXML and writes rewards back in a single operation.
    /// Convenience method for save operations.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="rewards">The reward rows from the grid.</param>
    /// <returns>True if save succeeded or was skipped (no path), false on error.</returns>
    internal static bool SyncPlatformRewards(string? mxmlPath, List<(string Id, bool Unlocked)> rewards)
    {
        if (string.IsNullOrEmpty(mxmlPath))
            return true; // Gracefully skip if no file selected

        return WriteUnlockedRewards(mxmlPath, rewards);
    }

    /// <summary>
    /// Writes a managed reward list to the MXML file in a single operation.
    /// Convenience method for save operations.
    /// </summary>
    /// <param name="mxmlPath">Full path to GCUSERSETTINGSDATA.MXML.</param>
    /// <param name="propertyName">The Property name of the list to write.</param>
    /// <param name="rewards">The managed IDs with their desired presence state.</param>
    /// <returns>True if save succeeded or was skipped (no path), false on error.</returns>
    internal static bool SyncManagedRewards(string? mxmlPath, string propertyName,
        List<(string Id, bool Present)> rewards)
    {
        if (string.IsNullOrEmpty(mxmlPath))
            return true; // Gracefully skip if no file selected

        return WriteManagedRewardValues(mxmlPath, propertyName, rewards);
    }
}
