using System.Globalization;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Util;

namespace NMSE.UI.Controls;

/// <summary>
/// Shared chrome for the catalogue completion sub-tabs: a row grid with a
/// per-row checkbox, a completion counter and Complete All / Clear All buttons.
/// Subclasses populate rows and implement the apply/clear semantics.
/// </summary>
internal abstract class CompletionGridPanel : UserControl
{
    /// <summary>Raised when the subclass changes save data so the host can mark it dirty.</summary>
    public event EventHandler? DataModified;

    /// <summary>Grid holding the completion rows.</summary>
    protected DataGridView Grid { get; }

    /// <summary>Counter label showing completion progress.</summary>
    protected Label CounterLabel { get; }

    /// <summary>Completes every missing entry.</summary>
    protected Button CompleteAllButton { get; }

    /// <summary>Clears every completed entry.</summary>
    protected Button ClearAllButton { get; }

    /// <summary>Current save root (for DiscoveryManagerData edits).</summary>
    protected JsonObject? SaveRoot { get; private set; }

    /// <summary>Current PlayerStateData.</summary>
    protected JsonObject? PlayerState { get; private set; }

    /// <summary>Catalogue database with the verified completion data.</summary>
    protected CatalogueDatabase? Catalogue { get; private set; }

    /// <summary>Item database used for names and icons.</summary>
    protected GameItemDatabase? Database { get; private set; }

    /// <summary>Icon manager used for item icons.</summary>
    protected IconManager? IconManager { get; private set; }

    private bool _updatingCheckboxes;
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Bitmap PlaceholderIcon = new(24, 24);
    private readonly TextBox _filterBox;

    /// <summary>Creates the shared grid, counter and buttons.</summary>
    protected CompletionGridPanel()
    {
        Dock = DockStyle.Fill;

        Grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            RowTemplate = { Height = 26 },
        };
        Grid.CellValueChanged += OnGridCellValueChanged;
        Grid.CellValidating += OnGridCellValidating;
        Grid.CellEndEdit += OnGridCellEndEdit;
        Grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (Grid.IsCurrentCellDirty) Grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        CounterLabel = new Label
        {
            AutoSize = true,
            Padding = new Padding(0, 8, 8, 0),
            Font = FontManager.CreateFont(9, FontStyle.Bold),
        };

        CompleteAllButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        CompleteAllButton.Click += (_, _) => OnCompleteAllRequested();
        ClearAllButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        ClearAllButton.Click += (_, _) => OnClearAllRequested();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        buttonPanel.Controls.Add(CounterLabel);
        buttonPanel.Controls.Add(CompleteAllButton);
        buttonPanel.Controls.Add(ClearAllButton);

        _filterBox = new TextBox
        {
            Width = 200,
            MinimumSize = new Size(0, 25),
            PlaceholderText = UiStrings.Get("discovery.filter_items"),
        };
        _filterBox.TextChanged += (_, _) => ApplyGridFilter();

        var filterClearButton = new Button
        {
            Text = "X",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(28, 25),
        };
        filterClearButton.Click += (_, _) => _filterBox.Text = string.Empty;

