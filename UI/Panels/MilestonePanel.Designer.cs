namespace NMSE.UI.Panels;

partial class MilestonePanel
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

    private void InitializeComponent()
    {
        DoubleBuffered = true;
        SuspendLayout();

        _headerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 4, 4, 2),
        };

        _findStatBtn = new Button
        {
            Text = "Find Stat...",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 3, 6, 3),
        };
        _findStatBtn.Click += OnFindStatClicked;
        _headerPanel.Controls.Add(_findStatBtn);

        _gotoJsonBtn = new Button
        {
            FlatStyle = FlatStyle.Flat,
            FlatAppearance =
            {
                BorderColor = Core.ThemeManager.Effective == Core.AppTheme.Dark
                    ? Color.FromArgb(100, 100, 100)
                    : SystemColors.ControlDark,
                BorderSize = 1,
            },
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Size = new Size(28, 24),
            Text = "\U0001F4D1",
            Margin = new Padding(1, 3, 1, 3),
            Cursor = Cursors.Hand,
        };
        _gotoJsonBtn.Click += OnGoToJsonClicked;
        _headerPanel.Controls.Add(_gotoJsonBtn);

        _tabControl = new DoubleBufferedTabControl { Dock = DockStyle.Fill };

        Controls.Add(_tabControl);
        Controls.Add(_headerPanel);

        ResumeLayout(false);
        PerformLayout();
    }

    private FlowLayoutPanel _headerPanel = null!;
    private DoubleBufferedTabControl _tabControl = null!;
    private Button _findStatBtn = null!;
    private Button _gotoJsonBtn = null!;
}
