using System.Globalization;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Dialogs;
using NMSE.UI.Util;

namespace NMSE.UI.Panels;

/// <summary>
/// Player panel sub-tab for editing the quick menu hotkeys (HotActions): three context
/// columns (On Foot, Ship, Exocraft) of ten key slots with action pickers and per-action
/// parameter editors. Changes are written into the save data immediately.
/// </summary>
internal sealed class HotkeysPanel : UserControl
{
    /// <summary>Combo box entry holding the action metadata and its localised label.</summary>
    private sealed record ActionItem(HotActionInfo Info, string Text)
    {
        public override string ToString() => Text;
    }

    private static readonly string[] ContextLabelKeys =
    [
        "hotkeys.context_on_foot",
        "hotkeys.context_in_ship",
        "hotkeys.context_in_exocraft"
    ];

    private static readonly string[] SlotKeys =
    [
        "Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5",
        "Ctrl+6", "Ctrl+7", "Ctrl+8", "Ctrl+9", "Ctrl+0"
    ];

    private readonly Label[] _contextLabels = new Label[HotActionsLogic.ContextCount];
    private readonly ComboBox[][] _actionCombos = new ComboBox[HotActionsLogic.ContextCount][];
    private readonly Button[][] _paramButtons = new Button[HotActionsLogic.ContextCount][];
    private readonly Button _exportButton = new();
    private readonly Button _importButton = new();
    private readonly Label _hintLabel = new();
    private readonly ToolTip _toolTip = new();

    private JsonObject? _playerState;
    private bool _loading;

    /// <summary>Raised when a hotkey slot changes so the host can mark the save dirty.</summary>
    internal event EventHandler? DataModified;

