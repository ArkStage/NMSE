using NMSE.Core;
using NMSE.Data;
using NMSE.UI.Controls;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Small editor for the parameters of a quick menu hotkey action. The visible fields depend on
/// the action's <see cref="HotActionParameterKind"/> (reversed mapping in `_RE_Docs/hot-actions.md`).
/// </summary>
internal sealed class HotActionParameterDialog : Form
{
    private readonly InvariantNumericTextBox? _numberBox;
    private readonly InvariantNumericTextBox? _inventoryBox;
    private readonly InvariantNumericTextBox? _xBox;
    private readonly InvariantNumericTextBox? _yBox;
    private readonly TextBox? _idBox;

    /// <summary>Id value (emote ID or item ID).</summary>
    internal string Id { get; private set; } = "^";

    /// <summary>Number value (index, inventory choice, weapon mode or amount).</summary>
    internal int Number { get; private set; }

    /// <summary>InventoryIndex X.</summary>
    internal int IndexX { get; private set; } = -1;

    /// <summary>InventoryIndex Y.</summary>
    internal int IndexY { get; private set; } = -1;

    /// <summary>
    /// Creates the dialog for the given action and current parameter values.
    /// </summary>
    /// <param name="action">The action metadata.</param>
    /// <param name="displayName">Localised action name for the title.</param>
    /// <param name="id">Current Id value.</param>
    /// <param name="number">Current Number value.</param>
    /// <param name="indexX">Current InventoryIndex X.</param>
    /// <param name="indexY">Current InventoryIndex Y.</param>
    internal HotActionParameterDialog(HotActionInfo action, string displayName, string id, int number,
        int indexX, int indexY)
    {
        Text = displayName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(320, 180);
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string label, Control control)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            }, 0, layout.RowCount - 1);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 4, 0, 0);
            layout.Controls.Add(control, 1, layout.RowCount - 1);
            layout.RowCount++;
        }

        switch (action.ParameterKind)
        {
            case HotActionParameterKind.Index:
                _numberBox = new InvariantNumericTextBox { Minimum = 0, Maximum = 9999, NumericValue = number };
                AddRow(UiStrings.Get("hotkeys.index"), _numberBox);
                break;

            case HotActionParameterKind.InventoryItem:
                _inventoryBox = new InvariantNumericTextBox { Minimum = 0, Maximum = 999, NumericValue = number };
                _xBox = new InvariantNumericTextBox { Minimum = -1, Maximum = 999, NumericValue = indexX };
                _yBox = new InvariantNumericTextBox { Minimum = -1, Maximum = 999, NumericValue = indexY };
                AddRow(UiStrings.Get("hotkeys.inventory"), _inventoryBox);
                AddRow(UiStrings.Get("hotkeys.slot_x"), _xBox);
                AddRow(UiStrings.Get("hotkeys.slot_y"), _yBox);
                break;

            case HotActionParameterKind.EmoteId:
                _idBox = new TextBox { Text = id };
                AddRow(UiStrings.Get("hotkeys.emote_id"), _idBox);
                break;

            case HotActionParameterKind.WeaponMode:
                _numberBox = new InvariantNumericTextBox { Minimum = 0, Maximum = 999, NumericValue = number };
                AddRow(UiStrings.Get("hotkeys.weapon_mode"), _numberBox);
                break;

            case HotActionParameterKind.CreatureFood:
                _numberBox = new InvariantNumericTextBox { Minimum = 0, Maximum = 9999, NumericValue = number };
                _idBox = new TextBox { Text = id };
                AddRow(UiStrings.Get("hotkeys.amount"), _numberBox);
                AddRow(UiStrings.Get("hotkeys.item_id"), _idBox);
                break;

            default:
                AddRow("", new Label
                {
                    Text = UiStrings.Get("hotkeys.no_parameters"),
                    AutoSize = true,
                    Anchor = AnchorStyles.Left
                });
                break;
        }

        var okButton = new Button
        {
            Text = UiStrings.Get("common.ok"),
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0),
            Anchor = AnchorStyles.Right
        };
        var cancelButton = new Button
        {
            Text = UiStrings.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0),
            Anchor = AnchorStyles.Right
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = Padding.Empty
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        okButton.Click += (_, _) =>
        {
            Number = (int)(_numberBox?.NumericValue ?? _inventoryBox?.NumericValue ?? 0);
            IndexX = (int)(_xBox?.NumericValue ?? -1);
            IndexY = (int)(_yBox?.NumericValue ?? -1);
            Id = string.IsNullOrWhiteSpace(_idBox?.Text) ? "^" : _idBox!.Text.Trim();
        };
    }
}
