# UI Localisation

## Overview

NMSE has two independent localisation layers:

1. **UI strings** - the application's own text: menus, tabs, dialog messages, toolbar
   labels, status bar text, panel labels, section headers, grid column headers, context
   menus, no-save overlay messages and all other user-visible controls. These live in
   `Resources/ui/lang/{bcp47}.json` and are served by the static `Data/UiStrings.cs` class.
2. **Game-data strings** - item names, descriptions, recipe names, traits, perks and
   similar game text. These are resolved from NMS internal localisation keys (`_LocStr`)
   against `Resources/json/lang/{bcp47}.json` through `Data/LocalisationService.cs`.

This document covers both, starting with the UI strings.

## Architecture

### UiStrings Static Class

`Data/UiStrings.cs` is a static class with no mutable instance state. The dictionaries are
replaced atomically on load, so concurrent reads are safe, but callers should not cache
dictionary references across language switches.

| Member | Description |
|--------|-------------|
| `SetDirectory(path)` | Sets the directory containing the UI string JSON files |
| `Load(bcp47Tag)` (`bool`) | Loads a language; always reloads `en-GB.json` as the fallback first. Returns `false` when the directory is unset or the language file is missing or empty. `null`, `""` or `"en-GB"` selects English and returns `true` |
| `Get(key)` (`string`) | Returns the localised string, the English fallback, or the raw key |
| `GetOrNull(key)` (`string?`) | Returns the localised string, or `null` if the key is missing everywhere |
| `Format(key, args)` (`string`) | Calls `Get(key)` then `string.Format` with `InvariantCulture`; returns the unformatted template if the placeholders do not match |
| `TotalKeyCount` (`int`) | Number of keys in the loaded English fallback |
| `TranslatedCount` (`int`) | Number of keys in the active language; `0` while English is active |
| `IsActive` (`bool`) | Whether a non-English language is currently loaded |
| `Reset()` | Clears all state; `internal`, used by unit tests |

### File Location

App UI strings are stored in `Resources/ui/lang/{bcp47}.json`:

```
Resources/
  ui/
    lang/
      en-GB.json    - source of truth (English)
      en-US.json
      fr-FR.json
      ...
      (16 files in total)
```

Each file is a flat JSON object (string to string) with no nesting. The files are copied
to the output directory because `Resources\ui\**\*` is a `Content` item in `NMSE.csproj`,
and `MainForm.LoadDatabase()` points `UiStrings` at `Resources/ui/lang`.

The game language dumps live in a separate directory, `Resources/json/lang/`; see
"Game-Data Localisation" below. `UiStrings` never reads that directory.

### Loading and Fallback Behaviour

`Load(tag)` performs these steps:

1. Reloads `en-GB.json` from the configured directory into the fallback dictionary
   (when the file exists).
2. For `null`, `""` or `"en-GB"`, points the active dictionary at the fallback and
   returns `true`.
3. Otherwise loads `{tag}.json`. If it is missing, empty or unparsable, the active
   dictionary is left pointing at the English fallback and `Load` returns `false`.
4. On success, the translated dictionary becomes active and `Load` returns `true`.

Lookups use a three-step fallback chain:

```
active language -> English fallback -> raw key string
```

Returning the raw key makes untranslated strings easy to spot during development.
`GetOrNull` stops at `null` instead of returning the raw key.

`Format` uses `CultureInfo.InvariantCulture` for argument substitution and catches
`FormatException`, returning the unformatted template. A bad translation therefore
cannot crash the UI.

### Key Format

Keys use dot-delimited hierarchical naming:

```json
{
  "menu.file": "&File",
  "menu.file.save": "&Save",
  "tab.player": "Player",
  "player.health": "Health:",
  "status.loaded_save": "Loaded: {0} ({1}ms)",
  "dialog.save_success": "Save file written successfully!",
  "settlement.perk": "Perk {0}:",
  "bytebeat.data_channel": "Data [{0}]:"
}
```

#### Key Prefixes

