using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.UI.Controls;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Modal solver for pet battle seeds: searches for a SpeciesSeed/GenusSeed pair (and an
/// optional scale) that rolls the requested classes and core stat values. Exact stat targets
/// may be entered per stat; leaving a stat blank maximises it. A priority can weight one stat
/// above the others. Results are ranked, shown against the best simultaneously achievable
/// values and applied by the caller.
/// </summary>
internal sealed class PetBattleSeedSearchDialog : Form
{
    private static readonly string[] ClassItems = { "C", "B", "A", "S" };

    /// <summary>Candidate budgets for the deterministic sweep, from shallow to deep.</summary>
    private static readonly long[] BudgetSteps = [2_000_000L, 10_000_000L, 50_000_000L];

    private readonly int _healthLevel;
    private readonly int _speedLevel;
    private readonly int _combatLevel;
    private readonly double _currentScale;

    private readonly ComboBox[] _classTargets = new ComboBox[3];
    private readonly InvariantNumericTextBox[] _statTargets = new InvariantNumericTextBox[3];
    private readonly ComboBox _priorityCombo;
    private readonly CheckBox _adjustScaleCheck;
    private readonly ListView _resultsList;
    private readonly TextBox _speciesSeedValue;
    private readonly TextBox _genusSeedValue;
    private readonly Label _detailsLabel;
    private readonly Label _gameBarsLabel;
    private readonly Label _statusLabel;
    private readonly Button _searchButton;
    private readonly Button _deeperButton;
    private readonly Button _applyButton;

    private IReadOnlyList<PetBattleLogic.SolveCandidate> _results = [];
    private PetBattleLogic.SolveReference _reference;
    private CancellationTokenSource? _searchCts;
    private bool _searching;
    private bool _lastSearchAdjustedScale;
    private int _budgetIndex;
    private long _lastExamined;

    /// <summary>Gets whether the user applied a result.</summary>
    public bool Applied { get; private set; }

    /// <summary>Gets the chosen species seed.</summary>
    public ulong SpeciesSeed { get; private set; }

    /// <summary>Gets the chosen genus seed.</summary>
    public ulong GenusSeed { get; private set; }

    /// <summary>Gets whether the chosen candidate also adjusts the pet's scale.</summary>
    public bool HasScale { get; private set; }

    /// <summary>Gets the chosen scale.</summary>
    public double SelectedScale { get; private set; }

    /// <summary>Creates the seed solver dialog for a pet with the given context.</summary>
    /// <param name="healthLevel">The pet's current Health gene edit level.</param>
    /// <param name="speedLevel">The pet's current Speed gene edit level.</param>
    /// <param name="combatLevel">The pet's current Combat gene edit level.</param>
    /// <param name="currentScale">The pet's current Scale.</param>
    public PetBattleSeedSearchDialog(int healthLevel, int speedLevel, int combatLevel, double currentScale)
    {
        _healthLevel = healthLevel;
        _speedLevel = speedLevel;
        _combatLevel = combatLevel;
        _currentScale = currentScale;

        Text = UiStrings.Get("companion.battle_seed_title");
        Size = new Size(760, 660);
        MinimumSize = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var hint = new Label
        {
            Text = UiStrings.Get("companion.battle_seed_hint"),
            AutoSize = true,
            MaximumSize = new Size(710, 0),
            Margin = new Padding(0, 0, 0, 10),
        };

        var classFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        AddClassTarget(classFlow, "companion.battle_seed_health", 0);
        AddClassTarget(classFlow, "companion.battle_seed_agility", 1);
        AddClassTarget(classFlow, "companion.battle_seed_combat", 2);

        var statsHeading = new Label
        {
            Text = UiStrings.Get("companion.battle_seed_target_stats"),
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 2),
        };

        var statsFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        AddStatTarget(statsFlow, "companion.battle_seed_health", 0);
        AddStatTarget(statsFlow, "companion.battle_seed_agility", 1);
        AddStatTarget(statsFlow, "companion.battle_seed_combat", 2);

        var priorityFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
        };
        priorityFlow.Controls.Add(new Label
        {
            Text = UiStrings.Get("companion.battle_seed_priority"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 4, 0),
        });
        _priorityCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 150,
        };
        _priorityCombo.Items.Add(UiStrings.Get("companion.battle_seed_priority_balanced"));
        _priorityCombo.Items.Add(UiStrings.Get("companion.battle_seed_col_health"));
        _priorityCombo.Items.Add(UiStrings.Get("companion.battle_seed_col_speed"));
        _priorityCombo.Items.Add(UiStrings.Get("companion.battle_seed_col_combat"));
        _priorityCombo.SelectedIndex = 0;
        _priorityCombo.SelectedIndexChanged += (_, _) => ClearResults();
        priorityFlow.Controls.Add(_priorityCombo);

        _adjustScaleCheck = new CheckBox
        {
            Text = UiStrings.Get("companion.battle_seed_adjust_scale"),
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        _adjustScaleCheck.CheckedChanged += (_, _) => ClearResults();

        var scaleNote = new Label
        {
            Text = UiStrings.Get("companion.battle_seed_scale_note"),
            AutoSize = true,
            MaximumSize = new Size(710, 0),
            Margin = new Padding(0, 0, 0, 6),
        };

        _searchButton = new Button
        {
            Text = UiStrings.Get("common.search"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 10, 0),
        };
        _searchButton.Click += (_, _) => OnSearchClick();

        _deeperButton = new Button
        {
            Text = UiStrings.Get("companion.battle_seed_search_deeper"),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = false,
            Margin = new Padding(0, 0, 10, 0),
        };
        _deeperButton.Click += (_, _) => OnSearchDeeper();

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
        searchFlow.Controls.Add(_deeperButton);
        searchFlow.Controls.Add(_statusLabel);

        _resultsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Margin = new Padding(0, 4, 0, 4),
        };
        _resultsList.Columns.Add(UiStrings.Get("companion.battle_seed_col_health"), 90, HorizontalAlignment.Right);
        _resultsList.Columns.Add(UiStrings.Get("companion.battle_seed_col_speed"), 90, HorizontalAlignment.Right);
        _resultsList.Columns.Add(UiStrings.Get("companion.battle_seed_col_combat"), 90, HorizontalAlignment.Right);
        _resultsList.Columns.Add(UiStrings.Get("companion.battle_seed_col_scale"), 90, HorizontalAlignment.Right);
        _resultsList.Columns.Add(UiStrings.Get("companion.battle_seed_col_match"), 80, HorizontalAlignment.Right);
        _resultsList.SelectedIndexChanged += (_, _) => UpdateDetails();

        _speciesSeedValue = CreateResultBox();
        _genusSeedValue = CreateResultBox();
        _detailsLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 0),
        };
        _gameBarsLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 0),
        };

        var seedRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
        };
        seedRow.Controls.Add(CreateDetailsLabel("companion.battle_seed_species"));
        seedRow.Controls.Add(_speciesSeedValue);
        seedRow.Controls.Add(CreateDetailsLabel("companion.battle_seed_genus"));
        seedRow.Controls.Add(_genusSeedValue);

        var readoutRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 0),
        };
        readoutRow.Controls.Add(_detailsLabel);

        var gameBarsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 0),
        };
        gameBarsRow.Controls.Add(_gameBarsLabel);

        var detailsPanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 6),
        };
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsPanel.Controls.Add(seedRow, 0, 0);
        detailsPanel.Controls.Add(readoutRow, 0, 1);
        detailsPanel.Controls.Add(gameBarsRow, 0, 2);

        var resultsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        resultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        resultsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultsPanel.Controls.Add(_resultsList, 0, 0);
        resultsPanel.Controls.Add(detailsPanel, 0, 1);

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

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainPanel.Controls.Add(searchFlow, 0, 0);
        mainPanel.Controls.Add(resultsPanel, 0, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(classFlow, 0, 1);
        layout.Controls.Add(statsHeading, 0, 2);
        layout.Controls.Add(statsFlow, 0, 3);
        layout.Controls.Add(priorityFlow, 0, 4);
        layout.Controls.Add(_adjustScaleCheck, 0, 5);
        layout.Controls.Add(scaleNote, 0, 6);
        layout.Controls.Add(mainPanel, 0, 7);
        layout.Controls.Add(buttonPanel, 0, 8);
        Controls.Add(layout);

        AcceptButton = _searchButton;
        CancelButton = closeButton;

        FormClosing += (_, _) => _searchCts?.Cancel();
    }

    /// <summary>Adds a class target label and combo box to the class row.</summary>
    private void AddClassTarget(FlowLayoutPanel flow, string labelKey, int index)
    {
        flow.Controls.Add(new Label
        {
            Text = UiStrings.Get(labelKey),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(index == 0 ? 0 : 16, 6, 4, 0),
        });

        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 50,
        };
        combo.Items.AddRange(ClassItems);
        combo.SelectedIndex = 3;
        combo.SelectedIndexChanged += (_, _) => ClearResults();
        flow.Controls.Add(combo);
        _classTargets[index] = combo;
    }

    /// <summary>Adds an optional exact stat target label and numeric box to the stats row.</summary>
    private void AddStatTarget(FlowLayoutPanel flow, string labelKey, int index)
    {
        flow.Controls.Add(new Label
        {
            Text = UiStrings.Get(labelKey),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(index == 0 ? 0 : 16, 6, 4, 0),
        });

        var box = new InvariantNumericTextBox
        {
            Width = 100,
            Minimum = 0,
            Maximum = 100000,
            Margin = new Padding(0, 3, 0, 0),
        };
        box.NumericValueChanged += (_, _) => ClearResults();
        flow.Controls.Add(box);
        _statTargets[index] = box;
    }

    /// <summary>Creates a read-only text box for a seed value.</summary>
    private static TextBox CreateResultBox() => new()
    {
        ReadOnly = true,
        Width = 170,
        Margin = new Padding(0, 3, 0, 0),
    };

    /// <summary>Creates a label for the seed details row.</summary>
    private static Label CreateDetailsLabel(string key) => new()
    {
        Text = UiStrings.Get(key),
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 4, 0),
    };

    /// <summary>Maps the priority selection to stat weights (the chosen stat gets weight 4).</summary>
    private (float Health, float Speed, float Combat) PriorityWeights() => _priorityCombo.SelectedIndex switch
    {
        1 => (4f, 1f, 1f),
        2 => (1f, 4f, 1f),
        3 => (1f, 1f, 4f),
        _ => (1f, 1f, 1f),
    };

    /// <summary>Starts or cancels the search.</summary>
    private async void OnSearchClick()
    {
        if (_searching)
        {
            _searchCts?.Cancel();
            return;
        }

        var classes = new PetBattleLogic.Classes(
            _classTargets[2].SelectedIndex, _classTargets[1].SelectedIndex, _classTargets[0].SelectedIndex);
        _lastSearchAdjustedScale = _adjustScaleCheck.Checked;
        var weights = PriorityWeights();
        var request = new PetBattleLogic.SolveRequest(
            classes, ReadTarget(_statTargets[0]), ReadTarget(_statTargets[1]), ReadTarget(_statTargets[2]),
            _currentScale, _lastSearchAdjustedScale, _healthLevel, _speedLevel, _combatLevel,
            MaxCandidates: BudgetSteps[_budgetIndex],
            Salt: 0,
            HealthWeight: weights.Health, SpeedWeight: weights.Speed, CombatWeight: weights.Combat);
        _reference = PetBattleLogic.GetReference(request);

        _searching = true;
        _searchButton.Text = UiStrings.Get("common.cancel");
        _applyButton.Enabled = false;
        _deeperButton.Enabled = false;
        _results = [];
        _resultsList.Items.Clear();
        _speciesSeedValue.Text = "";
        _genusSeedValue.Text = "";
        _detailsLabel.Text = "";
        _gameBarsLabel.Text = "";
        _statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
            UiStrings.Get("companion.battle_seed_searching"), 0);

        _searchCts = new CancellationTokenSource();
        var progress = new Progress<long>(count =>
        {
            _lastExamined = count;
            _statusLabel.Text = string.Format(
                CultureInfo.CurrentCulture, UiStrings.Get("companion.battle_seed_searching"),
                count.ToString("N0", CultureInfo.CurrentCulture));
        });

        try
        {
            var results = await Task.Run(() => PetBattleLogic.Solve(request, _searchCts.Token, progress));
            _results = results;
            PopulateResults();
            if (results.Count == 0)
            {
                _statusLabel.Text = UiStrings.Get("companion.battle_seed_not_found");
            }
            else
            {
                _statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    UiStrings.Get("companion.battle_seed_searched"),
                    _lastExamined.ToString("N0", CultureInfo.CurrentCulture));
                _deeperButton.Enabled = _budgetIndex < BudgetSteps.Length - 1;
            }
        }
        catch (Exception)
        {
            _statusLabel.Text = UiStrings.Get("companion.battle_seed_not_found");
        }
        finally
        {
            _searching = false;
            _searchButton.Text = UiStrings.Get("common.search");
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }

    /// <summary>
    /// Continues the same deterministic sweep with a larger candidate budget. Earlier
    /// candidates are always included, so results only improve or stay the same.
    /// </summary>
    private void OnSearchDeeper()
    {
        if (_searching || _budgetIndex >= BudgetSteps.Length - 1) return;
        _budgetIndex++;
        OnSearchClick();
    }

    /// <summary>Reads a stat target, returning null when the field is blank (maximise).</summary>
    private static float? ReadTarget(InvariantNumericTextBox box)
    {
        box.TryCommit();
        return box.NumericValue is double value ? (float)value : null;
    }

    /// <summary>
    /// Converts a raw score to the displayed match fraction: how close the candidate is to the
    /// best simultaneously achievable values for the request.
    /// </summary>
    private double DisplayedMatch(double score)
    {
        double ideal = _reference.IdealScore;
        return ideal > 0.0 ? Math.Clamp(score / ideal, 0.0, 1.0) : Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>Clears any previous results (for example when the stipulations change).</summary>
    private void ClearResults()
    {
        _results = [];
        _resultsList.Items.Clear();
        _speciesSeedValue.Text = "";
        _genusSeedValue.Text = "";
        _detailsLabel.Text = "";
        _gameBarsLabel.Text = "";
        _statusLabel.Text = "";
        _applyButton.Enabled = false;
        _deeperButton.Enabled = false;
        _budgetIndex = 0;
    }

    /// <summary>Populates the results list and selects the best candidate.</summary>
    private void PopulateResults()
    {
        _resultsList.BeginUpdate();
        try
        {
            _resultsList.Items.Clear();
            foreach (var candidate in _results)
            {
                var item = new ListViewItem(candidate.Stats.Health.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(candidate.Stats.Speed.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(candidate.Stats.Combat.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add(candidate.Scale.ToString(CultureInfo.CurrentCulture));
                item.SubItems.Add((DisplayedMatch(candidate.Score) * 100.0).ToString("0.0", CultureInfo.CurrentCulture) + "%");
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

    /// <summary>Shows the selected candidate's seeds, scale and stats against their best achievable values.</summary>
    private void UpdateDetails()
    {
        if (_resultsList.SelectedIndices.Count == 0)
        {
            _speciesSeedValue.Text = "";
            _genusSeedValue.Text = "";
            _detailsLabel.Text = "";
            _gameBarsLabel.Text = "";
            _applyButton.Enabled = false;
            return;
        }

        var candidate = _results[_resultsList.SelectedIndices[0]];
        _speciesSeedValue.Text = SeedHelper.FormatSeed(candidate.SpeciesSeed);
        _genusSeedValue.Text = SeedHelper.FormatSeed(candidate.GenusSeed);
        _detailsLabel.Text = string.Format(CultureInfo.CurrentCulture,
            UiStrings.Get("companion.battle_seed_details_format"),
            candidate.Scale.ToString(CultureInfo.CurrentCulture),
            candidate.Stats.Health.ToString(CultureInfo.CurrentCulture),
            MathF.Round(_reference.Health).ToString(CultureInfo.CurrentCulture),
            candidate.Stats.Speed.ToString(CultureInfo.CurrentCulture),
            MathF.Round(_reference.Speed).ToString(CultureInfo.CurrentCulture),
            candidate.Stats.Combat.ToString(CultureInfo.CurrentCulture),
            MathF.Round(_reference.Combat).ToString(CultureInfo.CurrentCulture));
        _gameBarsLabel.Text = string.Format(CultureInfo.CurrentCulture,
            UiStrings.Get("companion.battle_seed_game_bars_format"),
            GameBarPercent(candidate.Stats.Health, PetBattleLogic.StatKind.Health),
            GameBarPercent(candidate.Stats.Speed, PetBattleLogic.StatKind.Speed),
            GameBarPercent(candidate.Stats.Combat, PetBattleLogic.StatKind.Combat));
        _applyButton.Enabled = true;
    }

    /// <summary>Formats the pet screen bar percentage the game will show for a stat value.</summary>
    private static string GameBarPercent(int value, PetBattleLogic.StatKind kind)
    {
        float fraction = PetBattleLogic.GetGameBarFraction(kind, value);
        return (fraction * 100f).ToString("0.#", CultureInfo.CurrentCulture);
    }

    /// <summary>Applies the selected candidate and closes the dialog.</summary>
    private void OnApply()
    {
        if (_resultsList.SelectedIndices.Count == 0) return;

        var candidate = _results[_resultsList.SelectedIndices[0]];
        SpeciesSeed = candidate.SpeciesSeed;
        GenusSeed = candidate.GenusSeed;
        HasScale = _lastSearchAdjustedScale;
            SelectedScale = candidate.Scale;
        Applied = true;
        DialogResult = DialogResult.OK;
    }
}
