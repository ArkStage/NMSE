using NMSE.Models;

namespace NMSE.Core;

/// <summary>
/// Base-list classification, filtering, sorting and block reordering shared by the
/// Bases panel and the 2.0 Avalonia view. No UI dependencies.
/// </summary>
internal static class BaseListLogic
{
    /// <summary>The kinds of entry found in <c>PersistentPlayerBases</c>.</summary>
    internal enum BaseListKind
    {
        /// <summary>Planetary base with a base computer.</summary>
        Home,
        /// <summary>Freighter base.</summary>
        Freighter,
        /// <summary>Space station base (Cosmos).</summary>
        SpaceStation,
        /// <summary>Corvette base (<c>PlayerShipBase</c>).</summary>
        Corvette,
        /// <summary>Space / asteroid base (<c>PlayerSpaceBase</c>).</summary>
        SpaceBase,
        /// <summary>Temporary ship base (<c>UITempShipBase</c>).</summary>
        TemporaryShip,
        /// <summary>Base written by an older game format (<c>BaseVersion</c> below 3).</summary>
        Legacy,
        /// <summary>Unrecognised or unreadable entry.</summary>
        Other
    }

    /// <summary>One row of the base list.</summary>
    internal sealed class BaseRow
    {
        /// <summary>Creates a row.</summary>
        public BaseRow(int dataIndex, JsonObject? data, string name, string typeName,
            BaseListKind kind, int version, int objectCount, bool nameEditable)
        {
            DataIndex = dataIndex;
            Data = data;
            Name = name;
            TypeName = typeName;
            Kind = kind;
            Version = version;
            ObjectCount = objectCount;
            NameEditable = nameEditable;
        }

        /// <summary>Gets the entry's index in <c>PersistentPlayerBases</c>.</summary>
        public int DataIndex { get; }

        /// <summary>Gets the underlying JSON object, or null when the entry is unreadable.</summary>
        public JsonObject? Data { get; }

        /// <summary>Gets the resolved base name (empty when the save has none).</summary>
        public string Name { get; }

        /// <summary>Gets the raw base type name (e.g. <c>HomePlanetBase</c>).</summary>
        public string TypeName { get; }

        /// <summary>Gets the classified base kind.</summary>
        public BaseListKind Kind { get; }

        /// <summary>Gets the base format version.</summary>
        public int Version { get; }

        /// <summary>Gets the number of objects in the base.</summary>
        public int ObjectCount { get; }

        /// <summary>Gets whether the entry's name can be edited.</summary>
        public bool NameEditable { get; }

        /// <summary>Gets whether the entry could be read at all.</summary>
        public bool IsReadable => Data != null;
    }

    /// <summary>Gets the raw base type name of an entry.</summary>
    internal static string GetTypeName(JsonObject baseObj) =>
        baseObj.GetString("BaseType.PersistentBaseTypes") ?? baseObj.GetString("BaseType") ?? "";