| Prefix | Scope |
|--------|-------|
| `_meta.*` | File metadata (`description`, `version`); not read at runtime |
| `menu.*` | Main menu items (File, Edit, Tools, Language, Theme, Help) |
| `tab.*` | Top-level tab page titles (all 16 tabs) |
| `toolbar.*` | Toolbar labels: Directory, Browse, Backup (`toolbar.backup`), Save Slot, File, Load, Save |
| `status.*` | Status bar messages, item counts and language-switch summaries |
| `dialog.*` | Dialog and message box text, file dialog titles/filters, backup restore picker |
| `common.*` | Shared strings (Cargo, Technology, filters, Unknown, Max Supported, Close, OK, Cancel) |
| `player.*` | Player panel labels, sub-tabs (General, Guide, Titles, Multiplayer), statistics, coordinates, save info |
| `starship.*` | Starship panel labels, buttons and the Customisation tab |
| `multitool.*` | Multi-tool panel labels and buttons |
| `freighter.*` | Freighter panel labels and rooms |
| `settlement.*` | Settlement panel labels, stats, building tabs |
| `companion.*` | Companion panel labels, traits, seeds, accessories, battle tab |
| `exocraft.*` | Exocraft panel labels plus the Summoning Stations sub-tab (`exocraft.tab_stations`, `exocraft.station_*`) |
| `frigate.*` | Frigate panel labels, stats and traits |
| `squadron.*` | Squadron panel labels |
| `discovery.*` | Catalogue panel labels, tabs, columns and completion sub-panels (the panel keeps the historical `discovery.*` prefix from the former Discovery naming) |
| `milestone.*` | Milestone panel labels and sections |
| `bytebeat.*` | ByteBeat panel labels and sections |
| `account.*` | Account/rewards panel and the consistency check dialog |
| `base.*` | Base panel tabs and export/import actions |
| `inventory.*` | Inventory grid labels, context menu and inline item picker |
| `item_picker.*` | Item picker dialog buttons, columns and placeholders |
| `item_category.*` | Item category display names (inventory detail panel, Database Search) |
| `item_type.*` | Item type display names (inventory detail panel, Database Search) |
| `location_type.*` | Discovery location type names (Catalogue panel) |
| `recipe.*` | Recipe panel columns |
| `raw_json.*` | JSON editor labels, context menu and node export/import |
| `export_config.*` | Export config tabs and buttons |
| `fleet.*` | Fleet panel sub-tabs (Freighter, Frigates, Squadron) |
| `exosuit.*` | Exosuit sub-tabs |
| `multiplayer.*` | Multiplayer experimental panel (hosted inside the Player tab) |
| `outfits.*` | Outfit slot section in the Player tab |
| `db_search.*` | Database Search panel |
| `goto_json.*` | "Open in Raw JSON Editor" button tooltips and navigation names |
| `lock.*` | No-save overlay messages (`lock.title`, `lock.hint`) |
| `slot.*` | Save slot tags (for example `slot.expedition`) |
| `update.*` | Update check, download and apply messages |
| `theme_menu`, `theme_system`, `theme_light`, `theme_dark` | Theme menu labels (unprefixed keys) |
| `app.*` | Application-level strings (window title format) |

### Integration Points

1. **`MainForm.LoadDatabase()`** - calls `UiStrings.SetDirectory()` with
   `Resources/ui/lang`, and `LocalisationService.SetLangDirectory()` with
   `Resources/json/lang`.
2. **`MainForm.ApplyStartupLanguage()`** - invoked from `PerformStartup()` after
   `LoadDatabase()`. Loads the saved `AppConfig.Language`, calls `UiStrings.Load(tag)` and
   `_localisationService.LoadLanguage(tag)`, runs `ApplyLocalisation()` on the game
   databases, then calls `ApplyUiLocalisation()`.
3. **`MainForm.OnLanguageSelected()`** - does the same work when the user picks a
   language from the Language menu, refreshes the loaded panels, then persists the choice
   to `AppConfig`.
4. **`MainForm.ApplyUiLocalisation()`** - pushes strings to:
   - the File, Edit, Tools, Language, Theme and Help menus (Language menu items keep
     their BCP 47 tags as text);
   - toolbar labels on both rows, including `toolbar.backup` and the backup Browse
     button;
   - all 16 tab titles via the `tab.*` keys;
   - the status bar database item count (`status.total_db_items`);
   - 17 panel/control classes directly: `_mainStatsPanel`, `_milestonePanel`,
     `_cataloguePanel`, `_settlementPanel`, `_byteBeatPanel`, `_accountPanel`,
     `_recipePanel`, `_rawJsonPanel`, `_databaseSearchPanel`, `_exportConfigPanel`,
     `_exosuitPanel`, `_companionPanel`, `_basePanel`, `_fleetPanel`, `_vehiclePanel`,
     `_multitoolPanel` and `_shipPanel`;
   - `NoSaveOverlay.RefreshLocalisation()` for every locked tab.
5. **Panel cascades** - some panels forward to their children: `FleetPanel` calls
   `ApplyUiLocalisation()` on `FreighterPanel`, `FrigatePanel` and `SquadronPanel`;
   `MainStatsPanel` calls `MultiplayerPanel`; `BasePanel` calls its four sub-panels;
   `CataloguePanel` calls its completion sub-panels.
6. **Panel `ApplyUiLocalisation()` methods** - 28 panel/control classes define at least
   one (`BasePanel` defines four, one per sub-panel). They update form labels, section
   headers, buttons, tab sub-pages, grid column headers, context menu items,
   filter/search placeholders and dynamic count labels (via `UiStrings.Format()`).
