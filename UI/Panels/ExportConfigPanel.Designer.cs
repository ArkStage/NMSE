using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Util;

namespace NMSE.UI.Panels;

partial class ExportConfigPanel
{
    private System.ComponentModel.IContainer components = null;

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
        this._extFields = new System.Collections.Generic.Dictionary<string, System.Windows.Forms.TextBox>();
        this._templateFields = new System.Collections.Generic.Dictionary<string, System.Windows.Forms.TextBox>();
        this._mainLayout = new System.Windows.Forms.TableLayoutPanel();
        this._sectionTabs = new DoubleBufferedTabControl();
        this._saveBtn = new System.Windows.Forms.Button();
        this._resetBtn = new System.Windows.Forms.Button();
        this._statusLabel = new System.Windows.Forms.Label();
        this._mainLayout.SuspendLayout();
        this.SuspendLayout();
        //
        // _mainLayout
        //
        this._mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
        this._mainLayout.ColumnCount = 1;
        this._mainLayout.RowCount = 4;
        this._mainLayout.Padding = new System.Windows.Forms.Padding(8);
        this._mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        this._mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this._mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        this._mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        this._mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        //
        // title
        //
        _titleLabel = new System.Windows.Forms.Label();
        _titleLabel.Text = "Export Settings";
        _titleLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        _titleLabel.AutoSize = true;
        FontManager.ApplyHeadingFont(_titleLabel, 14);
        //
        // header
        //
        _headerLabel = new System.Windows.Forms.Label();
        _headerLabel.Text = "Configure custom file extensions and naming templates for import/export operations.\n" +
                       "Templates use {variable} placeholders. Changes apply immediately to all panels.";
        _headerLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        _headerLabel.AutoSize = true;
        _headerLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
        this._mainLayout.Controls.Add(_titleLabel, 0, 0);
        this._mainLayout.Controls.Add(_headerLabel, 0, 1);
        //
        // _sectionTabs
        //
        this._sectionTabs.Dock = System.Windows.Forms.DockStyle.Fill;
        this._sectionTabs.TabPages.Add(BuildExtensionsTab());
        this._sectionTabs.TabPages.Add(BuildTemplatesTab());
        this._sectionTabs.TabPages.Add(BuildHelpTab());
        this._mainLayout.Controls.Add(this._sectionTabs, 0, 2);
        //
        // buttonPanel
        //
        System.Windows.Forms.FlowLayoutPanel buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
        buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
        buttonPanel.AutoSize = true;
        buttonPanel.Padding = new System.Windows.Forms.Padding(0, 8, 0, 0);
        //
        // _saveBtn
        //
        this._saveBtn.Text = "Save Settings";
        this._saveBtn.AutoSize = true;
        this._saveBtn.Click += OnSave;
        //
        // _resetBtn
        //
        this._resetBtn.Text = "Reset to Defaults";
        this._resetBtn.AutoSize = true;
        this._resetBtn.Click += OnReset;
        //
        // _statusLabel
        //
        this._statusLabel.Text = "";
        this._statusLabel.AutoSize = true;
        this._statusLabel.ForeColor = ThemeManager.Effective == AppTheme.Dark ? ThemeColors.Dark.SuccessGreen : System.Drawing.Color.Green;
        this._statusLabel.Padding = new System.Windows.Forms.Padding(12, 6, 0, 0);
        //
        // buttonPanel contents
        //
        buttonPanel.Controls.Add(this._saveBtn);
        buttonPanel.Controls.Add(this._resetBtn);
        buttonPanel.Controls.Add(this._statusLabel);
        this._mainLayout.Controls.Add(buttonPanel, 0, 3);
        //
        // ExportConfigPanel
        //
        this.Controls.Add(this._mainLayout);
        this._mainLayout.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    #endregion

