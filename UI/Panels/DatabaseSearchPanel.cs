using System.Globalization;
using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Util;

namespace NMSE.UI.Panels;

/// <summary>
/// Database Search panel: searches the whole item database (every entry loaded
/// from the Resources/json files) by id, name, description, category and other
/// fields, with substring and <c>*</c>/<c>?</c> wildcard matching. Shows a
/// virtualised result list with a details pane and item icon. This panel is a
/// read-only lookup tool, is independent of any loaded save and never raises
/// data-modification events.
/// </summary>
internal sealed partial class DatabaseSearchPanel : UserControl
{
    private static readonly Bitmap PlaceholderIcon = new(24, 24);
    private static readonly Bitmap DetailPlaceholderIcon = new(72, 72);

    private GameItemDatabase? _database;
    private IconManager? _iconManager;
    private readonly List<GameItem> _allItems = new();
    private readonly List<GameItem> _filteredItems = new();
    private bool _updatingSelection;
    private bool _splitInitialised;

    public DatabaseSearchPanel()
    {
        InitializeComponent();
        SetupLayout();
        ApplyUiLocalisation();
        ApplyFilter();
    }

    /// <summary>
    /// Sets the shared item database and rebuilds the browsable list. Safe to
    /// call with <c>null</c> (shows an empty database message).
    /// </summary>
    public void SetDatabase(GameItemDatabase? database)
    {
        _database = database;
        RebuildItemList();
        PopulateSourceFilter();
        ApplyFilter();
    }

    /// <summary>Sets the shared icon manager used for result and detail icons.</summary>
    public void SetIconManager(IconManager? iconManager)
    {
        _iconManager = iconManager;
        _resultsGrid.Invalidate();
        ShowDetails(GetSelectedItem());
    }

    /// <summary>Applies localised text to all panel controls.</summary>
    public void ApplyUiLocalisation()
    {
        _searchBox.PlaceholderText = UiStrings.Get("db_search.placeholder");
        _hintLabel.Text = UiStrings.Get("db_search.hint");
        _sourceLabel.Text = UiStrings.Get("db_search.source_label");
        _descriptionCaption.Text = UiStrings.Get("db_search.detail_description");

        if (_resultsGrid.Columns["Name"] is DataGridViewColumn nameCol)
            nameCol.HeaderText = UiStrings.Get("discovery.col_name");
        if (_resultsGrid.Columns["Category"] is DataGridViewColumn categoryCol)
            categoryCol.HeaderText = UiStrings.Get("discovery.col_category");
        if (_resultsGrid.Columns["ID"] is DataGridViewColumn idCol)
            idCol.HeaderText = UiStrings.Get("discovery.col_id");
        if (_resultsGrid.Columns["Source"] is DataGridViewColumn sourceCol)
            sourceCol.HeaderText = UiStrings.Get("db_search.col_source");

        // Item display names are localised in place by GameItemDatabase, so the
        // sorted list and the source filter must be rebuilt after a language switch.
        RebuildItemList();
        PopulateSourceFilter();
        ApplyFilter();
    }

    /// <summary>Gets the selected item, or null when nothing is selected.</summary>
    private GameItem? GetSelectedItem()
    {
        var row = _resultsGrid.CurrentRow;
        if (row == null) return null;
        int index = row.Index;
        return index >= 0 && index < _filteredItems.Count ? _filteredItems[index] : null;
    }

