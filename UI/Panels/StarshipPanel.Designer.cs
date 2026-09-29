#nullable enable
using NMSE.Core;
using NMSE.UI.Controls;
using NMSE.UI.Util;

namespace NMSE.UI.Panels;

partial class StarshipPanel
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer? components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Component Designer generated code
    private void InitializeComponent()
    {
        this.SuspendLayout();
        // 
        // StarshipPanel
        // 
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.DoubleBuffered = true;
        this.ResumeLayout(false);
    }
    #endregion

    private void SetupLayout()
    {
        SuspendLayout();

        // Main layout: 1 column, 3 rows
        // Row 0: title, Row 1: ship selector, Row 2: outer tab control (fill)
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            AutoSize = false
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // ship selector row
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // outer tabs

        // --- Title panel ---
        _titleLabel = new Label
        {
            Text = "Starships",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 2),
            Anchor = AnchorStyles.Left
        };
        FontManager.ApplyHeadingFont(_titleLabel, 14);
        _primaryShipLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(15, 5, 0, 0),
            Font = new Font(Font.FontFamily, 9, FontStyle.Italic),
        };

        var headerStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1
        };
        headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titlePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        titlePanel.Controls.Add(_titleLabel);
        titlePanel.Controls.Add(_primaryShipLabel);
        headerStrip.Controls.Add(titlePanel, 0, 0);

		var gotoButtonPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			AutoSize = true,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
		};

        _gotoListBtn = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 24),
            Text = "\U0001F4D1",
            Margin = new Padding(1, 3, 1, 1),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _gotoListBtn.FlatAppearance.BorderColor = ThemeManager.Effective == AppTheme.Dark ? Color.FromArgb(100, 100, 100) : SystemColors.ControlDark;
        _gotoListBtn.Click += OnGoToJsonListClicked;
        gotoButtonPanel.Controls.Add(_gotoListBtn);

        _gotoSelectedBtn = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 24),
            Text = "\U0001F4D1",
            Margin = new Padding(1, 3, 1, 1),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _gotoSelectedBtn.FlatAppearance.BorderColor = ThemeManager.Effective == AppTheme.Dark ? Color.FromArgb(100, 100, 100) : SystemColors.ControlDark;
        _gotoSelectedBtn.Click += OnGoToJsonSelectedClicked;
        gotoButtonPanel.Controls.Add(_gotoSelectedBtn);

        _gotoStoreBtn = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 24),
            Text = "\U0001F4D1",
            Margin = new Padding(1, 3, 1, 1),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _gotoStoreBtn.FlatAppearance.BorderColor = ThemeManager.Effective == AppTheme.Dark ? Color.FromArgb(100, 100, 100) : SystemColors.ControlDark;
        _gotoStoreBtn.Click += OnGoToJsonCargoClicked;
        gotoButtonPanel.Controls.Add(_gotoStoreBtn);

        _gotoCustBtn = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Size = new Size(28, 24),
            Text = "\U0001F4D1",
            Margin = new Padding(1, 3, 1, 1),
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _gotoCustBtn.FlatAppearance.BorderColor = ThemeManager.Effective == AppTheme.Dark ? Color.FromArgb(100, 100, 100) : SystemColors.ControlDark;
        _gotoCustBtn.Click += OnGoToJsonCustomisationClicked;
        gotoButtonPanel.Controls.Add(_gotoCustBtn);

        headerStrip.Controls.Add(gotoButtonPanel, 2, 0);

        mainLayout.Controls.Add(headerStrip, 0, 0);

        // --- Ship selector row (above the tabs) ---
        var selectorRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 4),
        };
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _selectLabel = new Label
        {
            Text = "Select Ship:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 10, 0),
        };
        _shipSelector = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _shipSelector.SelectedIndexChanged += OnShipSelected;
        selectorRow.Controls.Add(_selectLabel, 0, 0);
        selectorRow.Controls.Add(_shipSelector, 1, 0);

        _archiveMoveBtn = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(3, 0, 0, 0),
        };
        _archiveMoveBtn.Click += OnMoveShipToArchive;
        selectorRow.Controls.Add(_archiveMoveBtn, 2, 0);

        _archiveImportBtn = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(3, 0, 0, 0),
        };
        _archiveImportBtn.Click += OnImportShipFromArchive;
        selectorRow.Controls.Add(_archiveImportBtn, 3, 0);

        mainLayout.Controls.Add(selectorRow, 0, 1);

        // --- Outer tab control ---
        _outerTabs = new DoubleBufferedTabControl { Dock = DockStyle.Fill };

        // -- "Ship Details" tab --
        _shipDetailsTabPage = new TabPage();

        var shipDetailsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = false,
        };
        shipDetailsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shipDetailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Details+Stats layout: 2 columns, 3 rows (details/stats, buttons, corvette row)
        var detailsStatsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true
        };
        detailsStatsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detailsStatsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        detailsStatsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsStatsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsStatsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Left panel for ship properties
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = true
        };
        for (int i = 0; i < 6; i++)
            leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        leftPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        _detailsLabel = new Label
        {
            Text = "Details",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 2)
        };
        FontManager.ApplyHeadingFont(_detailsLabel, 10);
        leftPanel.Controls.Add(_detailsLabel, 0, row);
        leftPanel.SetColumnSpan(_detailsLabel, 2);
        row++;

        _shipName = new TextBox { Dock = DockStyle.Fill };
        _shipName.TextChanged += OnShipDetailChanged;
        _shipName.Leave += OnShipNameChanged;
        _shipName.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _shipName.Parent?.Focus(); }
        };
        _nameLabel = AddRow(leftPanel, "Name:", _shipName, row++);

        _shipType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _shipType.SelectedIndexChanged += OnShipTypeChanged;
        _typeLabel = AddRow(leftPanel, "Type:", _shipType, row++);

        _shipClass = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        _shipClass.Items.AddRange(StarshipLogic.ShipClasses);
        _shipClass.SelectedIndexChanged += OnShipDetailChanged;
        _classLabel = AddRow(leftPanel, "Class:", _shipClass, row++);

        var seedPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        seedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _shipSeed = new TextBox { Dock = DockStyle.Fill };
        _shipSeed.TextChanged += OnShipDetailChanged;
        _shipSeed.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _shipSeed.Parent?.Focus(); }
        };
        _generateSeedBtn = new Button
        {
            Text = "Generate",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(70, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom,
        };
        _generateSeedBtn.Click += (s, e) =>
        {
            byte[] bytes = new byte[8];
            _rng.NextBytes(bytes);
            _shipSeed.Text = "0x" + BitConverter.ToString(bytes).Replace("-", "");
        };
        seedPanel.Controls.Add(_shipSeed, 0, 0);
        seedPanel.Controls.Add(_generateSeedBtn, 1, 0);
        _seedLabel = AddRow(leftPanel, "Seed:", seedPanel, row++);

        // Right panel for base stats
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true
        };
        for (int i = 0; i < 5; i++)
            rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rightPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int statRow = 0;
        _statsLabel = new Label
        {
            Text = "Base Stats",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 2)
        };
        FontManager.ApplyHeadingFont(_statsLabel, 10);
        rightPanel.Controls.Add(_statsLabel, 0, statRow);
        rightPanel.SetColumnSpan(_statsLabel, 2);
        statRow++;

        _damageField = new InvariantNumericTextBox { Dock = DockStyle.Fill };
        _damageField.NumericValueChanged += OnShipDetailChanged;
        _damageLabel = AddRow(rightPanel, "Damage:", _damageField, statRow++);

        _shieldField = new InvariantNumericTextBox { Dock = DockStyle.Fill };
        _shieldField.NumericValueChanged += OnShipDetailChanged;
        _shieldLabel = AddRow(rightPanel, "Shield:", _shieldField, statRow++);

        _hyperdriveField = new InvariantNumericTextBox { Dock = DockStyle.Fill };
        _hyperdriveField.NumericValueChanged += OnShipDetailChanged;
        _hyperdriveLabel = AddRow(rightPanel, "Hyperdrive:", _hyperdriveField, statRow++);

        _maneuverField = new InvariantNumericTextBox { Dock = DockStyle.Fill };
        _maneuverField.NumericValueChanged += OnShipDetailChanged;
        _maneuverLabel = AddRow(rightPanel, "Maneuverability:", _maneuverField, statRow++);

        _useOldColours = new CheckBox { Text = "Use Old Color", AutoSize = true };
        _useOldColours.CheckedChanged += OnShipDetailChanged;
        rightPanel.Controls.Add(new Label(), 0, statRow);
        rightPanel.Controls.Add(_useOldColours, 1, statRow++);

        detailsStatsLayout.Controls.Add(leftPanel, 0, 0);
        detailsStatsLayout.Controls.Add(rightPanel, 1, 0);

        // Buttons panel
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        _deleteBtn = new Button
        {
            Text = "Delete Ship",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(76, 0)
        };
        _deleteBtn.Click += OnDeleteShip;
        _exportBtn = new Button
        {
            Text = "Export",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0)
        };
        _exportBtn.Click += OnExportShip;
        _importBtn = new Button
        {
            Text = "Import",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0)
        };
        _importBtn.Click += OnImportShip;
        _makePrimaryBtn = new Button
        {
            Text = "Make Primary",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 0)
        };
        _makePrimaryBtn.Click += OnMakePrimary;
        _corvetteWarningLabel = new ColorEmojiLabel
        {
            Text = "\u26A0 Saves only store full Technology slots for your last active Corvette.",
            Font = new Font("Segoe UI Emoji", 9, FontStyle.Bold),
            AutoSize = true,
            ForeColor = ThemeManager.Effective == AppTheme.Dark ? ThemeColors.Dark.ErrorRed : Color.Red,
            Padding = new Padding(5, 5, 0, 0),
            Visible = false,
        };
        buttonPanel.Controls.Add(_deleteBtn);
        buttonPanel.Controls.Add(_exportBtn);
        buttonPanel.Controls.Add(_importBtn);
        buttonPanel.Controls.Add(_makePrimaryBtn);
        buttonPanel.Controls.Add(_corvetteWarningLabel);
        detailsStatsLayout.Controls.Add(buttonPanel, 0, 1);
        detailsStatsLayout.SetColumnSpan(buttonPanel, 2);

        // Corvette extras panel
        _corvetteExtrasPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0),
            Margin = new Padding(0),
            Visible = false,
        };
        _snapshotTechBtn = new Button
        {
            Text = "Snapshot Tech",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(100, 0)
        };
        _snapshotTechBtn.Click += OnSnapshotTech;
        _importSnapshotBtn = new Button
        {
            Text = "Import Snapshot",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(100, 0)
        };
        _importSnapshotBtn.Click += OnImportSnapshot;
        _optimiseBtn = new Button
        {
            Text = "Optimise Build",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(100, 0)
        };
        _optimiseBtn.Click += OnOptimiseCorvette;
        _optimiseIndicator = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 0),
        };
        SetOptimiseIndicator(false);
        _corvetteExtrasPanel.Controls.Add(_snapshotTechBtn);
        _corvetteExtrasPanel.Controls.Add(_importSnapshotBtn);
        _corvetteExtrasPanel.Controls.Add(_optimiseBtn);
        _corvetteExtrasPanel.Controls.Add(_optimiseIndicator);
        detailsStatsLayout.Controls.Add(_corvetteExtrasPanel, 0, 2);
        detailsStatsLayout.SetColumnSpan(_corvetteExtrasPanel, 2);

        shipDetailsLayout.Controls.Add(detailsStatsLayout, 0, 0);

        // Inventory tabs
        _inventoryGrid = new InventoryGridPanel { Dock = DockStyle.Fill };
        _techGrid = new InventoryGridPanel { Dock = DockStyle.Fill };
        _techGrid.SetIsTechInventory(true);
        _inventoryGrid.SetIsCargoInventory(true);
        _inventoryGrid.SetSortingEnabled(true);
        _techGrid.SetInventoryOwnerType("Ship");
        _inventoryGrid.SetInventoryOwnerType("Ship");
        _inventoryGrid.SetInventoryGroup("ShipCargo");
        _inventoryGrid.SetPinSlotFeatureEnabled(true);
        _techGrid.SetInventoryGroup("Ship");
        _inventoryGrid.DataModified += (s, e) => DataModified?.Invoke(this, e);
        _techGrid.DataModified += (s, e) => DataModified?.Invoke(this, e);
        _inventoryGrid.PinnedSlotsChanged += OnPinnedSlotsChanged;
        _inventoryGrid.AutoStackToStorageRequested += OnAutoStackToStorageRequested;
        _inventoryGrid.AutoStackToFreighterRequested += OnAutoStackToFreighterRequested;
        _inventoryGrid.AutoStackSelectedSlotToStorageRequested += OnAutoStackSelectedSlotToStorageRequested;
        _inventoryGrid.AutoStackSelectedSlotToFreighterRequested += OnAutoStackSelectedSlotToFreighterRequested;
        _inventoryGrid.RefreshToolbarActions();

        _invTabs = new DoubleBufferedTabControl { Dock = DockStyle.Fill };
        _cargoTabPage = new TabPage();
        _cargoTabPage.Controls.Add(_inventoryGrid);
        _techTabPage = new TabPage();
        _techTabPage.Controls.Add(_techGrid);
        _invTabs.TabPages.Add(_cargoTabPage);
        _invTabs.TabPages.Add(_techTabPage);
        shipDetailsLayout.Controls.Add(_invTabs, 0, 1);

        _shipDetailsTabPage.Controls.Add(shipDetailsLayout);
        _outerTabs.TabPages.Add(_shipDetailsTabPage);

        // -- "Customisation" tab --
        _customisationTabPage = new TabPage();
        BuildCustomisationTab();
        _outerTabs.TabPages.Add(_customisationTabPage);

        // Prevent navigation to the Customisation tab when it is disabled
        _outerTabs.Selecting += OnOuterTabSelecting;

        mainLayout.Controls.Add(_outerTabs, 0, 2);
        Controls.Add(mainLayout);

        _inventoryGrid.SetSuperchargeDisabled(true);

        // Set initial Max Supported labels (will be updated on ship selection)
        SetStarshipMaxSupportedLabels("Unknown");

        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// Builds the initial content of the Customisation tab.
    /// The static controls (scene row, info label) are created here.
    /// Dynamic controls (slots, palette, texture options) are rebuilt in
    /// RebuildCustomisationDynamicControls when a ship is selected.
    /// </summary>
    private void BuildCustomisationTab()
    {
        _customisationScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };

        _customisationContent = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0),
        };
        _customisationContent.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _customisationContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Row 0: scene label + combo
        _sceneLabelCtrl = new Label
        {
            Text = "Ship Scene (Model) Resource:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 10, 0),
        };
        _sceneCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
        };
        _sceneCombo.Leave += OnSceneComboLeave;
        _customisationContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _customisationContent.Controls.Add(_sceneLabelCtrl, 0, 0);
        _customisationContent.Controls.Add(_sceneCombo, 1, 0);

        // Info label (shown when corvette or no config available)
        _customisationInfoLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0),
            ForeColor = SystemColors.GrayText,
            Visible = false,
        };

        _customisationScroll.Controls.Add(_customisationContent);
        _customisationTabPage.Controls.Add(_customisationScroll);
    }

    /// <summary>
    /// Cancels tab switching to the Customisation tab when it is in the disabled state.
    /// </summary>
    private void OnOuterTabSelecting(object? sender, TabControlCancelEventArgs e)
    {
        if (e.TabPage == _customisationTabPage && !_customisationTabEnabled)
            e.Cancel = true;
    }

    private ComboBox _shipSelector = null!;
    private TextBox _shipName = null!;
    private ComboBox _shipClass = null!;
    private ComboBox _shipType = null!;
    private TextBox _shipSeed = null!;
    private Button _generateSeedBtn = null!;
    private CheckBox _useOldColours = null!;
    private InvariantNumericTextBox _damageField = null!;
    private InvariantNumericTextBox _shieldField = null!;
    private InvariantNumericTextBox _hyperdriveField = null!;
    private InvariantNumericTextBox _maneuverField = null!;
    private Button _deleteBtn = null!;
    private Button _exportBtn = null!;
    private Button _importBtn = null!;
    private Button _makePrimaryBtn = null!;
    private Button _archiveMoveBtn = null!;
    private Button _archiveImportBtn = null!;
    private Label _primaryShipLabel = null!;
    private ColorEmojiLabel _corvetteWarningLabel = null!;
    private FlowLayoutPanel _corvetteExtrasPanel = null!;
    private Button _snapshotTechBtn = null!;
    private Button _importSnapshotBtn = null!;
    private Button _optimiseBtn = null!;
    private Label _optimiseIndicator = null!;
    private DoubleBufferedTabControl _invTabs = null!;
    private InventoryGridPanel _inventoryGrid = null!;
    private InventoryGridPanel _techGrid = null!;
    private Label _titleLabel = null!;
    private Label _detailsLabel = null!;
    private Label _statsLabel = null!;
    private Label _selectLabel = null!;
    private Label _nameLabel = null!;
    private Label _typeLabel = null!;
    private Label _classLabel = null!;
    private Label _seedLabel = null!;
    private Label _damageLabel = null!;
    private Label _shieldLabel = null!;
    private Label _hyperdriveLabel = null!;
    private Label _maneuverLabel = null!;
    private TabPage _cargoTabPage = null!;
    private TabPage _techTabPage = null!;

    // Outer tab control and sub-tab pages
    private DoubleBufferedTabControl _outerTabs = null!;
    private TabPage _shipDetailsTabPage = null!;
    private TabPage _customisationTabPage = null!;

    // Customisation tab controls
    private Panel _customisationScroll = null!;
    private TableLayoutPanel _customisationContent = null!;
    private Label _sceneLabelCtrl = null!;
    private ComboBox _sceneCombo = null!;
    private Label _customisationInfoLabel = null!;

    // Tracks whether the Customisation tab may be navigated to
    private bool _customisationTabEnabled = true;

    // Warning label next to Solar sail colour picker
    private Label? _sailColourWarningLabel = null;

    private Button _gotoListBtn = null!;
    private Button _gotoSelectedBtn = null!;
    private Button _gotoStoreBtn = null!;
    private Button _gotoCustBtn = null!;
}