    private TabPage BuildExtensionsTab()
    {
        TabPage page = new TabPage("File Extensions");
        Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        string[] labels =
        [
            "Exosuit", "Multi-tool", "Starship", "Corvette", "Corvette Snapshot",
            "Starship Cargo", "Starship Tech",
            "Freighter", "Freighter Cargo", "Freighter Tech",
            "Frigate", "Squadron",
            "Exocraft", "Exocraft Cargo", "Exocraft Tech",
            "Companion", "Base", "Chest", "Storage",
            "Discovery", "Settlement", "ByteBeat", "Outfit", "Hotkeys"
        ];

        for (int i = 0; i < labels.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = labels[i] + ":",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, i);
            TextBox tb = new TextBox { Dock = DockStyle.Fill };
            _extFields[labels[i]] = tb;
            layout.Controls.Add(tb, 1, i);
        }

        scroll.Controls.Add(layout);
        page.Controls.Add(scroll);
        return page;
    }

    private TabPage BuildTemplatesTab()
    {
        TabPage page = new TabPage("Naming Templates");
        Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        string[] labels =
        [
            "Exosuit Cargo", "Exosuit Tech",
            "Multi-tool", "Starship", "Corvette", "Corvette Snapshot",
            "Starship Cargo", "Starship Tech",
            "Freighter", "Freighter Cargo", "Freighter Tech",
            "Frigate", "Squadron",
            "Exocraft", "Exocraft Cargo", "Exocraft Tech",
            "Companion", "Base", "Chest", "Storage",
            "Discovery", "Settlement", "ByteBeat", "Outfit", "Hotkeys"
        ];

        _templateLabels = new Label[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label
            {
                Text = labels[i] + ":",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _templateLabels[i] = lbl;
            layout.Controls.Add(lbl, 0, i);
            TextBox tb = new TextBox { Dock = DockStyle.Fill };
            _templateFields[labels[i]] = tb;
            layout.Controls.Add(tb, 1, i);
        }

        scroll.Controls.Add(layout);
        page.Controls.Add(scroll);
        return page;
    }

    private TabPage BuildHelpTab()
    {
        TabPage page = new TabPage("Help");
        const int helpHeadingBottomMargin = 2;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(4),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _helpHeadingLabel = new Label
        {
            Dock = DockStyle.Top,
            Font = new Font(DefaultFont.FontFamily, 12, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, helpHeadingBottomMargin),
            Text = """
                Template Variables
                """
        };
        FontManager.ApplyHeadingFont(_helpHeadingLabel, 12);

        _helpTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = """


                {player_name}       – Player name
                {ship_name}         – Ship / freighter name
                {multitool_name}    – Multi-tool name
                {type}              – Type display name (ship type, frigate type, etc.)
                {class}             – Class letter (S/A/B/C)
                {race}              – NPC race
                {rank}              – Pilot rank
                {seed}              – Seed value
                {name}              – Generic name
                {species}           – Companion species
                {creature_seed}     – Companion creature seed
                {vehicle_name}      – Exocraft name
                {vehicle_type}      – Exocraft type
                {base_name}         – Base name
                {chest_number}      – Chest slot number
                {timestamp}         – Epoch timestamp
                {frigate_name}      – Frigate name
                {freighter_name}    – Freighter name
                {settlement_name}   - Settlement name

                File Extensions:
                
                Extensions must start with a dot (e.g. ".nmsship").
                These are used for Save/Open file dialogs alongside
                standard .json and all-files filters.

                The naming template is combined with the extension to
                produce the default filename shown in export dialogs.
                Invalid filename characters are automatically removed.
                """
        };

        layout.Controls.Add(_helpHeadingLabel, 0, 0);
        layout.Controls.Add(_helpTextBox, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private System.Windows.Forms.TableLayoutPanel _mainLayout;
    private DoubleBufferedTabControl _sectionTabs;
    private System.Collections.Generic.Dictionary<string, System.Windows.Forms.TextBox> _extFields;
    private System.Collections.Generic.Dictionary<string, System.Windows.Forms.TextBox> _templateFields;
    private System.Windows.Forms.Button _saveBtn;
    private System.Windows.Forms.Button _resetBtn;
    private System.Windows.Forms.Label _statusLabel;
    private System.Windows.Forms.Label _titleLabel;
    private System.Windows.Forms.Label _headerLabel;
    private System.Windows.Forms.Label[] _templateLabels;
    private System.Windows.Forms.Label _helpHeadingLabel;
    private System.Windows.Forms.TextBox _helpTextBox;
}