        var filterFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };
        filterFlow.Controls.Add(_filterBox);
        filterFlow.Controls.Add(filterClearButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(filterFlow, 0, 0);
        layout.Controls.Add(Grid, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(layout);

        // Set the base chrome text directly: ApplyUiLocalisation is virtual and
        // derived fields are not initialised yet at this point.
        CompleteAllButton.Text = UiStrings.Get("discovery.complete_all");
        ClearAllButton.Text = UiStrings.Get("discovery.clear_all");
    }

    /// <summary>Supplies the save data and catalogue database. Call on every save load.</summary>
    public void LoadData(JsonObject saveData, CatalogueDatabase? catalogue)
    {
        SaveRoot = saveData;
        PlayerState = saveData.GetObject("PlayerStateData");
        Catalogue = catalogue;
        Reload();
    }

    /// <summary>Sets the item database used for names and icons.</summary>
    public void SetDatabase(GameItemDatabase? database)
    {
        Database = database;
        Reload();
    }

    /// <summary>Sets the icon manager used for item icons.</summary>
    public void SetIconManager(IconManager? iconManager)
    {
        IconManager = iconManager;
        Reload();
    }

    /// <summary>Refreshes localised button and counter text.</summary>
    public virtual void ApplyUiLocalisation()
    {
        CompleteAllButton.Text = UiStrings.Get("discovery.complete_all");
        ClearAllButton.Text = UiStrings.Get("discovery.clear_all");
        _filterBox.PlaceholderText = UiStrings.Get("discovery.filter_items");
    }

    /// <summary>Releases grid rows and references held by the panel.</summary>
    public virtual void PurgeData()
    {
        _updatingCheckboxes = true;
        Grid.Rows.Clear();
        _updatingCheckboxes = false;
        foreach (var image in _iconCache.Values)
            image.Dispose();
        _iconCache.Clear();
        SaveRoot = null;
        PlayerState = null;
    }

    /// <summary>Rebuilds the grid rows and refreshes the counter.</summary>
    public virtual void Reload()
    {
        _updatingCheckboxes = true;
        try
        {
            Grid.Rows.Clear();
            if (PlayerState != null && Catalogue != null)
                PopulateRows();
        }
        finally
        {
            _updatingCheckboxes = false;
        }
        ApplyGridFilter();
        RefreshCounter();
    }

    /// <summary>Populates the grid rows for the current save state.</summary>
    protected abstract void PopulateRows();

    /// <summary>Returns the number of completed and total entries.</summary>
    protected abstract (int Have, int Total) GetCompletion();

    /// <summary>Completes every missing entry. Returns the number of changes.</summary>
    protected abstract int CompleteAll();

    /// <summary>Clears every completed entry. Returns the number of changes.</summary>
    protected abstract int ClearAll();

    /// <summary>Fills or clears the row identified by <paramref name="row"/>.</summary>
    protected abstract bool ToggleRow(DataGridViewRow row, bool complete);

    /// <summary>
    /// Returns the allowed progress range for a row, or null when the row's progress
    /// cannot be edited directly.
    /// </summary>
    protected virtual (int Min, int Max)? GetProgressRange(DataGridViewRow row) => null;

    /// <summary>Applies an explicit progress value to a row. Returns true when changed.</summary>
    protected virtual bool TryApplyProgress(DataGridViewRow row, int value) => false;

    /// <summary>Returns the scaled icon for an item ID, or the shared placeholder.</summary>
    protected Image GetItemIcon(string itemId)
    {
        if (IconManager == null || string.IsNullOrEmpty(itemId)) return PlaceholderIcon;
        if (_iconCache.TryGetValue(itemId, out var cached)) return cached;

        var icon = IconManager.GetIconForItem(itemId, Database);
        if (icon == null) return PlaceholderIcon;

        try
        {
            var scaled = new Bitmap(24, 24);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(icon, 0, 0, 24, 24);
            }
            _iconCache[itemId] = scaled;
            return scaled;
        }
        catch
        {
            return PlaceholderIcon;
        }
    }

    /// <summary>Marks a row's Progress cell as read-only when it has no editable range.</summary>
    protected void ApplyProgressEditability(DataGridViewRow row)
    {
        if (Grid.Columns["Progress"] is not DataGridViewColumn progress) return;
        if (GetProgressRange(row) is null) row.Cells[progress.Index].ReadOnly = true;
    }

    /// <summary>Creates a read-only text column.</summary>
    protected static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, float weight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            ReadOnly = true,
            FillWeight = weight,
        };
    }

    /// <summary>Creates the leading checkbox column.</summary>
    protected static DataGridViewCheckBoxColumn CreateCheckColumn()
    {
        return new DataGridViewCheckBoxColumn
        {
            Name = "Done",
            HeaderText = string.Empty,
            FillWeight = 8,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 34,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
    }

    /// <summary>Refreshes the counter label from <see cref="GetCompletion"/>.</summary>
    protected virtual void RefreshCounter()
    {
        if (Catalogue == null || PlayerState == null)
        {
            CounterLabel.Text = string.Empty;
            CompleteAllButton.Enabled = false;
            ClearAllButton.Enabled = false;
            return;
        }

        var (have, total) = GetCompletion();
        int percent = total <= 0 ? 100 : (int)Math.Round(have * 100.0 / total, MidpointRounding.AwayFromZero);
        CounterLabel.Text = UiStrings.Format("discovery.completion_counter", have, total, percent);
        CompleteAllButton.Enabled = total > 0;
        ClearAllButton.Enabled = total > 0;
    }

    /// <summary>Hides rows that do not contain the current filter text.</summary>
    private void ApplyGridFilter()
    {
        string filter = _filterBox.Text.Trim();
        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (filter.Length == 0)
            {
                row.Visible = true;
                continue;
            }

            bool visible = false;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell is DataGridViewCheckBoxCell || cell is DataGridViewImageCell) continue;
                if (cell.Value is string text && text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    visible = true;
                    break;
                }
            }
            row.Visible = visible;
        }
    }

    /// <summary>Notifies the host that save data changed.</summary>
    protected void RaiseDataModified() => DataModified?.Invoke(this, EventArgs.Empty);

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingCheckboxes || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (Grid.Columns[e.ColumnIndex].Name != "Done") return;

        var row = Grid.Rows[e.RowIndex];
        bool complete = row.Cells["Done"].Value is true;
        if (ToggleRow(row, complete))
        {
            RefreshCounter();
            RaiseDataModified();
            ScheduleReload();
        }
    }

    private void OnGridCellValidating(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_updatingCheckboxes || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (Grid.Columns[e.ColumnIndex].Name != "Progress") return;

        var row = Grid.Rows[e.RowIndex];
        var range = GetProgressRange(row);
        if (range is null)
        {
            e.Cancel = true;
            return;
        }

        string text = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? "";
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value >= range.Value.Min && value <= range.Value.Max)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(this,
            UiStrings.Format("discovery.invalid_progress", range.Value.Min, range.Value.Max),
            UiStrings.Get("discovery.add_all_missing_title"),
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OnGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_updatingCheckboxes || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (Grid.Columns[e.ColumnIndex].Name != "Progress") return;

        var row = Grid.Rows[e.RowIndex];
        string text = Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? "";
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return;
        if (!TryApplyProgress(row, value)) return;

        RefreshCounter();
        RaiseDataModified();
        ScheduleReload();
    }

    /// <summary>Defers a grid rebuild until the current cell edit has completed.</summary>
    private void ScheduleReload()
    {
        if (IsHandleCreated && Grid.IsHandleCreated)
            BeginInvoke(new Action(ReloadPreservingScroll));
        else
            ReloadPreservingScroll();
    }

    private void ReloadPreservingScroll()
    {
        int firstRow = -1;
        try { firstRow = Grid.FirstDisplayedScrollingRowIndex; } catch (InvalidOperationException) { }
        Reload();
        if (firstRow <= 0 || firstRow >= Grid.Rows.Count) return;
        try { Grid.FirstDisplayedScrollingRowIndex = firstRow; } catch (InvalidOperationException) { }
    }

    /// <summary>Prompts for confirmation before a bulk action.</summary>
    protected bool Confirm(string messageKey, int count)
    {
        return MessageBox.Show(this,
            UiStrings.Format(messageKey, count),
            UiStrings.Get("discovery.add_all_missing_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    private void OnCompleteAllRequested()
    {
        var (have, total) = GetCompletion();
        int missing = total - have;
        if (missing > 0 && !Confirm("discovery.complete_all_confirm", missing)) return;

        // Always attempt the fix, even when the counter already reads complete: older saves
        // can hold values the editor reads as complete but the game rejects (for example the
        // -1 page stats that the in-game Collected Knowledge counter credits as zero).
        int changed = CompleteAll();
        Reload();
        if (changed > 0)
        {
            RaiseDataModified();
            return;
        }

        if (missing <= 0)
        {
            MessageBox.Show(this, UiStrings.Get("discovery.add_all_missing_none"),
                UiStrings.Get("discovery.add_all_missing_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OnClearAllRequested()
    {
        var (have, _) = GetCompletion();
        if (have <= 0)
        {
            MessageBox.Show(this, UiStrings.Get("discovery.clear_all_none"),
                UiStrings.Get("discovery.add_all_missing_title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Confirm("discovery.clear_all_confirm", have)) return;

        int changed = ClearAll();
        Reload();
        if (changed > 0) RaiseDataModified();
    }
}