    private void RebuildItemList()
    {
        _allItems.Clear();
        if (_database == null) return;

        _allItems.AddRange(_database.Items.Values);
        _allItems.Sort((a, b) =>
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void PopulateSourceFilter()
    {
        string previous = _sourceFilter.SelectedIndex > 0
            ? _sourceFilter.SelectedItem as string ?? ""
            : "";

        _sourceFilter.SelectedIndexChanged -= OnSourceFilterChanged;
        try
        {
            _sourceFilter.Items.Clear();
            _sourceFilter.Items.Add(UiStrings.Get("db_search.source_all"));

            var types = _allItems
                .Select(item => item.ItemType)
                .Where(type => !string.IsNullOrEmpty(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase);

            foreach (string type in types)
                _sourceFilter.Items.Add(type);

            int restore = 0;
            if (previous.Length > 0)
            {
                for (int i = 1; i < _sourceFilter.Items.Count; i++)
                {
                    if (_sourceFilter.Items[i] is string candidate
                        && string.Equals(candidate, previous, StringComparison.OrdinalIgnoreCase))
                    {
                        restore = i;
                        break;
                    }
                }
            }
            _sourceFilter.SelectedIndex = restore;
        }
        finally
        {
            _sourceFilter.SelectedIndexChanged += OnSourceFilterChanged;
        }
    }

    private void OnSourceFilterChanged(object? sender, EventArgs e) => ApplyFilter();

    /// <summary>
    /// Filters the browsable item list by the current query and source filter,
    /// then refreshes the virtual grid and the details pane.
    /// </summary>
    private void ApplyFilter()
    {
        var query = DatabaseSearchLogic.ParseQuery(_searchBox.Text);
        string source = _sourceFilter.SelectedIndex > 0
            ? _sourceFilter.SelectedItem as string ?? ""
            : "";

        _filteredItems.Clear();
        foreach (var item in _allItems)
        {
            if (source.Length > 0
                && !string.Equals(item.ItemType, source, StringComparison.OrdinalIgnoreCase))
                continue;

            if (DatabaseSearchLogic.MatchesItem(item, query))
                _filteredItems.Add(item);
        }

        _updatingSelection = true;
        try
        {
            _resultsGrid.RowCount = _filteredItems.Count;
            _resultsGrid.CurrentCell = _filteredItems.Count > 0
                ? _resultsGrid.Rows[0].Cells[0]
                : null;
        }
        catch (InvalidOperationException)
        {
            // The grid handle may not exist yet during initial construction.
        }
        finally
        {
            _updatingSelection = false;
        }

        _countLabel.Text = UiStrings.Format(
            "db_search.count", _filteredItems.Count.ToString("N0", CultureInfo.CurrentCulture));
        _statusLabel.Text = BuildStatusText();
        ShowDetails(_filteredItems.Count > 0 ? _filteredItems[0] : null);
        _resultsGrid.Invalidate();
    }

    private string BuildStatusText()
    {
        if (_database == null)
            return UiStrings.Get("db_search.no_database");
        if (_filteredItems.Count == 0 && _allItems.Count > 0 && !string.IsNullOrWhiteSpace(_searchBox.Text))
            return UiStrings.Get("db_search.no_results");
        return UiStrings.Format(
            "db_search.total", _allItems.Count.ToString("N0", CultureInfo.CurrentCulture));
    }

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _filteredItems.Count) return;
        var item = _filteredItems[e.RowIndex];

        switch (e.ColumnIndex)
        {
            case 0:
                e.Value = GetItemIcon(item) ?? PlaceholderIcon;
                break;
            case 1:
                e.Value = item.Name;
                break;
            case 2:
                e.Value = DatabaseSearchLogic.GetDisplayCategory(item);
                break;
            case 3:
                e.Value = item.Id;
                break;
            case 4:
                e.Value = item.ItemType;
                break;
        }
    }

    private void OnResultSelectionChanged(object? sender, EventArgs e)
    {
        if (_updatingSelection) return;
        ShowDetails(GetSelectedItem());
    }

    private Image? GetItemIcon(GameItem item)
    {
        if (_iconManager == null) return null;

        if (!string.IsNullOrEmpty(item.Icon))
        {
            var icon = _iconManager.GetIcon(item.Icon);
            if (icon != null) return icon;
        }
        return _iconManager.GetIconForItem(item.Id, _database);
    }

    /// <summary>Populates the details pane for the given item, or clears it when null.</summary>
    private void ShowDetails(GameItem? item)
    {
        if (item == null)
        {
            _detailIcon.Image = null;
            _detailName.Text = "";
            _detailId.Text = "";
            _fieldsGrid.Rows.Clear();
            _descriptionBox.Text = "";
            return;
        }

        _detailIcon.Image = GetItemIcon(item) ?? DetailPlaceholderIcon;
        _detailName.Text = item.Name;
        _detailId.Text = item.Id;

        _fieldsGrid.Rows.Clear();
        AddDetailRow("Id", item.Id);
        AddDetailRow("Name", item.Name);
        AddDetailRow("NameLower", item.NameLower);
        AddDetailRow("Group", item.Subtitle);
        AddDetailRow("Category", item.Category);
        AddDetailRow("ProductCategory", item.ProductCategory);
        AddDetailRow("SubstanceCategory", item.SubstanceCategory);
        AddDetailRow("WikiCategory", item.WikiCategory);
        AddDetailRow("TechnologyCategory", item.TechnologyCategory);
        AddDetailRow("ItemType", item.ItemType);
        AddDetailRow("SourceTable", item.SourceTable);
        AddDetailRow("Rarity", item.Rarity);
        AddDetailRow("Quality", item.Quality);
        AddDetailRow("MaxStackSize", item.MaxStackSize.ToString(CultureInfo.InvariantCulture));
        AddDetailRow("ChargeValue", item.ChargeValue.ToString(CultureInfo.InvariantCulture));
        AddDetailRow("IsChargeable", BoolText(item.IsChargeable));
        AddDetailRow("BuildFullyCharged", BoolText(item.BuildFullyCharged));
        AddDetailRow("IsCooking", BoolText(item.IsCooking));
        AddDetailRow("IsUpgrade", BoolText(item.IsUpgrade));
        AddDetailRow("IsCore", BoolText(item.IsCore));
        AddDetailRow("IsProcedural", BoolText(item.IsProcedural));
        AddDetailRow("IsCraftable", BoolText(item.IsCraftable));
        AddDetailRow("CanPickUp", BoolText(item.CanPickUp));
        AddDetailRow("IsTemporary", BoolText(item.IsTemporary));
        AddDetailRow("IsBuilding", BoolText(item.IsBuilding));
        AddDetailRow("DeploysInto", item.DeploysInto);
        AddDetailRow("BuildableShipTechID", item.BuildableShipTechID);
        AddDetailRow("TradeCategory", item.TradeCategory);
        AddDetailRow("GiveRewardOnSpecialPurchase", item.GiveRewardOnSpecialPurchase);
        AddDetailRow("NameLocStr", item.NameLocStr);
        AddDetailRow("NameLowerLocStr", item.NameLowerLocStr);
        AddDetailRow("SubtitleLocStr", item.SubtitleLocStr);
        AddDetailRow("DescriptionLocStr", item.DescriptionLocStr);
        AddDetailRow("Symbol", item.Symbol);
        AddDetailRow("Icon", item.Icon);

        _descriptionBox.Text = ControlTokens.Resolve(item.Description);
    }

    private void AddDetailRow(string field, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _fieldsGrid.Rows.Add(field, value);
    }

    private static string BoolText(bool value) => value ? "true" : "false";

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
            return true;
        }
        if (keyData == Keys.Escape && _searchBox.Focused)
        {
            _searchBox.Text = "";
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_split == null || _splitInitialised || _split.Width <= 500) return;

        // First real layout pass: give the results list about 55% of the width.
        int distance = (int)(_split.Width * 0.55);
        int max = _split.Width - _split.SplitterWidth - 200;
        if (distance > max) distance = max;
        if (distance < 200) distance = 200;
        if (distance <= 0) return;

        _split.SplitterDistance = distance;
        _splitInitialised = true;
    }
}
