using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Searchable correspondence table for the Milestones tab. Shows the in-game name, editor name,
/// internal stat ID, location and notes for every placement.
/// </summary>
internal sealed class MilestoneStatReferenceDialog : Form
{
    private readonly IReadOnlyList<MilestonePlacement> _placements;
    private readonly Func<MilestonePlacement, string> _locationFormatter;
    private readonly Func<string, string> _translate;
    private readonly DataGridView _grid = new();
    private readonly TextBox _filter = new();
    private readonly Label _countLabel = new();
    private readonly Button _goButton = new();

    internal MilestonePlacement? SelectedPlacement { get; private set; }

    internal MilestoneStatReferenceDialog(
        IReadOnlyList<MilestonePlacement> placements,
        Func<MilestonePlacement, string> locationFormatter,
        Func<string, string> translate)
    {
        _placements = placements;
        _locationFormatter = locationFormatter;
        _translate = translate;

        Text = _translate("milestone.reference_title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowIcon = false;
        Width = 1050;
        Height = 640;
        MinimumSize = new Size(700, 400);

        BuildUi();
        PopulateRows();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var filterRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6),
        };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _filter.Dock = DockStyle.Fill;
        _filter.PlaceholderText = _translate("milestone.reference_filter");
        _filter.TextChanged += (_, _) => ApplyFilter();
        filterRow.Controls.Add(_filter, 0, 0);

        _countLabel.AutoSize = true;
        _countLabel.Anchor = AnchorStyles.Right;
        _countLabel.Margin = new Padding(8, 4, 0, 0);
        filterRow.Controls.Add(_countLabel, 1, 0);
        root.Controls.Add(filterRow, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "game",
            HeaderText = _translate("milestone.reference_game"),
            FillWeight = 22,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "editor",
            HeaderText = _translate("milestone.reference_editor"),
            FillWeight = 22,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "id",
            HeaderText = _translate("milestone.reference_id"),
            FillWeight = 16,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "location",
            HeaderText = _translate("milestone.reference_location"),
            FillWeight = 26,
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "notes",
            HeaderText = _translate("milestone.reference_notes"),
            FillWeight = 24,
        });
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0) SelectRow(e.RowIndex);
        };
        root.Controls.Add(_grid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };

        var closeButton = new Button
        {
            Text = _translate("milestone.reference_close"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.Cancel,
        };
        buttons.Controls.Add(closeButton);

        _goButton.Text = _translate("milestone.reference_navigate");
        _goButton.AutoSize = true;
        _goButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _goButton.Enabled = false;
        _goButton.Click += (_, _) => SelectCurrentRow();
        buttons.Controls.Add(_goButton);
        root.Controls.Add(buttons, 0, 2);

        _grid.SelectionChanged += (_, _) =>
            _goButton.Enabled = _grid.SelectedRows.Count > 0;

        Controls.Add(root);
        CancelButton = closeButton;
    }

    private void PopulateRows()
    {
        _grid.Rows.Clear();
        foreach (var placement in _placements)
        {
            var stat = MilestoneCatalog.Stats.TryGetValue(placement.StatId, out var value)
                ? value
                : null;

            string notes = string.Empty;
            if (placement.NoteKey != null)
                notes = _translate(placement.NoteKey);
            if (placement.EditorOnly && placement.NoteKey != "milestone.note_editor_only")
                notes = JoinNote(notes, _translate("milestone.note_editor_only"));

            if (stat is { IsShared: true })
            {
                var others = stat.Placements
                    .Where(p => p != placement)
                    .Select(_locationFormatter)
                    .Distinct(StringComparer.CurrentCulture)
                    .ToList();
                if (others.Count > 0)
                    notes = JoinNote(notes, UiStrings.Format("milestone.note_shared", string.Join("; ", others)));
            }

            int rowIndex = _grid.Rows.Add(
                _translate(placement.GameNameKey),
                _translate(placement.EditorNameKey),
                placement.StatId,
                _locationFormatter(placement),
                notes);
            _grid.Rows[rowIndex].Tag = placement;
        }

        ApplyFilter();
    }

    private static string JoinNote(string current, string addition)
    {
        if (string.IsNullOrEmpty(current)) return addition;
        return current + "; " + addition;
    }

    private void ApplyFilter()
    {
        string query = _filter.Text.Trim();
        int visible = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            bool match = query.Length == 0;
            if (!match)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    string text = cell.Value?.ToString() ?? "";
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }
            }

            row.Visible = match;
            if (match) visible++;
        }

        _countLabel.Text = UiStrings.Format(
            "milestone.reference_count", visible, _grid.Rows.Count);
        _goButton.Enabled = _grid.SelectedRows.Count > 0
            && _grid.SelectedRows[0].Visible;
    }

    private void SelectCurrentRow()
    {
        if (_grid.SelectedRows.Count == 0) return;
        SelectRow(_grid.SelectedRows[0].Index);
    }

    private void SelectRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[rowIndex].Tag is not MilestonePlacement placement) return;
        SelectedPlacement = placement;
        DialogResult = DialogResult.OK;
        Close();
    }
}