    /// <summary>Gets the base format version, or 0 when absent.</summary>
    internal static int GetVersion(JsonObject baseObj)
    {
        try
        {
            return baseObj.GetInt("BaseVersion");
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Classifies a base entry, including temporary and legacy types.</summary>
    internal static BaseListKind Classify(JsonObject baseObj)
    {
        // Entries written by an older format are kept visible but treated as legacy,
        // regardless of their type name.
        if (GetVersion(baseObj) < 3)
            return BaseListKind.Legacy;

        var kind = SpaceStationLogic.GetBaseKind(baseObj);
        if (kind == SpaceStationLogic.BaseKind.Home) return BaseListKind.Home;
        if (kind == SpaceStationLogic.BaseKind.Freighter) return BaseListKind.Freighter;
        if (kind == SpaceStationLogic.BaseKind.SpaceStation) return BaseListKind.SpaceStation;
        if (kind == SpaceStationLogic.BaseKind.Corvette) return BaseListKind.Corvette;
        if (kind == SpaceStationLogic.BaseKind.SpaceBase) return BaseListKind.SpaceBase;

        if (GetTypeName(baseObj).Equals("UITempShipBase", StringComparison.Ordinal))
            return BaseListKind.TemporaryShip;
        return BaseListKind.Other;
    }

    /// <summary>
    /// Resolves the display name of a base: the freighter name for freighter bases,
    /// the owning ship's name for corvettes, otherwise the base's own name.
    /// </summary>
    internal static string ResolveName(JsonObject playerState, JsonObject baseObj, BaseListKind kind, int index)
    {
        if (kind == BaseListKind.Freighter)
            return playerState.GetString("PlayerFreighterName") ?? "";
        if (kind == BaseListKind.Corvette)
            return SpaceStationLogic.ResolveCorvetteBaseName(playerState, baseObj) ?? baseObj.GetString("Name") ?? "";
        return baseObj.GetString("Name") ?? "";
    }

    /// <summary>
    /// Builds the rows for the base list. Temporary, legacy and other entries are
    /// included (and marked) unless <paramref name="includeHidden"/> is false.
    /// </summary>
    /// <param name="playerState">The save's PlayerStateData object.</param>
    /// <param name="bases">The PersistentPlayerBases array, or null.</param>
    /// <param name="filter">Search term matched against name, type and kind.</param>
    /// <param name="includeHidden">False to hide temporary/legacy/other entries.</param>
    /// <param name="kindLabel">Optional localiser used for the kind label in searches.</param>
    internal static List<BaseRow> BuildRows(
        JsonObject playerState,
        JsonArray? bases,
        string? filter,
        bool includeHidden,
        Func<BaseListKind, string>? kindLabel = null)
    {
        var rows = new List<BaseRow>();
        if (bases == null)
            return rows;

        string term = (filter ?? "").Trim();
        for (int i = 0; i < bases.Length; i++)
        {
            JsonObject? baseObj = null;
            try
            {
                baseObj = bases.GetObject(i);
            }
            catch
            {
            }

            if (baseObj == null)
            {
                if (includeHidden && term.Length == 0)
                    rows.Add(new BaseRow(i, null, "", "", BaseListKind.Other, 0, 0, nameEditable: false));
                continue;
            }

            var kind = Classify(baseObj);
            bool hidden = kind is BaseListKind.TemporaryShip or BaseListKind.Legacy or BaseListKind.Other;
            if (hidden && !includeHidden)
                continue;

            var typeName = GetTypeName(baseObj);
            var name = ResolveName(playerState, baseObj, kind, i);
            if (term.Length > 0 && !Matches(name, typeName, kind, term, kindLabel))
                continue;

            int version = GetVersion(baseObj);
            int objectCount = 0;
            try
            {
                objectCount = baseObj.GetArray("Objects")?.Length ?? 0;
            }
            catch
            {
            }

            bool nameEditable = kind is BaseListKind.Home or BaseListKind.SpaceStation or BaseListKind.SpaceBase;
            rows.Add(new BaseRow(i, baseObj, name, typeName, kind, version, objectCount, nameEditable));
        }
        return rows;
    }

    private static bool Matches(
        string name,
        string typeName,
        BaseListKind kind,
        string term,
        Func<BaseListKind, string>? kindLabel)
    {
        if (name.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        if (typeName.Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        if (kind.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
            return true;
        string? label = kindLabel?.Invoke(kind);
        return label != null && label.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves the selected entries as one block to the given insertion position in the
    /// original array, preserving the block's internal order.
    /// </summary>
    /// <param name="bases">The PersistentPlayerBases array.</param>
    /// <param name="indices">Current array indices of the entries to move.</param>
    /// <param name="targetIndex">Insertion position in the original array (0..Length).</param>
    /// <returns>The new array indices occupied by the moved block.</returns>
    internal static int[] MoveBlock(JsonArray bases, IReadOnlyList<int> indices, int targetIndex)
    {
        var selected = indices
            .Where(i => i >= 0 && i < bases.Length)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (selected.Count == 0)
            return Array.Empty<int>();

        if (targetIndex < 0) targetIndex = 0;
        if (targetIndex > bases.Length) targetIndex = bases.Length;

        var items = new List<object?>(bases.Length);
        for (int i = 0; i < bases.Length; i++)
            items.Add(bases.Get(i));

        var selectedSet = new HashSet<int>(selected);
        var moving = new List<object?>(selected.Count);
        foreach (int index in selected)
            moving.Add(items[index]);

        var remaining = new List<object?>(items.Count - selected.Count);
        int insertAt = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (selectedSet.Contains(i))
                continue;
            if (i < targetIndex)
                insertAt++;
            remaining.Add(items[i]);
        }

        remaining.InsertRange(insertAt, moving);
        for (int i = 0; i < remaining.Count; i++)
            bases.Set(i, remaining[i]);

        var result = new int[moving.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = insertAt + i;
        return result;
    }

    /// <summary>
    /// Sorts every entry in the array by resolved name, keeping the array length and
    /// entries intact. Empty names sort last in ascending order.
    /// </summary>
    internal static void SortByName(JsonObject playerState, JsonArray bases, bool descending)
    {
        if (bases.Length < 2)
            return;

        var entries = new List<(object? Item, string Name, int Index)>(bases.Length);
        for (int i = 0; i < bases.Length; i++)
        {
            object? raw = null;
            try
            {
                raw = bases.Get(i);
            }
            catch
            {
            }

            string name = raw is JsonObject obj
                ? ResolveName(playerState, obj, Classify(obj), i)
                : "";
            entries.Add((raw, name, i));
        }

        // Empty names sort last in both directions; the original index keeps the sort stable.
        var ordered = descending
            ? entries
                .OrderBy(e => e.Name.Length == 0 ? 1 : 0)
                .ThenByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Index)
                .ToList()
            : entries
                .OrderBy(e => e.Name.Length == 0 ? 1 : 0)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Index)
                .ToList();

        for (int i = 0; i < ordered.Count; i++)
            bases.Set(i, ordered[i].Item);
    }
}
