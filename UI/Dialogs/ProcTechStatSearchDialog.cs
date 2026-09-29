using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Controls;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Modal search for a procedural technology seed whose roll best matches the requested stats.
/// The search is exhaustive over all 100,000 five-digit seeds and deterministic: the same
/// selection always produces the same ranked list.
/// </summary>
internal sealed class ProcTechStatSearchDialog : Form
{
    /// <summary>One rollable stat with its controls.</summary>
    private sealed class StatRow
    {
        public required string Stat { get; init; }
        public required string DisplayName { get; init; }
        public required float Min { get; init; }
        public required float Max { get; init; }
        public required CheckBox Check { get; init; }
        public required ComboBox Mode { get; init; }
        public required InvariantNumericTextBox Target { get; init; }
    }

    private readonly GameItem _template;
    private readonly LocalisationService? _localisation;
    private readonly List<StatRow> _rows = [];
    private readonly List<string> _statNames = [];
    private readonly ComboBox _priorityCombo;
    private readonly ListView _resultsList;
    private readonly Label _statusLabel;
    private readonly Label _detailsLabel;
    private readonly Button _searchButton;
    private readonly Button _applyButton;
    private IReadOnlyList<ProcTechLogic.SeedCandidate> _results = [];
    private List<ProcTechLogic.StatCriterion> _activeCriteria = [];

    /// <summary>Gets whether the user applied a result.</summary>
    public bool Applied { get; private set; }

    /// <summary>Gets the chosen seed.</summary>
    public uint SelectedSeed { get; private set; }

    /// <summary>Creates the stat search dialog for a procedural technology template.</summary>
    /// <param name="template">The procedural technology template to roll.</param>
    /// <param name="localisation">Active game localisation, when available.</param>
    public ProcTechStatSearchDialog(GameItem template, LocalisationService? localisation)
    {
        _template = template;
        _localisation = localisation;

        Text = UiStrings.Get("techstats.search_title");
        Size = new Size(720, 620);
        MinimumSize = new Size(620, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var hint = new Label
        {
            Text = UiStrings.Get("techstats.search_hint"),
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        string[] modeItems =
        [
            UiStrings.Get("techstats.search_mode_maximise"),
            UiStrings.Get("techstats.search_mode_target"),
        ];

        // Stat rows: distinct stats in template order, with the union of their ranges.
        var statPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 8),
        };
        statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var statInfo = new List<(string Stat, string Name, float Min, float Max)>();
        foreach (var level in template.StatLevels)
        {
            if (string.IsNullOrEmpty(level.Stat)) continue;
            int existing = statInfo.FindIndex(s => string.Equals(s.Stat, level.Stat, StringComparison.OrdinalIgnoreCase));
            if (existing < 0)
            {
                string name = ProcTechLogic.GetStatDisplayName(localisation, level.Stat, level.Name);
                statInfo.Add((level.Stat, name, level.ValueMin, level.ValueMax));
            }
            else
            {
                var current = statInfo[existing];
                statInfo[existing] = (current.Stat, current.Name,
                    MathF.Min(current.Min, level.ValueMin), MathF.Max(current.Max, level.ValueMax));
            }
        }

        foreach (var info in statInfo)
        {
            _statNames.Add(info.Name);

            var check = new CheckBox
            {
                Text = info.Name,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 4, 8, 0),
            };

            var range = new Label
            {
                Text = string.Format(CultureInfo.CurrentCulture, "{0:0.###} - {1:0.###}", info.Min, info.Max),
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 7, 8, 0),
            };