    /// <summary>Creates the panel and builds the three context columns.</summary>
    internal HotkeysPanel()
    {
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Font;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = HotActionsLogic.ContextCount,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (int c = 0; c < HotActionsLogic.ContextCount; c++)
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / HotActionsLogic.ContextCount));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (int c = 0; c < HotActionsLogic.ContextCount; c++)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = HotActionsLogic.SlotsPerContext + 2,
                Margin = Padding.Empty,
                Padding = new Padding(6, 4, 6, 4)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));

            _contextLabels[c] = new Label
            {
                Text = UiStrings.Get(ContextLabelKeys[c]),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            FontManager.ApplyHeadingFont(_contextLabels[c], 9f);
            grid.Controls.Add(_contextLabels[c], 0, 0);
            grid.SetColumnSpan(_contextLabels[c], 3);
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _actionCombos[c] = new ComboBox[HotActionsLogic.SlotsPerContext];
            _paramButtons[c] = new Button[HotActionsLogic.SlotsPerContext];
            for (int s = 0; s < HotActionsLogic.SlotsPerContext; s++)
            {
                int context = c, slot = s;
                var keyLabel = new Label
                {
                    Text = SlotKeys[s],
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 3, 4, 3)
                };
                var combo = new ComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Margin = new Padding(0, 3, 3, 3)
                };
                combo.SelectedIndexChanged += (_, _) => OnActionChanged(context, slot);
                var button = new Button
                {
                    Text = "...",
                    AutoSize = false,
                    Height = combo.PreferredHeight,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Enabled = false,
                    Margin = new Padding(0, 3, 0, 3)
                };
                button.Click += (_, _) => OnEditParameters(context, slot);

                grid.Controls.Add(keyLabel, 0, s + 1);
                grid.Controls.Add(combo, 1, s + 1);
                grid.Controls.Add(button, 2, s + 1);
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _actionCombos[c][s] = combo;
                _paramButtons[c][s] = button;
            }

            // Filler row absorbs leftover height so the slot rows stay compact at the top.
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(grid, c, 0);
        }

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Margin = Padding.Empty,
            Padding = new Padding(6, 4, 6, 4)
        };
        _exportButton.AutoSize = true;
        _exportButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _exportButton.MinimumSize = new Size(75, 0);
        _exportButton.Click += OnExport;
        _importButton.AutoSize = true;
        _importButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _importButton.MinimumSize = new Size(75, 0);
        _importButton.Click += OnImport;
        _hintLabel.AutoSize = true;
        _hintLabel.Margin = new Padding(12, 8, 0, 0);
        bar.Controls.Add(_exportButton);
        bar.Controls.Add(_importButton);
        bar.Controls.Add(_hintLabel);
        root.Controls.Add(bar, 0, 1);
        root.SetColumnSpan(bar, HotActionsLogic.ContextCount);

        Controls.Add(root);
        ApplyUiLocalisation();
    }

    /// <summary>Loads the hotkey slots from the player state (read-only; missing data shows None).</summary>
    /// <param name="playerState">The PlayerStateData object.</param>
    internal void LoadData(JsonObject playerState)
    {
        _playerState = playerState;
        _loading = true;
        try
        {
            for (int c = 0; c < HotActionsLogic.ContextCount; c++)
            {
                for (int s = 0; s < HotActionsLogic.SlotsPerContext; s++)
                {
                    var slot = GetSlotObject(c, s);
                    PopulateCombo(_actionCombos[c][s], HotActionsLogic.GetActionName(slot));
                    UpdateParamButton(c, s);
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Refreshes the localised context labels, action names and buttons.</summary>
    internal void ApplyUiLocalisation()
    {
        for (int c = 0; c < HotActionsLogic.ContextCount; c++)
        {
            _contextLabels[c].Text = UiStrings.Get(ContextLabelKeys[c]);
            for (int s = 0; s < HotActionsLogic.SlotsPerContext; s++)
            {
                var combo = _actionCombos[c][s];
                string? selected = (combo.SelectedItem as ActionItem)?.Info.Name;
                bool wasLoading = _loading;
                _loading = true;
                try
                {
                    PopulateCombo(combo, selected);
                }
                finally
                {
                    _loading = wasLoading;
                }
                _paramButtons[c][s].Height = combo.PreferredHeight;
            }
        }
        _exportButton.Text = UiStrings.Get("common.export");
        _importButton.Text = UiStrings.Get("common.import");
        _hintLabel.Text = UiStrings.Get("hotkeys.hint");
    }

    /// <summary>Fills a combo with every action, selecting the given enum name.</summary>
    private void PopulateCombo(ComboBox combo, string? selectedName)
    {
        var localisation = ActiveLocalisation;
        combo.Items.Clear();
        int selectedIndex = 0;
        for (int i = 0; i < HotActionsLogic.Actions.Count; i++)
        {
            var action = HotActionsLogic.Actions[i];
            combo.Items.Add(new ActionItem(action, HotActionsLogic.GetDisplayName(action, localisation)));
            if (string.Equals(action.Name, selectedName, StringComparison.Ordinal))
                selectedIndex = i;
        }
        combo.SelectedIndex = selectedIndex;
    }

    /// <summary>Writes the newly selected action into the slot (parameters reset).</summary>
    private void OnActionChanged(int context, int slot)
    {
        if (_loading || _playerState == null) return;
        if (_actionCombos[context][slot].SelectedItem is not ActionItem item) return;

        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(_playerState, context));
        HotActionsLogic.SetSlot(slots.GetObject(slot), item.Info.Name);
        UpdateParamButton(context, slot);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the parameter dialog for a slot and writes the result back.</summary>
    private void OnEditParameters(int context, int slot)
    {
        if (_playerState == null) return;
        if (_actionCombos[context][slot].SelectedItem is not ActionItem item) return;

        var current = GetSlotObject(context, slot);
        string displayName = HotActionsLogic.GetDisplayName(item.Info, ActiveLocalisation);
        using var dialog = new HotActionParameterDialog(item.Info, displayName,
            HotActionsLogic.GetId(current), HotActionsLogic.GetNumber(current),
            HotActionsLogic.GetInventoryIndex(current).X, HotActionsLogic.GetInventoryIndex(current).Y);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        var slots = HotActionsLogic.EnsureSlots(HotActionsLogic.EnsureContext(_playerState, context));
        HotActionsLogic.SetSlot(slots.GetObject(slot), item.Info.Name, dialog.Id, dialog.Number,
            dialog.IndexX, dialog.IndexY);
        UpdateParamButton(context, slot);
        DataModified?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Enables the parameter button and updates its tooltip summary.</summary>
    private void UpdateParamButton(int context, int slot)
    {
        var button = _paramButtons[context][slot];
        if (_actionCombos[context][slot].SelectedItem is not ActionItem item)
        {
            button.Enabled = false;
            _toolTip.SetToolTip(button, "");
            return;
        }

        var kind = item.Info.ParameterKind;
        button.Enabled = kind != HotActionParameterKind.None;
        if (!button.Enabled)
        {
            _toolTip.SetToolTip(button, "");
            return;
        }

        var current = GetSlotObject(context, slot);
        int number = HotActionsLogic.GetNumber(current);
        var (x, y) = HotActionsLogic.GetInventoryIndex(current);
        string summary = kind switch
        {
            HotActionParameterKind.Index => number.ToString(CultureInfo.CurrentCulture),
            HotActionParameterKind.InventoryItem =>
                UiStrings.Format("hotkeys.summary_slot", number,
                    x.ToString(CultureInfo.CurrentCulture), y.ToString(CultureInfo.CurrentCulture)),
            HotActionParameterKind.EmoteId => HotActionsLogic.GetId(current),
            HotActionParameterKind.WeaponMode => number.ToString(CultureInfo.CurrentCulture),
            HotActionParameterKind.CreatureFood =>
                UiStrings.Format("hotkeys.summary_food", number, HotActionsLogic.GetId(current)),
            _ => ""
        };
        _toolTip.SetToolTip(button, summary);
    }

    /// <summary>Returns the slot object for a context/slot, or null when it does not exist.</summary>
    private JsonObject? GetSlotObject(int context, int slot)
    {
        var hot = _playerState?.GetArray("HotActions");
        if (hot == null || context >= hot.Length) return null;
        var slots = hot.GetObject(context)?.GetArray("KeyActions");
        if (slots == null || slot >= slots.Length) return null;
        return slots.Get(slot) as JsonObject;
    }

    /// <summary>Exports the three context arrays as a hotkeys-format JSON file.</summary>
    private void OnExport(object? sender, EventArgs e)
    {
        if (_playerState == null) return;

        var config = ExportConfig.Instance;
        var vars = new Dictionary<string, string> { ["name"] = "Hotkeys" };
        using var dialog = new SaveFileDialog
        {
            Filter = ExportConfig.BuildDialogFilter(config.HotkeysExt, "Hotkeys files"),
            DefaultExt = config.HotkeysExt.TrimStart('.'),
            FileName = ExportConfig.BuildFileName(config.HotkeysTemplate, config.HotkeysExt, vars)
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            HotActionsLogic.BuildExport(_playerState).ExportToFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiStrings.Format("discovery.export_failed", ex.Message),
                UiStrings.Get("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Imports hotkeys from an export or save-shaped JSON file.</summary>
    private void OnImport(object? sender, EventArgs e)
    {
        if (_playerState == null) return;

        var config = ExportConfig.Instance;
        using var dialog = new OpenFileDialog
        {
            Filter = ExportConfig.BuildOpenFilter(config.HotkeysExt, "Hotkeys files")
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var imported = JsonObject.ImportFromFile(dialog.FileName);
            int applied = HotActionsLogic.ApplyImport(_playerState, imported);
            if (applied == 0)
            {
                MessageBox.Show(this, UiStrings.Get("discovery.import_no_items"),
                    UiStrings.Get("common.import"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadData(_playerState);
            DataModified?.Invoke(this, EventArgs.Empty);
            MessageBox.Show(this, UiStrings.Format("hotkeys.import_success", applied),
                UiStrings.Get("common.import"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiStrings.Format("discovery.import_failed", ex.Message),
                UiStrings.Get("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>The active game localisation service, when hosted in the main form.</summary>
    private LocalisationService? ActiveLocalisation =>
        (FindForm() as MainFormResources)?.CurrentLocalisation;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
