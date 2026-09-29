using System.Globalization;
using NMSE.Core;
using NMSE.Core.Utilities;
using NMSE.Data;
using NMSE.Models;
using NMSE.UI.Util;

namespace NMSE.UI.Panels;

public partial class ExocraftPanel : UserControl
{
    /// <summary>Raised when inventory data is modified by the user.</summary>
    public event EventHandler? DataModified;

    /// <summary>Raised when the user requests navigation to a JSON path in the Raw JSON Editor.</summary>
    public event EventHandler<GoToJsonEventArgs>? GoToJsonRequested;

    private static (int Index, string Name)[] VehicleTypes => ExocraftLogic.VehicleTypes;

    private JsonArray? _vehicleOwnership;
    private JsonObject? _saveData;
    private JsonObject? _savedPlayerState;
    private readonly List<int> _addedVehicleIndices = new();

    // Station data
    private JsonArray? _baseBuildingObjects;
    private JsonArray? _persistentPlayerBases;
    private readonly List<JsonObject> _individualStations = new();
    private readonly List<(JsonObject BaseEntry, JsonObject ObjectEntry, int ObjectIndex, int BaseIndex)> _baseStations = new();
    private int _currentRealityIndex;
    private GameItemDatabase? _database;

    /// <summary>True while the panel is performing an initial data load; suppresses selection commits.</summary>
    private bool _loading;

    /// <summary>The array index of the vehicle currently displayed, or -1.</summary>
    private int _currentVehicleArrayIndex = -1;

    // Track which list is currently selected
    private enum StationListType { None, Individual, Base }
    private StationListType _activeStationList = StationListType.None;

    public ExocraftPanel()
    {
        InitializeComponent();
        SetupLayout();
    }

    private void SetupLayout()
    {
        _inventoryGrid.SetIsCargoInventory(true);
        _inventoryGrid.SetSuperchargeDisabled(true);
        _inventoryGrid.SetInventoryOwnerType("Vehicle");
        _inventoryGrid.SetInventoryGroup("Vehicle");
        _inventoryGrid.SetMaxSupportedLabel("");
        _inventoryGrid.DataModified += OnInventoryDataModified;

        _techGrid.SetIsTechInventory(true);
        _techGrid.SetSuperchargeDisabled(true);
        _techGrid.SetSlotToggleDisabled(true);
        _techGrid.SetInventoryOwnerType("Vehicle");
        _techGrid.SetInventoryGroup("Vehicle");
        _techGrid.SetMaxSupportedLabel("");
        _techGrid.DataModified += OnTechDataModified;

        _invTabs.SelectedIndexChanged += OnInvTabSelectedIndexChanged;
    }

    private void OnInventoryDataModified(object? sender, EventArgs e)
    {
        DataModified?.Invoke(this, e);
    }

    private void OnTechDataModified(object? sender, EventArgs e)
    {
        DataModified?.Invoke(this, e);
    }

    private void OnInvTabSelectedIndexChanged(object? sender, EventArgs e)
    {
        _techNoteLabel.Visible = _invTabs.SelectedIndex == 1;
    }

    public void SetDatabase(GameItemDatabase? database)
    {
        _inventoryGrid.SetDatabase(database);
        _techGrid.SetDatabase(database);
        _database = database;
    }

    public void SetIconManager(IconManager? iconManager)
    {
        _inventoryGrid.SetIconManager(iconManager);
        _techGrid.SetIconManager(iconManager);
    }

    public void LoadData(JsonObject saveData)
    {
        SuspendLayout();
        _vehicleSelector.BeginUpdate();
        try
        {
            _vehicleSelector.Items.Clear();
            _addedVehicleIndices.Clear();
            _currentVehicleArrayIndex = -1;
            _inventoryGrid.LoadInventory(null);
        _techGrid.LoadInventory(null);
        _saveData = saveData;
        try
        {
            var playerState = saveData.GetObject("PlayerStateData");
            if (playerState == null) return;

            _savedPlayerState = playerState;

            _vehicleOwnership = playerState.GetArray("VehicleOwnership");
            if (_vehicleOwnership == null) return;

            foreach (var (index, name) in VehicleTypes)
            {
                if (index < _vehicleOwnership.Length)
                {
                    _vehicleSelector.Items.Add(ExocraftLogic.GetLocalisedVehicleTypeName(name));
                    _addedVehicleIndices.Add(index);
                }
            }

            _loading = true;
            try
            {
                if (_vehicleSelector.Items.Count > 0)
                    _vehicleSelector.SelectedIndex = 0;
            }
            finally { _loading = false; }

            // Third person camera (stored in CommonStateData)
            try { _thirdPersonCam.Checked = saveData.GetObject("CommonStateData")?.GetBool("UsesThirdPersonVehicleCam") ?? false; } catch { _thirdPersonCam.Checked = false; }
            // Minotaur AI pilot
            try { _minotaurAI.Checked = playerState.GetBool("VehicleAIControlEnabled"); } catch { _minotaurAI.Checked = false; }
        }
        catch { }

        // Load station data
        try
        {
            var playerState = saveData.GetObject("PlayerStateData");
            if (playerState != null)
            {
                _currentRealityIndex = 0;
                try
                {
                    var universeAddr = playerState.GetObject("UniverseAddress");
                    if (universeAddr != null)
                        _currentRealityIndex = universeAddr.GetInt("RealityIndex");
                }
                catch { }

                _baseBuildingObjects = playerState.GetArray("BaseBuildingObjects");
                _persistentPlayerBases = playerState.GetArray("PersistentPlayerBases");
            }
        }
        catch { }
        PopulateStationsLists();
        }
        finally
        {
            _vehicleSelector.EndUpdate();
            ResumeLayout(true);
        }
    }