7. **Status bar** - `_totalDatabaseItems` includes `UiStrings.TotalKeyCount`, and the
   language-switch status message uses `status.language_localised` with five arguments
   (tag, item count, reward count, word count, UI string count).

### Panel Label Pattern

There is no single pattern; the common ones are:

- Helper-based designers store labels returned by `AddRow()` / `AddSectionHeader()` in
  private fields, and the panel's `ApplyUiLocalisation()` updates those fields.
- Some designers read `UiStrings.Get()` directly when the control is created and refresh
  the text again on language change.
- A few panels keep batches of labels in a collection and iterate them (for example
  `SpaceStationPanel` uses a `List<(string, Label)>`), and owner-drawn or composite
  controls expose their own `RefreshLocalisation()` method (for example `NoSaveOverlay`).

The helper pattern looks like this:

```csharp
// In Designer.cs - store the label reference:
_healthLabel = AddRow(leftLayout, "Health:", _healthField, leftRow++);

// In Panel.cs - update on language change:
public void ApplyUiLocalisation()
{
    _healthLabel.Text = UiStrings.Get("player.health");
}
```

### Accelerator Keys (Mnemonics)

`&` is the WinForms mnemonic marker. It is preserved as-is in the JSON and applied
directly to control `.Text` properties:

- Menu items in `en-GB` use them, for example `"menu.file": "&File"`.
- Top-level `tab.*` titles in `en-GB` do not use them, for example `tab.player` is
  `"Player"` and `tab.starships` is `"Starships"`.
- Translations may include `&` where the translator chose one; German uses `"&Datei"`
  for `menu.file`. Not every menu item has a mnemonic, even in `en-GB`.

### Safety

- **No logic impact:** UI strings are display-only. All filtering, comparison and save
  I/O logic uses English internal values or stable IDs from the data layer.
- **Fallback chain:** active language -> English fallback -> raw key string.
- **Format safety:** `UiStrings.Format()` catches `FormatException` from mismatched
  placeholders and returns the unformatted template. Substitutions use invariant culture,
  so numeric and date arguments are not shifted by the user's locale.
- **Missing-file safety:** a missing or broken translation file leaves the English
  fallback active rather than clearing the string tables.

## Supported Languages

The Language menu follows `LocalisationService.SupportedLanguages`, in this order:

| BCP 47 | Language | Notes |
|--------|----------|-------|
| en-GB | English (Great Britain) | Source of truth |
| fr-FR | French | |
| it-IT | Italian | |
| de-DE | German | |
| es-ES | Spanish (Spain) | |
| ru-RU | Russian | |
| pl-PL | Polish | |
| nl-NL | Dutch | |
| pt-PT | Portuguese (Portugal) | |
| es-419 | Latin American Spanish | |
| pt-BR | Brazilian Portuguese | |
| zh-CN | Simplified Chinese | |
| zh-TW | Traditional Chinese | |
| ko-KR | Korean | |
| ja-JP | Japanese | |
| en-US | English (United States) | Last entry in the menu |

## Adding New Strings

1. Add the English key and value to `Resources/ui/lang/en-GB.json` first. This file is
   the source of truth and defines `TotalKeyCount`.
2. Add the same key to all 15 other language files, even if the first-pass value is
   still English text. A missing key silently falls back to English, so the gap is
   invisible in the UI but breaks key parity.
3. Keep the file grouped by prefix; each file is ordered by feature area, not strictly
   alphabetically.
4. Use targeted edits for the localisation JSON files. They are large (roughly 120 KB to
   165 KB and about 2200 lines each) and shared across 16 languages, so rewriting a whole
   file from a generated listing can silently drop keys.
5. Reference the key in code via `UiStrings.Get("your.key")` or
   `UiStrings.Format("your.key", args)`.
6. For panel labels, either store the `Label` reference and update it in
   `ApplyUiLocalisation()`, or read `UiStrings.Get()` when the control is created and
   refresh it on language change.
7. For `en-GB` menu items include `&` mnemonics; `tab.*` titles do not currently use
   them. Never strip translated `&` markers.
8. Keep format placeholders consistent in count and meaning across all 16 files.
9. After editing, check that all 16 files have identical key sets and key counts.

### Current Key Count

All 16 UI language files contain the full set of keys, each with identical key sets
(0 missing keys). The count was verified by parsing every `Resources/ui/lang/*.json`
file as a JSON object.

## Game-Data Localisation

### Language Dumps (`Resources/json/lang`)

- 16 files named by BCP 47 tag, produced by `NMSE.Extractor`
  (`Data/LocalisationBuilder.cs`).
