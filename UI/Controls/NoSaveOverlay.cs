using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Util;

namespace NMSE.UI.Controls;

/// <summary>
/// Full-page overlay shown over editor tabs while no save file is loaded.
/// Displays a themed message telling the user to load a save and blocks
/// interaction with the panel content beneath it.
/// </summary>
internal sealed class NoSaveOverlay : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;

    /// <summary>
    /// Creates the overlay. The message text is read from the localisation
    /// layer and refreshed on language changes via <see cref="RefreshLocalisation"/>.
    /// </summary>
    public NoSaveOverlay()
    {
        Dock = DockStyle.Fill;
        TabStop = false;
        Margin = Padding.Empty;

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = UiStrings.Get("lock.title"),
            Padding = new Padding(0, 0, 0, 4)
        };
        FontManager.ApplyHeadingFont(_titleLabel, 18F);

        _hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = UiStrings.Get("lock.hint"),
            Padding = new Padding(32, 4, 32, 0)
        };
        FontManager.ApplyFont(_hintLabel, 10F);

        // Keep the hint wrapped to the overlay width so long sentences and translations do
        // not clip horizontally at high display scaling.
        Resize += (_, _) => UpdateHintWrap();
        UpdateHintWrap();

        // Centres the title/hint pair vertically between two flexible rows.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        layout.Controls.Add(_titleLabel, 0, 1);
        layout.Controls.Add(_hintLabel, 0, 2);
        Controls.Add(layout);

        ApplyPalette();
        ThemeManager.ThemeChanged += ApplyPalette;
    }

    /// <summary>
    /// Constrains the hint label to the overlay width so its text wraps instead of
    /// overflowing when the display is scaled up.
    /// </summary>
    private void UpdateHintWrap()
    {
        int width = Width - _hintLabel.Padding.Horizontal;
        if (width <= 0) return;
        _hintLabel.MaximumSize = new Size(width, 0);
    }

    /// <summary>
    /// Refreshes the localised message text. Called after a language switch.
    /// </summary>
    internal void RefreshLocalisation()
    {
        _titleLabel.Text = UiStrings.Get("lock.title");
        _hintLabel.Text = UiStrings.Get("lock.hint");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ThemeManager.ThemeChanged -= ApplyPalette;
        base.Dispose(disposing);
    }

    /// <summary>
    /// Applies the current theme colours to the overlay and its labels.
    /// </summary>
    private void ApplyPalette()
    {
        var palette = ThemeColors.Get(ThemeManager.Effective == AppTheme.Dark ? "Dark" : "Light");
        BackColor = palette.Background;
        _titleLabel.ForeColor = palette.WarningOrange;
        _hintLabel.ForeColor = palette.SecondaryText;
    }
}
