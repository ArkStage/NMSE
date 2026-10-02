using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NMSE.Core;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Controls;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Modal editor for the developer commentary unlock state, opened from the Collected
/// Knowledge tab. Writes both the DEV_NOTES high-water index and the SeenStories count
/// through <see cref="DevNotesLogic"/> so the game and the catalogue stay in step.
/// </summary>
internal sealed class DevNotesEditorDialog : Form
{
    private readonly JsonObject _playerState;
    private readonly LocalisationService? _localisation;
    private readonly InvariantNumericTextBox _countField;
    private readonly ListBox _entryList;
    private readonly TextBox _preview;
    private readonly int _devNotesSlot;
    private readonly int _devNotesPageIndex;
    private bool _updating;

    /// <summary>Gets whether the dialog changed the save data.</summary>
    public bool Modified { get; private set; }

    /// <summary>Creates the editor for the given player state.</summary>
    /// <param name="playerState">PlayerStateData from the loaded save.</param>
    /// <param name="localisation">Active game localisation, when available.</param>
    /// <param name="devNotesSlot">SeenStories slot of the Developer Commentary page.</param>
    /// <param name="devNotesPageIndex">Page index of the Developer Commentary page.</param>
    public DevNotesEditorDialog(JsonObject playerState, LocalisationService? localisation, int devNotesSlot = KnowledgeCatalogue.DevNotesPageSlot, int devNotesPageIndex = KnowledgeCatalogue.DevNotesPageIndex)
    {
        _playerState = playerState;
        _localisation = localisation;
        _devNotesSlot = devNotesSlot;
        _devNotesPageIndex = devNotesPageIndex;

        Text = UiStrings.Get("devnotes.title");
        Size = new Size(620, 560);
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var countLabel = new Label
        {
            Text = UiStrings.Get("devnotes.highest_unlocked"),
            AutoSize = true,
            Margin = new Padding(0, 6, 6, 0),
        };

        _countField = new InvariantNumericTextBox
        {
            Width = 70,
            Minimum = 0,
            Maximum = DevNotesLogic.EntryCount,
        };
        _countField.NumericValueChanged += (_, _) => OnCountChanged();

        var unlockAllButton = new Button
        {
            Text = UiStrings.Get("devnotes.unlock_all"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(10, 2, 0, 0),
        };
        unlockAllButton.Click += (_, _) => _countField.NumericValue = DevNotesLogic.EntryCount;

        var countFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 4),
        };
        countFlow.Controls.Add(countLabel);
        countFlow.Controls.Add(_countField);
        countFlow.Controls.Add(unlockAllButton);

        _entryList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
        };
        _entryList.SelectedIndexChanged += (_, _) => OnSelectionChanged();

        _preview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
        };

        var closeButton = new Button
        {
            Text = UiStrings.Get("common.close"),
            DialogResult = DialogResult.OK,
            Width = 110,
            Margin = new Padding(6, 0, 0, 0),
        };
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
        };
        buttonPanel.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(countFlow, 0, 0);
        layout.Controls.Add(_entryList, 0, 1);
        layout.Controls.Add(_preview, 0, 2);
        layout.Controls.Add(buttonPanel, 0, 3);
        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _updating = true;
        try
        {
            _countField.NumericValue = DevNotesLogic.GetUnlockedCount(_playerState, _devNotesSlot, _devNotesPageIndex);
            UpdateEntryList();
            UpdatePreview();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Applies the count field value to the save.</summary>
    private void OnCountChanged()
    {
        if (_updating) return;

        int count = (int)(_countField.NumericValue ?? 0);
        if (DevNotesLogic.SetUnlockedCount(_playerState, count, _devNotesSlot, _devNotesPageIndex))
            Modified = true;

        _updating = true;
        try
        {
            _countField.NumericValue = DevNotesLogic.GetUnlockedCount(_playerState, _devNotesSlot, _devNotesPageIndex);
            UpdateEntryList();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Selecting an entry unlocks every note up to it and previews its text.</summary>
    private void OnSelectionChanged()
    {
        if (_updating) return;

        int selected = _entryList.SelectedIndex;
        if (selected >= 0)
        {
            if (DevNotesLogic.SetUnlockedCount(_playerState, selected + 1, _devNotesSlot, _devNotesPageIndex))
                Modified = true;

            _updating = true;
            try
            {
                _countField.NumericValue = DevNotesLogic.GetUnlockedCount(_playerState, _devNotesSlot, _devNotesPageIndex);
                UpdateEntryList();
            }
            finally
            {
                _updating = false;
            }
        }
        UpdatePreview();
    }

    /// <summary>Rebuilds the entry list with the current unlock state.</summary>
    private void UpdateEntryList()
    {
        int unlocked = DevNotesLogic.GetUnlockedCount(_playerState, _devNotesSlot, _devNotesPageIndex);
        int selected = _entryList.SelectedIndex;

        _entryList.BeginUpdate();
        try
        {
            _entryList.Items.Clear();
            foreach (var entry in DevNotesLogic.Entries)
            {
                string name = ProcTechLogic.LookupGameString(_localisation, entry.NameLocKey) ?? entry.NameLocKey;
                string date = ProcTechLogic.LookupGameString(_localisation, entry.DateLocKey) ?? "";
                string status = entry.Index < unlocked
                    ? UiStrings.Get("devnotes.status_unlocked")
                    : UiStrings.Get("devnotes.status_locked");
                _entryList.Items.Add(date.Length > 0
                    ? string.Format(CultureInfo.CurrentCulture, "{0} - {1} ({2})", name, date, status)
                    : string.Format(CultureInfo.CurrentCulture, "{0} ({1})", name, status));
            }
            if (selected >= 0 && selected < _entryList.Items.Count)
                _entryList.SelectedIndex = selected;
        }
        finally
        {
            _entryList.EndUpdate();
        }
    }

    /// <summary>Shows the selected entry's commentary text in the preview box.</summary>
    private void UpdatePreview()
    {
        int index = _entryList.SelectedIndex;
        if (index < 0 || index >= DevNotesLogic.Entries.Count)
        {
            _preview.Text = "";
            return;
        }

        _preview.Text = ProcTechLogic.LookupGameString(_localisation, DevNotesLogic.Entries[index].LogLocKey) ?? "";
        _preview.SelectionStart = 0;
    }
}