    public void SaveData(JsonObject saveData)
    {
        try
        {
            var playerState = saveData.GetObject("PlayerStateData");
            if (playerState == null) return;

            var vehicles = playerState.GetArray("VehicleOwnership");
            if (vehicles == null || _vehicleSelector.SelectedIndex < 0) return;

            int selIdx = _vehicleSelector.SelectedIndex;
            if (selIdx < 0 || selIdx >= _addedVehicleIndices.Count) return;
            int arrIdx = _addedVehicleIndices[selIdx];

            var vehicle = vehicles.GetObject(arrIdx);
            _inventoryGrid.SaveInventory(vehicle.GetObject("Inventory"));
            _techGrid.SaveInventory(vehicle.GetObject("Inventory_TechOnly"));

            // Save vehicle name
            try { vehicle.Set("Name", _nameField.Text); } catch { }

            // Third person camera
            try { saveData.GetObject("CommonStateData")?.Set("UsesThirdPersonVehicleCam", _thirdPersonCam.Checked); } catch { }
            // Minotaur AI pilot
            try { playerState.Set("VehicleAIControlEnabled", _minotaurAI.Checked); } catch { }
        }
        catch { }
    }

    private void OnVehicleSelected(object? sender, EventArgs e)
    {
        // Commit the previously displayed vehicle's edits before switching away, so unsaved
        // changes are not overwritten by the new vehicle's data.
        if (!_loading)
            CommitCurrentVehicle();

        RedrawHelper.Suspend(this);
        SuspendLayout();
        try
        {
            if (_vehicleOwnership == null || _vehicleSelector.SelectedIndex < 0) return;
            int selIdx = _vehicleSelector.SelectedIndex;
            if (selIdx >= _addedVehicleIndices.Count) return;
            int arrIdx = _addedVehicleIndices[selIdx];
            _currentVehicleArrayIndex = arrIdx;

            var vehicle = _vehicleOwnership.GetObject(arrIdx);

            // Dynamically set owner type based on vehicle subtype for proper tech filtering.
            // Must be set BEFORE LoadInventory so the item picker filters reflect the correct owner.
            string vehicleName = GetSelectedVehicleInternalName();
            string ownerType = ExocraftLogic.GetOwnerTypeForVehicle(vehicleName);
            _inventoryGrid.BeginBatchUpdate();
            _techGrid.BeginBatchUpdate();
            try
            {
                _inventoryGrid.SetInventoryOwnerType(ownerType);
                _techGrid.SetInventoryOwnerType(ownerType);
            }
            finally
            {
                _techGrid.EndBatchUpdate();
                _inventoryGrid.EndBatchUpdate();
            }

            _inventoryGrid.LoadInventory(vehicle.GetObject("Inventory"));
            _techGrid.LoadInventory(vehicle.GetObject("Inventory_TechOnly"));

            // Load vehicle name
            try { _nameField.Text = vehicle.GetString("Name") ?? ""; } catch { _nameField.Text = ""; }

            // Primary vehicle check
            try
            {
                int primaryIdx = _savedPlayerState?.GetInt("PrimaryVehicle") ?? -1;
                _primaryVehicleCheck.Checked = (arrIdx == primaryIdx);
            }
            catch { _primaryVehicleCheck.Checked = false; }

            // Deployed status
            try
            {
                long location = 0;
                try { location = vehicle.GetLong("Location"); } catch { }
                bool deployed = location != 0;
                _deployedLabel.Text = deployed ? UiStrings.Get("exocraft.status_deployed") : UiStrings.Get("exocraft.status_not_deployed");
                _deployedLabel.ForeColor = deployed
    ? (ThemeManager.Effective == AppTheme.Dark ? ThemeColors.Dark.SuccessGreen : System.Drawing.Color.Green)
    : (ThemeManager.Effective == AppTheme.Dark ? ThemeColors.Dark.SecondaryText : System.Drawing.Color.Gray);
                _undeployBtn.Enabled = deployed;
            }
            catch { _deployedLabel.Text = ""; _undeployBtn.Enabled = false; }

            // Set export filenames based on vehicle name
            string exportName = vehicleName.Replace(' ', '_');
            var cfg = ExportConfig.Instance;
            _inventoryGrid.SetExportFileName($"{exportName}_cargo_inv{cfg.ExocraftCargoExt}");
            _techGrid.SetExportFileName($"{exportName}_tech_inv{cfg.ExocraftTechExt}");
            string cargoExportFilter = ExportConfig.BuildDialogFilter(cfg.ExocraftCargoExt, "Exocraft cargo inventory");
            string cargoImportFilter = ExportConfig.BuildImportFilter(cfg.ExocraftCargoExt, "Exocraft cargo inventory", ".exo");
            _inventoryGrid.SetExportFileFilter(cargoExportFilter, cargoImportFilter, cfg.ExocraftCargoExt.TrimStart('.'));
            string techExportFilter = ExportConfig.BuildDialogFilter(cfg.ExocraftTechExt, "Exocraft tech inventory");
            string techImportFilter = ExportConfig.BuildImportFilter(cfg.ExocraftTechExt, "Exocraft tech inventory", ".exo");
            _techGrid.SetExportFileFilter(techExportFilter, techImportFilter, cfg.ExocraftTechExt.TrimStart('.'));
        }
        catch { }
        finally
        {
            ResumeLayout(true);
            RedrawHelper.Resume(this);
        }
    }