            var mode = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 110,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 4, 8, 0),
            };
            mode.Items.AddRange(modeItems);
            mode.SelectedIndex = 0;

            var target = new InvariantNumericTextBox
            {
                Width = 90,
                Minimum = -1000000,
                Maximum = 1000000,
                Enabled = false,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(8, 3, 0, 0),
            };
            target.NumericValue = (info.Min + info.Max) / 2.0;
            mode.SelectedIndexChanged += (_, _) =>
            {
                target.Enabled = mode.SelectedIndex == 1;
            };

            statPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statPanel.Controls.Add(check, 0, statPanel.RowCount);
            statPanel.Controls.Add(range, 1, statPanel.RowCount);
            statPanel.Controls.Add(mode, 2, statPanel.RowCount);
            statPanel.Controls.Add(target, 3, statPanel.RowCount);
            statPanel.RowCount++;
            _rows.Add(new StatRow
            {
                Stat = info.Stat,
                DisplayName = info.Name,
                Min = info.Min,
                Max = info.Max,
                Check = check,
                Mode = mode,
                Target = target,
            });
        }

        var statScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Height = 180,
            Margin = new Padding(0, 0, 0, 8),
        };
        statScroll.Controls.Add(statPanel);

        // Priority row.
        var priorityFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        priorityFlow.Controls.Add(new Label
        {
            Text = UiStrings.Get("techstats.search_priority"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 4, 0),
        });
        _priorityCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
        };
        _priorityCombo.Items.Add(UiStrings.Get("techstats.search_priority_none"));
        foreach (var name in _statNames)
            _priorityCombo.Items.Add(name);
        _priorityCombo.SelectedIndex = 0;
        priorityFlow.Controls.Add(_priorityCombo);

        // Search row.
        _searchButton = new Button
        {
            Text = UiStrings.Get("common.search"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 10, 0),
        };
        _searchButton.Click += (_, _) => OnSearchClick();

        _statusLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 0, 0),
        };

        var searchFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        searchFlow.Controls.Add(_searchButton);
        searchFlow.Controls.Add(_statusLabel);

        // Results grid and details.
        _resultsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Margin = new Padding(0, 4, 0, 4),
        };
        _resultsList.SelectedIndexChanged += (_, _) => UpdateDetails();

        _detailsLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
        };

        var resultsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        resultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        resultsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultsPanel.Controls.Add(_resultsList, 0, 0);
        resultsPanel.Controls.Add(_detailsLabel, 0, 1);

        _applyButton = new Button
        {
            Text = UiStrings.Get("common.apply"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = false,
            Margin = new Padding(6, 0, 0, 0),
        };
        _applyButton.Click += (_, _) => OnApply();

        var closeButton = new Button
        {
            Text = UiStrings.Get("common.close"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(6, 0, 0, 0),
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(_applyButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(statScroll, 0, 1);
        layout.Controls.Add(priorityFlow, 0, 2);
        layout.Controls.Add(searchFlow, 0, 3);
        layout.Controls.Add(resultsPanel, 0, 4);
        layout.Controls.Add(buttonPanel, 0, 5);
        Controls.Add(layout);

        AcceptButton = _searchButton;
        CancelButton = closeButton;
    }

    /// <summary>Builds the search criteria from the checked rows.</summary>
    private List<ProcTechLogic.StatCriterion> BuildCriteria()
    {
        var criteria = new List<ProcTechLogic.StatCriterion>();
        string priorityStat = _priorityCombo.SelectedIndex > 0
            ? _rows[_priorityCombo.SelectedIndex - 1].Stat
            : "";

        foreach (var row in _rows)
        {
            if (!row.Check.Checked) continue;

            var mode = row.Mode.SelectedIndex == 1
                ? ProcTechLogic.StatMatchMode.Target
                : ProcTechLogic.StatMatchMode.Maximise;
            float target = 0f;
            if (mode == ProcTechLogic.StatMatchMode.Target)
            {
                row.Target.TryCommit();
                target = (float)(row.Target.NumericValue ?? (row.Min + row.Max) / 2.0);
            }
            bool priority = string.Equals(row.Stat, priorityStat, StringComparison.OrdinalIgnoreCase);
            criteria.Add(new ProcTechLogic.StatCriterion(row.Stat, mode, target, priority));
        }
        return criteria;
    }

    /// <summary>Runs the exhaustive seed search for the selected criteria.</summary>
    private async void OnSearchClick()
    {
        var criteria = BuildCriteria();
        if (criteria.Count == 0)
        {
            _statusLabel.Text = UiStrings.Get("techstats.search_none");
            return;
        }

        _activeCriteria = criteria;
        ConfigureColumns();
        _searchButton.Enabled = false;
        _applyButton.Enabled = false;
        _results = [];
        _resultsList.Items.Clear();
        _detailsLabel.Text = "";
        _statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
            UiStrings.Get("techstats.search_searching"), 0);

        var progress = new Progress<int>(count => _statusLabel.Text = string.Format(
            CultureInfo.CurrentCulture, UiStrings.Get("techstats.search_searching"),
            count.ToString("N0", CultureInfo.CurrentCulture)));

        try
        {
            var results = await Task.Run(() => ProcTechLogic.Search(_template, criteria, 20, false, progress));
            _results = results;
            PopulateResults();
            _statusLabel.Text = results.Count == 0 ? UiStrings.Get("techstats.search_none") : "";
        }
        catch (Exception)
        {
            _statusLabel.Text = UiStrings.Get("techstats.search_none");
        }
        finally
        {
            _searchButton.Enabled = true;
        }
    }

    /// <summary>Rebuilds the result columns for the active criteria.</summary>
    private void ConfigureColumns()
    {
        _resultsList.Columns.Clear();
        _resultsList.Columns.Add(UiStrings.Get("techstats.search_seed_col"), 60, HorizontalAlignment.Right);
        foreach (var criterion in _activeCriteria)
        {
            string name = _rows.Find(r => string.Equals(r.Stat, criterion.Stat, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? criterion.Stat;
            _resultsList.Columns.Add(name, 150, HorizontalAlignment.Right);
        }
        _resultsList.Columns.Add(UiStrings.Get("techstats.search_match_col"), 60, HorizontalAlignment.Right);
    }

    /// <summary>Populates the results list and selects the best candidate.</summary>
    private void PopulateResults()
    {
        string? lightYearTemplate = ProcTechLogic.LookupGameString(_localisation, "STATS_UNIT_LIGHTYEAR_DISTANCE");

        _resultsList.BeginUpdate();
        try
        {
            _resultsList.Items.Clear();
            foreach (var candidate in _results)
            {
                var item = new ListViewItem(candidate.Seed.ToString("D5", CultureInfo.InvariantCulture));
                foreach (var criterion in _activeCriteria)
                {
                    ProcTechLogic.ProcStatRoll? roll = null;
                    foreach (var candidateRoll in candidate.Rolls)
                    {
                        if (string.Equals(candidateRoll.Stat, criterion.Stat, StringComparison.OrdinalIgnoreCase))
                        {
                            roll = candidateRoll;
                            break;
                        }
                    }
                    if (roll == null)
                    {
                        item.SubItems.Add("-");
                    }
                    else
                    {
                        bool baseKnown = ProcTechData.TryGetBaseStatAmount(roll.Stat, _template.BaseStat, out float baseAmount);
                        item.SubItems.Add(ProcTechLogic.FormatValue(roll, baseAmount, baseKnown, lightYearTemplate));
                    }
                }
                item.SubItems.Add((candidate.Score * 100f).ToString("0.0", CultureInfo.CurrentCulture) + "%");
                _resultsList.Items.Add(item);
            }
            if (_resultsList.Items.Count > 0)
                _resultsList.Items[0].Selected = true;
        }
        finally
        {
            _resultsList.EndUpdate();
        }
        UpdateDetails();
    }

    /// <summary>Shows the selected candidate's full roll in the details label.</summary>
    private void UpdateDetails()
    {
        if (_resultsList.SelectedIndices.Count == 0)
        {
            _detailsLabel.Text = "";
            _applyButton.Enabled = false;
            return;
        }

        var candidate = _results[_resultsList.SelectedIndices[0]];
        string? lightYearTemplate = ProcTechLogic.LookupGameString(_localisation, "STATS_UNIT_LIGHTYEAR_DISTANCE");
        var lines = new List<string>();
        foreach (var roll in candidate.Rolls)
        {
            if (!ProcTechData.IsStatDisplayable(roll.Stat))
                continue;
            lines.Add(ProcTechLogic.FormatRollLine(roll, _template.BaseStat, _localisation, lightYearTemplate));
        }
        _detailsLabel.Text = string.Join(Environment.NewLine, lines);
        _applyButton.Enabled = true;
    }

    /// <summary>Applies the selected seed and closes the dialog.</summary>
    private void OnApply()
    {
        if (_resultsList.SelectedIndices.Count == 0) return;

        SelectedSeed = _results[_resultsList.SelectedIndices[0]].Seed;
        Applied = true;
        DialogResult = DialogResult.OK;
    }
}
