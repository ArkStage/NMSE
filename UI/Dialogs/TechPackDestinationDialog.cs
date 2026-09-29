using System.Drawing;
using System.Windows.Forms;
using NMSE.Core;
using NMSE.Data;

namespace NMSE.UI.Dialogs;

/// <summary>
/// Dialog for choosing a destination inventory and free slot when packing or
/// unpacking a technology.
/// </summary>
internal sealed class TechPackDestinationDialog : Form
{
    /// <summary>Gets the chosen destination inventory, or null when the dialog was cancelled.</summary>
    public TechPackLogic.TechPackDestination? SelectedDestination { get; private set; }

    /// <summary>Gets the chosen slot X coordinate.</summary>
    public int SelectedX { get; private set; }

    /// <summary>Gets the chosen slot Y coordinate.</summary>
    public int SelectedY { get; private set; }

    private readonly ListBox _inventoryList;
    private readonly ListBox _slotList;
    private readonly Button _okButton;
    private readonly List<(int X, int Y)> _positions = new();

    /// <summary>
    /// Creates the destination picker with the given candidate inventories.
    /// </summary>
    /// <param name="title">Dialog title, e.g. the localised "Pack Technology" label.</param>
    /// <param name="destinations">Candidate destinations, each with at least one free slot.</param>
    public TechPackDestinationDialog(string title, IReadOnlyList<TechPackLogic.TechPackDestination> destinations)
    {
        Text = title;
        Size = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var inventoryGroup = new GroupBox
        {
            Text = UiStrings.Get("techpack.choose_inventory"),
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        _inventoryList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        inventoryGroup.Controls.Add(_inventoryList);

        var slotGroup = new GroupBox
        {
            Text = UiStrings.Get("techpack.choose_slot"),
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        _slotList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };
        slotGroup.Controls.Add(_slotList);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        layout.Controls.Add(inventoryGroup, 0, 0);
        layout.Controls.Add(slotGroup, 1, 0);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        _okButton = new Button
        {
            Text = UiStrings.Get("common.ok"),
            DialogResult = DialogResult.OK,
            Width = 110,
            Enabled = false,
            Margin = new Padding(6, 0, 0, 0)
        };
        var cancelButton = new Button
        {
            Text = UiStrings.Get("common.cancel"),
            DialogResult = DialogResult.Cancel,
            Width = 110
        };
        buttonPanel.Controls.Add(_okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = _okButton;
        CancelButton = cancelButton;

        foreach (var destination in destinations)
        {
            _inventoryList.Items.Add(new DestinationItem(destination));
        }

        _inventoryList.SelectedIndexChanged += (_, _) => PopulateSlots();
        _slotList.SelectedIndexChanged += (_, _) => _okButton.Enabled = _slotList.SelectedIndex >= 0;
        _slotList.DoubleClick += (_, _) =>
        {
            if (_slotList.SelectedIndex >= 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        _okButton.Click += (_, _) => ApplySelection();

        if (_inventoryList.Items.Count > 0)
            _inventoryList.SelectedIndex = 0;
    }

    /// <summary>Fills the slot list for the selected destination inventory.</summary>
    private void PopulateSlots()
    {
        _slotList.Items.Clear();
        _positions.Clear();
        _okButton.Enabled = false;

        if (_inventoryList.SelectedItem is not DestinationItem item)
            return;

        _positions.AddRange(TechPackLogic.GetFreePositions(item.Destination.Inventory, item.Destination.Slots));
        foreach (var (x, y) in _positions)
        {
            _slotList.Items.Add(UiStrings.Format("techpack.slot_label", x, y));
        }

        if (_slotList.Items.Count > 0)
            _slotList.SelectedIndex = 0;
    }

    /// <summary>Captures the chosen destination and slot when the user confirms.</summary>
    private void ApplySelection()
    {
        if (_inventoryList.SelectedItem is not DestinationItem item)
            return;
        int slotIndex = _slotList.SelectedIndex;
        if (slotIndex < 0 || slotIndex >= _positions.Count)
            return;

        SelectedDestination = item.Destination;
        SelectedX = _positions[slotIndex].X;
        SelectedY = _positions[slotIndex].Y;
    }

    /// <summary>List entry wrapper that formats a destination's localised display name.</summary>
    private sealed class DestinationItem
    {
        public TechPackLogic.TechPackDestination Destination { get; }

        private readonly string _display;

        public DestinationItem(TechPackLogic.TechPackDestination destination)
        {
            Destination = destination;
            _display = UiStrings.Format(destination.NameKey, destination.NameArgs);
        }

        public override string ToString() => _display;
    }
}