    /// <summary>
    /// Commits the currently displayed vehicle's edits (name and global vehicle flags) into
    /// the in-memory save before the selection changes. In-memory only, no disk I/O.
    /// </summary>
    private void CommitCurrentVehicle()
    {
        if (_saveData == null || _vehicleOwnership == null) return;
        if (_currentVehicleArrayIndex < 0 || _currentVehicleArrayIndex >= _vehicleOwnership.Length) return;
        try
        {
            var vehicle = _vehicleOwnership.GetObject(_currentVehicleArrayIndex);
            if (vehicle == null) return;

            vehicle.Set("Name", _nameField.Text);
            _saveData.GetObject("CommonStateData")?.Set("UsesThirdPersonVehicleCam", _thirdPersonCam.Checked);
            _savedPlayerState?.Set("VehicleAIControlEnabled", _minotaurAI.Checked);
        }
        catch { }
    }

    private void OnExportVehicle(object? sender, EventArgs e)
    {
        try
        {
            if (_vehicleOwnership == null || _vehicleSelector.SelectedIndex < 0)
            {
                MessageBox.Show(this, UiStrings.Get("exocraft.no_vehicle_selected"), UiStrings.Get("common.export"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int selIdx = _vehicleSelector.SelectedIndex;
            if (selIdx >= _addedVehicleIndices.Count) return;
            int arrIdx = _addedVehicleIndices[selIdx];

            var vehicle = _vehicleOwnership.GetObject(arrIdx);

            var config = ExportConfig.Instance;
            var vars = new Dictionary<string, string>
            {
                ["vehicle_name"] = _nameField.Text ?? "",
                ["vehicle_type"] = GetSelectedVehicleInternalName()
            };

            using var dialog = new SaveFileDialog
            {
                Filter = ExportConfig.BuildDialogFilter(config.ExocraftExt, "Exocraft files"),
                DefaultExt = config.ExocraftExt.TrimStart('.'),
                FileName = ExportConfig.BuildFileName(config.ExocraftTemplate, config.ExocraftExt, vars)
            };

            if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                vehicle.ExportToFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiStrings.Format("common.export_failed", ex.Message), UiStrings.Get("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportVehicle(object? sender, EventArgs e)
    {
        try
        {
            if (_vehicleOwnership == null || _vehicleSelector.SelectedIndex < 0)
            {
                MessageBox.Show(this, UiStrings.Get("exocraft.no_vehicle_selected"), UiStrings.Get("common.import"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int selIdx = _vehicleSelector.SelectedIndex;
            if (selIdx >= _addedVehicleIndices.Count) return;
            int arrIdx = _addedVehicleIndices[selIdx];

            using var dialog = new OpenFileDialog
            {
                Filter = ExportConfig.BuildImportFilter(ExportConfig.Instance.ExocraftExt, "Exocraft files", ".exo")
            };

            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

            var imported = JsonObject.ImportFromFile(dialog.FileName);

            // Unwrap NomNom wrapper if present (Data -> Vehicle)
            imported = InventoryImportHelper.UnwrapNomNom(imported, "Vehicle");

            var vehicle = _vehicleOwnership.GetObject(arrIdx);

            // Replace all properties from imported vehicle
            foreach (var name in imported.Names())
                vehicle.Set(name, imported.Get(name));

            // Reload the inventories from the updated vehicle
            _inventoryGrid.LoadInventory(vehicle.GetObject("Inventory"));
            _techGrid.LoadInventory(vehicle.GetObject("Inventory_TechOnly"));

            MessageBox.Show(this, UiStrings.Get("exocraft.import_success"), UiStrings.Get("common.import"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiStrings.Format("common.import_failed", ex.Message), UiStrings.Get("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnPrimaryVehicleChanged(object? sender, EventArgs e)
    {
        if (_savedPlayerState == null || _vehicleSelector.SelectedIndex < 0) return;
        int selIdx = _vehicleSelector.SelectedIndex;
        if (selIdx >= _addedVehicleIndices.Count) return;
        int arrIdx = _addedVehicleIndices[selIdx];

        if (_primaryVehicleCheck.Checked)
        {
            try { _savedPlayerState.Set("PrimaryVehicle", arrIdx); } catch { }
        }
        else
        {
            try
            {
                int currentPrimary = _savedPlayerState.GetInt("PrimaryVehicle");
                if (currentPrimary == arrIdx)
                    _savedPlayerState.Set("PrimaryVehicle", -1);
            }
            catch { }
        }
    }

    private void OnUndeploy(object? sender, EventArgs e)
    {
        if (_vehicleOwnership == null || _vehicleSelector.SelectedIndex < 0) return;
        int selIdx = _vehicleSelector.SelectedIndex;
        if (selIdx >= _addedVehicleIndices.Count) return;
        int arrIdx = _addedVehicleIndices[selIdx];

        var result = MessageBox.Show(this, UiStrings.Get("exocraft.undeploy_confirm"), UiStrings.Get("exocraft.undeploy_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;

        try
        {
            var vehicle = _vehicleOwnership.GetObject(arrIdx);
            vehicle.Set("Location", 0);

            try
            {
                var dir = vehicle.GetArray("Direction");
                if (dir != null && dir.Length >= 4)
                {
                    dir.Set(0, 0.0);
                    dir.Set(1, 0.0);
                    dir.Set(2, 0.0);
                    dir.Set(3, -1.0);
                }
            }
            catch { }

            try
            {
                var pos = vehicle.GetArray("Position");
                if (pos != null && pos.Length >= 4)
                {
                    pos.Set(0, 0.0);
                    pos.Set(1, 0.0);
                    pos.Set(2, 0.0);
                    pos.Set(3, -1.0);
                }
            }
            catch { }

            _deployedLabel.Text = UiStrings.Get("exocraft.status_not_deployed");
            _deployedLabel.ForeColor = ThemeManager.Effective == AppTheme.Dark ? ThemeColors.Dark.SecondaryText : System.Drawing.Color.Gray;
            _undeployBtn.Enabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UiStrings.Format("exocraft.undeploy_failed", ex.Message), UiStrings.Get("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ===================== Station Logic =====================

    private void PopulateStationsLists()
    {
        _individualStationsList.BeginUpdate();
        _baseStationsList.BeginUpdate();
        try
        {
            _individualStations.Clear();
            _individualStationsList.Items.Clear();
            _baseStations.Clear();
            _baseStationsList.Items.Clear();

            // Individual stations from BaseBuildingObjects
            if (_baseBuildingObjects != null)
            {
                for (int i = 0; i < _baseBuildingObjects.Length; i++)
                {
                    var obj = _baseBuildingObjects.GetObject(i);
                    string? objectId = obj?.GetString("ObjectID");
                    if (objectId != null && objectId.Contains("GARAGE", StringComparison.OrdinalIgnoreCase))
                    {
                        _individualStations.Add(obj!);
                        string displayName = GetStationDisplayName(objectId);
                        _individualStationsList.Items.Add(displayName);
                    }
                }
            }

            // Stations from PersistentPlayerBases.Objects
            if (_persistentPlayerBases != null)
            {
                for (int baseIdx = 0; baseIdx < _persistentPlayerBases.Length; baseIdx++)
                {
                    var baseEntry = _persistentPlayerBases.GetObject(baseIdx);
                    if (baseEntry == null) continue;
                    var objects = baseEntry.GetArray("Objects");
                    if (objects == null) continue;

                    for (int objIdx = 0; objIdx < objects.Length; objIdx++)
                    {
                        var obj = objects.GetObject(objIdx);
                        string? objectId = obj?.GetString("ObjectID");
                        if (objectId != null && objectId.Contains("GARAGE", StringComparison.OrdinalIgnoreCase))
                        {
                            _baseStations.Add((baseEntry, obj!, objIdx, baseIdx));
                            string displayName = GetStationDisplayName(objectId);
                            _baseStationsList.Items.Add(displayName);
                        }
                    }
                }
            }
        }
        finally
        {
            _individualStationsList.EndUpdate();
            _baseStationsList.EndUpdate();
        }
    }

    private string GetStationDisplayName(string objectId)
    {
        string lookupId = objectId.StartsWith("^", StringComparison.Ordinal) ? objectId[1..] : objectId;
        if (_database != null)
        {
            var item = _database.GetItem(lookupId);
            if (item != null)
                return item.Name;
        }
        return objectId;
    }

    /// <summary>
    /// Extracts the 12-hex-digit portal code from a normalised GalacticAddress hex string.
    /// Handles both 12-digit (portal code format) and 14-digit (UniverseAddress format with RealityIndex).
    /// </summary>
    private static string ExtractPortalCode(string galacticAddrHex)
    {
        if (string.IsNullOrEmpty(galacticAddrHex) || !galacticAddrHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return "";

        string raw = galacticAddrHex[2..];
        if (raw.Length == 12)
            return raw;
        if (raw.Length == 14)
            return string.Concat(raw.AsSpan(0, 4), raw.AsSpan(6, 8));
        return "";
    }

    /// <summary>
    /// Extracts the reality index (galaxy index) from a normalised GalacticAddress hex string.
    /// The 14-hex-digit UniverseAddress format embeds a 2-digit reality index at positions 4-5
    /// (0-indexed after the "0x" prefix). Returns null for 12-digit (portal-code-only) addresses
    /// or if the address cannot be parsed.
    /// </summary>
    private static int? GetRealityIndexFromAddress(string galacticAddrHex)
    {
        if (string.IsNullOrEmpty(galacticAddrHex))
            return null;

        string raw = galacticAddrHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? galacticAddrHex[2..]
            : galacticAddrHex;

        // Only 14-hex-digit addresses contain reality index info
        if (raw.Length == 14
            && int.TryParse(raw.AsSpan(4, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int reality))
        {
            return reality;
        }

        return null;
    }

    private static string FormatTimestamp(long timestamp)
    {
        if (timestamp <= 0) return UiStrings.Get("common.unknown");
        try
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
            return dt.ToString("g", CultureInfo.CurrentCulture);
        }
        catch
        {
            return timestamp.ToString(CultureInfo.CurrentCulture);
        }
    }

    private static string FormatPosition(JsonArray? positionArray)
    {
        if (positionArray == null || positionArray.Length < 3)
            return UiStrings.Get("common.unknown");

        try
        {
            double x = positionArray.GetDouble(0);
            double y = positionArray.GetDouble(1);
            double z = positionArray.GetDouble(2);
            return $"X: {x.ToString("F2", CultureInfo.CurrentCulture)}, Y: {y.ToString("F2", CultureInfo.CurrentCulture)}, Z: {z.ToString("F2", CultureInfo.CurrentCulture)}";
        }
        catch
        {
            return UiStrings.Get("common.unknown");
        }
    }

    private void ClearStationDetail()
    {
        _stationNameValue.Text = "";
        _stationTimestampValue.Text = "";
        _stationBaseNameValue.Text = "";
        _stationGalacticAddrValue.Text = "";
        _stationRegionSeedLabel.Visible = true;
        _stationRegionSeedValue.Text = "";
        _stationRegionSeedValue.Visible = true;
        _stationPositionValue.Text = "";
        _stationGalaxyValue.Text = "";
        _stationGalaxyDotLabel.Image?.Dispose();
        _stationGalaxyDotLabel.Image = null;
        _stationPortalCodeValue.Text = "";
        _stationPortalCodeDecValue.Text = "";
        _stationSignalBoosterValue.Text = "";
        CoordinateHelper.UpdateGlyphPanel(_stationGlyphPanel, "");
        _stationVoxelXValue.Text = "";
        _stationVoxelYValue.Text = "";
        _stationVoxelZValue.Text = "";
        _stationSolarSystemValue.Text = "";
        _stationPlanetValue.Text = "";
        _deleteStationBtn.Enabled = false;
    }

    private void PopulateStationDetail(
        string objectId,
        long timestamp,
        string baseName,
        string galacticAddrHex,
        string? regionSeedDisplay,
        JsonArray? positionArray,
        string portalCode,
        int vx, int vy, int vz, int si, int pi,
        bool showRegionSeed,
        int? realityIndex)
    {
        _stationNameValue.Text = GetStationDisplayName(objectId);
        _stationTimestampValue.Text = FormatTimestamp(timestamp);
        _stationBaseNameValue.Text = baseName;
        _stationGalacticAddrValue.Text = galacticAddrHex;
        _stationRegionSeedLabel.Visible = showRegionSeed;
        _stationRegionSeedValue.Visible = showRegionSeed;
        _stationRegionSeedValue.Text = regionSeedDisplay ?? UiStrings.Get("common.unknown");
        _stationPositionValue.Text = FormatPosition(positionArray);

        // Galaxy display - use the reality index from the station/base address,
        // not the player's current galaxy. If the address is only a 12-digit portal
        // code (no galaxy info embedded), show a clear message.
        if (realityIndex.HasValue)
        {
            int ri = realityIndex.Value;
            string galaxyType = GalaxyDatabase.GetGalaxyType(ri);
            _stationGalaxyValue.Text = $"{GalaxyDatabase.GetGalaxyDisplayName(ri)} ({galaxyType})";
            GalaxyDisplayHelper.ConfigureGalaxyDotLabel(_stationGalaxyDotLabel, ri);
        }
        else
        {
            _stationGalaxyValue.Text = UiStrings.Get("exocraft.station_no_galaxy_info");
            _stationGalaxyDotLabel.Image?.Dispose();
            _stationGalaxyDotLabel.Image = null;
        }

        // Portal code
        _stationPortalCodeValue.Text = portalCode;
        _stationPortalCodeDecValue.Text = CoordinateHelper.PortalHexToDec(portalCode);
        _stationSignalBoosterValue.Text = CoordinateHelper.VoxelToSignalBooster(vx, vy, vz, si);
        CoordinateHelper.UpdateGlyphPanel(_stationGlyphPanel, portalCode);

        // Voxel / solar / planet
        _stationVoxelXValue.Text = vx.ToString(CultureInfo.CurrentCulture);
        _stationVoxelYValue.Text = vy.ToString(CultureInfo.CurrentCulture);
        _stationVoxelZValue.Text = vz.ToString(CultureInfo.CurrentCulture);
        _stationSolarSystemValue.Text = si.ToString(CultureInfo.CurrentCulture);
        _stationPlanetValue.Text = pi.ToString(CultureInfo.CurrentCulture);

        _deleteStationBtn.Enabled = true;
    }

    private void OnIndividualStationSelected(object? sender, EventArgs e)
    {
        // Deselect the other list
        _baseStationsList.SelectedIndexChanged -= OnBaseStationSelected;
        _baseStationsList.ClearSelected();
        _baseStationsList.SelectedIndexChanged += OnBaseStationSelected;

        int idx = _individualStationsList.SelectedIndex;
        if (idx < 0 || idx >= _individualStations.Count)
        {
            _activeStationList = StationListType.None;
            ClearStationDetail();
            return;
        }

        _activeStationList = StationListType.Individual;
        var entry = _individualStations[idx];

        string? objectId = "";
        long timestamp = 0;
        try { objectId = entry.GetString("ObjectID") ?? ""; } catch { }
        try { timestamp = entry.GetLong("Timestamp"); } catch { try { timestamp = entry.GetInt("Timestamp"); } catch { } }

        string galacticAddrHex = "";
        string portalCode = "";
        int vx = 0, vy = 0, vz = 0, si = 0, pi = 0;

        try
        {
            var addr = entry.Get("GalacticAddress");
            galacticAddrHex = CoordinateHelper.NormalizeGalacticAddress(addr);
            portalCode = ExtractPortalCode(galacticAddrHex);
            var parsed = ExocraftLogic.ParseGalacticAddressToVoxel(addr);
            if (parsed.HasValue)
            {
                vx = parsed.Value.VoxelX;
                vy = parsed.Value.VoxelY;
                vz = parsed.Value.VoxelZ;
                si = parsed.Value.SolarSystemIndex;
                pi = parsed.Value.PlanetIndex;
            }
        }
        catch { }

        string? regionSeedStr = null;
        try
        {
            var rs = entry.Get("RegionSeed");
            regionSeedStr = rs?.ToString();
        }
        catch { }

        JsonArray? positionArray = null;
        try { positionArray = entry.GetArray("Position"); } catch { }

        int? realityIndex = GetRealityIndexFromAddress(galacticAddrHex);
        PopulateStationDetail(objectId, timestamp, UiStrings.Get("exocraft.station_not_in_base"), galacticAddrHex, regionSeedStr, positionArray,
            portalCode, vx, vy, vz, si, pi, showRegionSeed: true, realityIndex: realityIndex);
    }

    private void OnBaseStationSelected(object? sender, EventArgs e)
    {
        // Deselect the other list
        _individualStationsList.SelectedIndexChanged -= OnIndividualStationSelected;
        _individualStationsList.ClearSelected();
        _individualStationsList.SelectedIndexChanged += OnIndividualStationSelected;

        int idx = _baseStationsList.SelectedIndex;
        if (idx < 0 || idx >= _baseStations.Count)
        {
            _activeStationList = StationListType.None;
            ClearStationDetail();
            return;
        }

        _activeStationList = StationListType.Base;
        var (baseEntry, objEntry, _, _) = _baseStations[idx];

        string? objectId = "";
        long timestamp = 0;
        try { objectId = objEntry.GetString("ObjectID") ?? ""; } catch { }
        try { timestamp = objEntry.GetLong("Timestamp"); } catch { try { timestamp = objEntry.GetInt("Timestamp"); } catch { } }

        // GalacticAddress comes from the base entry, not the object
        string galacticAddrHex = "";
        string portalCode = "";
        int vx = 0, vy = 0, vz = 0, si = 0, pi = 0;

        try
        {
            var addr = baseEntry.Get("GalacticAddress");
            galacticAddrHex = CoordinateHelper.NormalizeGalacticAddress(addr);
            portalCode = ExtractPortalCode(galacticAddrHex);
            var parsed = ExocraftLogic.ParseGalacticAddressToVoxel(addr);
            if (parsed.HasValue)
            {
                vx = parsed.Value.VoxelX;
                vy = parsed.Value.VoxelY;
                vz = parsed.Value.VoxelZ;
                si = parsed.Value.SolarSystemIndex;
                pi = parsed.Value.PlanetIndex;
            }
        }
        catch { }

        JsonArray? positionArray = null;
        try { positionArray = objEntry.GetArray("Position"); } catch { }

        // Resolve base name
        string baseName = "";
        try
        {
            string? rawName = baseEntry.GetString("Name");
            if (!string.IsNullOrEmpty(rawName))
                baseName = rawName;
            else
                baseName = UiStrings.Format("base.fallback_base_name", _baseStations[idx].BaseIndex + 1);
        }
        catch
        {
            baseName = UiStrings.Format("base.fallback_base_name", _baseStations[idx].BaseIndex + 1);
        }

        int? realityIndex = GetRealityIndexFromAddress(galacticAddrHex);
        PopulateStationDetail(objectId, timestamp, baseName, galacticAddrHex, null, positionArray,
            portalCode, vx, vy, vz, si, pi, showRegionSeed: false, realityIndex: realityIndex);
    }

    private void OnDeleteStation(object? sender, EventArgs e)
    {
        switch (_activeStationList)
        {
            case StationListType.Individual:
                DeleteIndividualStation();
                break;
            case StationListType.Base:
                DeleteBaseStation();
                break;
            default:
                MessageBox.Show(this, UiStrings.Get("exocraft.no_station_selected"), UiStrings.Get("common.error"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                break;
        }
    }

    private void DeleteIndividualStation()
    {
        int idx = _individualStationsList.SelectedIndex;
        if (idx < 0 || idx >= _individualStations.Count || _baseBuildingObjects == null) return;

        var result = MessageBox.Show(this, UiStrings.Get("exocraft.delete_station_confirm"),
            UiStrings.Get("exocraft.delete_station_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            // Find the index in the actual BaseBuildingObjects array
            var target = _individualStations[idx];
            int arrIdx = -1;
            for (int i = 0; i < _baseBuildingObjects.Length; i++)
            {
                if (_baseBuildingObjects.GetObject(i) == target)
                {
                    arrIdx = i;
                    break;
                }
            }

            if (arrIdx >= 0)
            {
                _baseBuildingObjects.RemoveAt(arrIdx);
                _individualStationsList.ClearSelected();
                _activeStationList = StationListType.None;
                ClearStationDetail();
                PopulateStationsLists();
                DataModified?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
    }

    private void DeleteBaseStation()
    {
        int idx = _baseStationsList.SelectedIndex;
        if (idx < 0 || idx >= _baseStations.Count) return;

        var (baseEntry, _, objectIndex, _) = _baseStations[idx];

        var result = MessageBox.Show(this, UiStrings.Get("exocraft.delete_base_station_confirm"),
            UiStrings.Get("exocraft.delete_station_title"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        try
        {
            var objects = baseEntry.GetArray("Objects");
            if (objects != null && objectIndex >= 0 && objectIndex < objects.Length)
            {
                objects.RemoveAt(objectIndex);
                _baseStationsList.ClearSelected();
                _activeStationList = StationListType.None;
                ClearStationDetail();
                PopulateStationsLists();
                DataModified?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
    }

    // ===================== Localisation =====================

    public void ApplyUiLocalisation()
    {
        _titleLabel.Text = UiStrings.Get("exocraft.title");
        _vehicleLabel.Text = UiStrings.Get("exocraft.vehicle");
        _nameLabel.Text = UiStrings.Get("exocraft.name");
        _exportBtn.Text = UiStrings.Get("exocraft.export");
        _importBtn.Text = UiStrings.Get("exocraft.import");
        _thirdPersonCam.Text = UiStrings.Get("exocraft.third_person");
        _minotaurAI.Text = UiStrings.Get("exocraft.minotaur_ai");
        _techNoteLabel.Text = UiStrings.Get("exocraft.supercharge_note");
        _invPage.Text = UiStrings.Get("common.cargo");
        _techPage.Text = UiStrings.Get("common.technology");
        _primaryVehicleCheck.Text = UiStrings.Get("exocraft.primary_vehicle");
        _undeployBtn.Text = UiStrings.Get("exocraft.undeploy_title");

        // Tab page localisation
        _exocraftsTabPage.Text = UiStrings.Get("exocraft.tab_exocrafts");
        _stationsTabPage.Text = UiStrings.Get("exocraft.tab_stations");
        _individualStationsLabel.Text = UiStrings.Get("exocraft.individual_stations");
        _baseStationsLabel.Text = UiStrings.Get("exocraft.part_of_bases");
        _stationNameLabel.Text = UiStrings.Get("exocraft.station_name");
        _stationTimestampLabel.Text = UiStrings.Get("exocraft.station_timestamp");
        _stationBaseNameLabel.Text = UiStrings.Get("exocraft.station_base_name");
        _stationGalacticAddrLabel.Text = UiStrings.Get("exocraft.station_galactic_address");
        _stationRegionSeedLabel.Text = UiStrings.Get("exocraft.station_region_seed");
        _stationPositionLabel.Text = UiStrings.Get("exocraft.station_position");
        _coordSectionLabel.Text = UiStrings.Get("exocraft.station_coordinates");
        _stationGalaxyLabel.Text = UiStrings.Get("exocraft.station_galaxy");
        _stationPortalCodeLabel.Text = UiStrings.Get("exocraft.station_portal_code");
        _stationPortalCodeDecLabel.Text = UiStrings.Get("exocraft.station_portal_code_dec");
        _stationSignalBoosterLabel.Text = UiStrings.Get("exocraft.station_signal_booster");
        _stationVoxelXLabel.Text = UiStrings.Get("exocraft.station_voxel_x");
        _stationVoxelYLabel.Text = UiStrings.Get("exocraft.station_voxel_y");
        _stationVoxelZLabel.Text = UiStrings.Get("exocraft.station_voxel_z");
        _stationSolarSystemLabel.Text = UiStrings.Get("exocraft.station_solar_system");
        _stationPlanetLabel.Text = UiStrings.Get("exocraft.station_planet");
        _deleteStationBtn.Text = UiStrings.Get("exocraft.delete_station");

        RefreshVehicleCombo();
        _inventoryGrid.ApplyUiLocalisation();
        _techGrid.ApplyUiLocalisation();
        new ToolTip().SetToolTip(_gotoVehicleListBtn, UiStrings.Format("goto_json.tooltip_section", _titleLabel.Text));
        new ToolTip().SetToolTip(_gotoVehicleCargoBtn, UiStrings.Format("goto_json.tooltip_section", _invPage.Text));
        new ToolTip().SetToolTip(_gotoBaseBuildingBtn, UiStrings.Format("goto_json.tooltip_section", _individualStationsLabel.Text));
        new ToolTip().SetToolTip(_gotoBaseStationsBtn, UiStrings.Format("goto_json.tooltip_section", UiStrings.Get("goto_json.nav_base_stations")));
    }

    private string GetSelectedVehicleInternalName()
    {
        int selIdx = _vehicleSelector.SelectedIndex;
        if (selIdx < 0 || selIdx >= _addedVehicleIndices.Count) return "vehicle";
        int arrIdx = _addedVehicleIndices[selIdx];
        foreach (var (index, name) in VehicleTypes)
        {
            if (index == arrIdx) return name;
        }
        return "vehicle";
    }

    private void RefreshVehicleCombo()
    {
        int currentSel = _vehicleSelector.SelectedIndex;
        _vehicleSelector.Items.Clear();
        foreach (var idx in _addedVehicleIndices)
        {
            string internalName = "vehicle";
            foreach (var (vIdx, name) in VehicleTypes)
            {
                if (vIdx == idx) { internalName = name; break; }
            }
            _vehicleSelector.Items.Add(ExocraftLogic.GetLocalisedVehicleTypeName(internalName));
        }
        if (currentSel >= 0 && currentSel < _vehicleSelector.Items.Count)
            _vehicleSelector.SelectedIndex = currentSel;
    }

    private void OnGoToJsonVehicleListClicked(object? sender, EventArgs e)
    {
        GoToJsonRequested?.Invoke(this, new GoToJsonEventArgs("PlayerStateData", "VehicleOwnership"));
    }

    private void OnGoToJsonVehicleCargoClicked(object? sender, EventArgs e)
    {
        int selectorIdx = _vehicleSelector.SelectedIndex;
        if (selectorIdx < 0 || selectorIdx >= _addedVehicleIndices.Count) return;

        int arrayIndex = _addedVehicleIndices[selectorIdx];
        GoToJsonRequested?.Invoke(this, new GoToJsonEventArgs("PlayerStateData", "VehicleOwnership", $"[{arrayIndex}]", "Inventory"));
    }

    private void OnGoToJsonBaseBuildingObjectsClicked(object? sender, EventArgs e)
    {
        GoToJsonRequested?.Invoke(this, new GoToJsonEventArgs("PlayerStateData", "BaseBuildingObjects"));
    }

    private void OnGoToJsonBaseStationsClicked(object? sender, EventArgs e)
    {
        GoToJsonRequested?.Invoke(this, new GoToJsonEventArgs("PlayerStateData", "PersistentPlayerBases"));
    }
}