- Each file is a flat dictionary of NMS internal localisation keys (for example
  `UI_FUEL_1_NAME`) to translated text, roughly 8 MB to 13 MB per file.
- Loaded at runtime by `LocalisationService` one language at a time.
  `LoadLanguage(null)` reverts to the default English values stored in the game JSON
  databases.
- Not read by `UiStrings`. App UI text lives in `Resources/ui/lang`; the two directories
  have independent files and independent key formats.
- Shipped as content because `Resources\json\**\*` is a `Content` item in
  `NMSE.csproj`.

### `_LocStr` Keys

The extractor emits both a default English value and a localisation key for every
localisable field, for example:

```json
{
  "Id": "FUEL1",
  "Name": "Di-hydrogen",
  "Name_LocStr": "UI_FUEL_1_NAME",
  "Description": "...",
  "Description_LocStr": "UI_FUEL_1_DESC"
}
```

Entries carry fields such as `NameLocStr`, `NameLowerLocStr`, `SubtitleLocStr` and
`DescriptionLocStr`. On a language switch the owning database's
`ApplyLocalisation(LocalisationService)` resolves those keys against the active language
dump and overwrites the display fields (`Name`, `Description`, and so on). The original
English values are backed up the first time and restored by `RevertLocalisation()`.

Internal editor behaviour uses IDs and the original English values; only display fields
are replaced. Some code paths deliberately keep the English value, for example
`WikiGuideDatabase.GetEnglishCategory()` is used for grid placement.

### Databases Localised at Runtime

Called in `MainForm.ApplyStartupLanguage()` and again in `OnLanguageSelected()`:

| Database | What is localised |
|----------|-------------------|
| `GameItemDatabase` | Item name, lower-case name, subtitle, description |
| `RewardDatabase` | Reward names |
| `WordDatabase` | Word text (with multiple group-key fallbacks) |
| `RecipeDatabase` | Recipe names and types |
| `TitleDatabase` | Title names and unlock descriptions |
| `FrigateTraitDatabase` | Trait names |
| `SettlementDatabase` | Perk names and descriptions |
| `WikiGuideDatabase` | Topic names and categories |
| `CompanionAccessoryDatabase` | Accessory names |
| `SpacePoiTableDatabase` | Space POI type display names |

### Name Fallback Chains in GameItemDatabase

Procedural and upgrade items have loc keys that do not map one-to-one to the language
dump, so `GameItemDatabase.ApplyLocalisation` tries, in order:

- `NameLocStr`
- `NameLocStr + "_NAME"`
- level-specific keys derived from the item ID: `NameLocStr + "X_NAME"` for `X` ids, or
  `NameLocStr + "N_NAME"` for numbered ids
- `NameLocStr + "1_NAME"`
- a name key derived from `DescriptionLocStr` by replacing `_DESC` with `_NAME`
  (the lower-case name attempts `_NAME_L` variants in the same way)

Descriptions try `DescriptionLocStr` and description-key variants, and subtitles are
resolved the same way. A missing translation leaves the default English value in place.

Some display strings are curated by the editor rather than the game dumps. For example
`CompanionDatabase.GetLocalisedAffinityName()` maps game affinity names to
`companion.battle_val_affinity_*` UI keys through `UiStrings.GetOrNull()`.

## Testing

### UI String Tests (`NMSE.Tests/UiStringsTests.cs`)

40 facts, split between two styles:

- Temp-file tests for core behaviour: raw-key default, English fallback load,
  translation load, missing-key fallback, `GetOrNull`, `Format` substitution and
  graceful failure, `TranslatedCount`, `TotalKeyCount`, `IsActive`, `Reset`, missing
  file or directory, language switches and revert-to-English.
- Real-locale tests that load `Resources/ui/lang/en-GB.json` and assert specific keys
  (`discovery.tab_locations`, `common.procedural_no_name`, `settlement.delete_warning`,
  companion battle keys, base export/import keys, raw JSON node export/import keys,
  inventory picker keys, accessory keys), including key-existence checks across all 16
  languages for several key groups.

### Game-Data Localisation Tests (`NMSE.Tests/DatabaseLocalisationTests.cs`)

10 facts for JSON-backed databases: `FrigateTraitDatabase`, `SettlementDatabase`,
`WikiGuideDatabase`, `WordDatabase` group-key fallback, `GameItemDatabase`
`_NAME` / `1_NAME` / level-suffix fallbacks, `RevertLocalisation`, and the `AppConfig`
default language.

### Logic Tests (`NMSE.Tests/LogicTests.cs`)

Loads `en-GB.json` on construction so that tests of logic classes which return
localised text through `UiStrings` (for example outfit slot labels and max-supported
labels) keep passing. The `MutableStaticDatabases` collection prevents these tests from
running in parallel with the localisation test classes that mutate `UiStrings` state.
