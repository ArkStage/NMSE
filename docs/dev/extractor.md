# NMSE.Extractor

## Overview

NMSE.Extractor (`NMSE.Extractor/`) is a .NET console application that mines the No Man's
Sky game archives to produce the JSON databases, PNG icons, field mapping, and generated
C# code that the main editor uses at runtime. It runs a 14-step pipeline (Steps 0 to 12,
including Step 9b) that:

1. Locates the NMS installation via the Steam registry
2. Estimates peak storage and prompts for confirmation
3. Downloads or updates the external tools (`hgpaktool`, `MBINCompiler`, ImageMagick, `7zr`)
4. Extracts filtered files from `.pak` archives using `hgpaktool`
5. Converts binary `.mbin` files to XML-like `.mxml` using `MBINCompiler`
6. Parses `.mxml` into structured dictionaries
7. Categorises items into typed JSON database files and runs the enrichment passes
8. Converts `.dds` textures to `.png` icons and downloads the MBIN field mapping

Output locations:

| Path | Content |
|------|---------|
| `Resources/json/` | JSON databases, `Catalogue Pack.json`, `none.json`, `Egg Modifiers.json.tbc` |
| `Resources/json/lang/` | Per-language localisation files `{bcp47}.json` (16 languages) |
| `Resources/images/` | PNG icons converted from DDS textures |
| `Resources/map/mapping.json` | MBIN field name mapping downloaded from the MBINCompiler releases |
| `Data/TechPackDatabase.Generated.cs` | Generated partial `TechPacks` class (written to the main project's `Data/` folder, not to `Resources/`) |

The working directories `banks/`, `Resources/mbin/` and `tools/` are created during a run;
`banks/` and `Resources/mbin/` are deleted when the run finishes or fails.

The editor's UI strings under `Resources/ui/lang/` are NOT produced by the extractor. Only
the game's own language tables are dumped, into `Resources/json/lang/`. The extractor has
no references to `UiStrings`, so UI localisation is maintained separately in the main
project.

---

## Pipeline Stages

The steps logged by `Program.Main` (`NMSE.Extractor/Program.cs`):

```
Step  0: SteamLocator.FindPcBanksPath()
           -> Locate the NMS GAMEDATA\PCBANKS directory (fatal if not found)

Step  1: PromptStorageConfirmation()
           -> Estimate peak storage (largest pak plus half the relevant pak size),
              print total/relevant pak sizes and free disk space, prompt (Y/n);
              answering n cancels the run and exits 0

Step  2: ToolManager
           -> EnsureHgPakToolAsync, EnsureMbinCompilerAsync, EnsureImageMagickAsync
              (7zr is downloaded on demand by EnsureImageMagickAsync)

Step  3: Cleanup
           -> CleanResources removes every file and subdirectory of Resources/
              (then recreates Resources/json/), and banks/extracted/ is deleted

Step  4: PakExtractor.ExtractPerPak()
           -> Skip irrelevant paks, then process one pak at a time: copy, run hgpaktool
              with pak-specific filters, delete the copy. Output accumulates in
              banks/extracted/ (MBINs and DDS textures)

Step  5: MbinConverter.ConsolidateMbins()
           -> Move all extracted .mbin files into Resources/mbin/ (deduplicated by name)

Step  6: MbinConverter.ConvertMbinsToMxml()
           -> Parallel MBINCompiler conversion (120 second timeout per file), then
              fatal validation of ExtractorConfig.ExpectedMxmlFiles

Step  7: LocalisationBuilder.BuildLocalisationJson()
           -> Parse per-language MXML files and write Resources/json/lang/{bcp47}.json
              for all 16 languages (unescaped UTF-8)

Step  8: RunParsers()
           -> Pre-warm the MxmlParser XML cache, then run 30 parsers in parallel

Step  9: CategorizeAndSave()
           -> Categorise items, run the enrichment/normalisation passes, write
              Resources/json/*.json plus none.json

Step  9b: CataloguePackBuilder.WriteCataloguePack()
           -> Write Resources/json/Catalogue Pack.json

Step 10: GenerateTechPackPartialClass()
           -> Write Data/TechPackDatabase.Generated.cs (the tech catalog)

Step 11: RunImageExtraction()
           -> ImageExtractor.NormalizeExtracted then ExtractIcons: DDS to PNG via
              ImageMagick magick.exe, into Resources/images/

Step 12: ToolManager.DownloadMappingJsonAsync()
           -> Download mapping.json to Resources/map/mapping.json

Cleanup (finally): PakExtractor.CleanupBanksDir + CleanupMbinFolder
           -> Remove banks/ and Resources/mbin/
```

Failures in non-critical stages log warnings and continue. Failures in critical stages (for
example a missing expected MXML file after Step 6) throw and abort the run with exit code 1.

---

## Classes

### Program

| | |
|---|---|
| File | `NMSE.Extractor/Program.cs` |
| Purpose | Entry point that orchestrates the 14-step extraction pipeline |

`Main` installs a `TeeTextWriter` on both `Console.Out` and `Console.Error`, mirroring all
output to `log.txt` beside the executable. It resolves the output paths from the folder
constants in `ExtractorConfig`, runs the steps above, and always restores the original
console writers and cleans the working directories in `finally`. Besides the stage methods
it contains the post-processing passes described under "Post-processing and normalisation"
and `GenerateTechPackPartialClass`.

---

### ExtractorConfig

| | |
|---|---|
| File | `NMSE.Extractor/Config/ExtractorConfig.cs` |
| Purpose | Central configuration: tool URLs, filter groups, expected outputs, folder names |

Constants and fields:

| Constant/field | Description |
|----------------|-------------|
| `NmsGamePath` | Relative path: `steamapps\common\No Man's Sky\GAMEDATA\PCBANKS` |
| `HgPakToolZipUrl` / `HgPakToolLatestUrl` | GitHub release URLs for the PAK extraction tool |
| `MbinCompilerUrl` / `MbinCompilerLatestUrl` | GitHub release URLs for the MBIN-to-MXML compiler |
| `MappingJsonUrl` | URL for `mapping.json` from the MBINCompiler releases |
| `ImageMagickLatestUrl` / `ImageMagickDownloadPattern` / `ImageMagickMaxFallbacks` | ImageMagick download URLs and the fallback count (5) |
| `SevenZipLatestUrl` / `SevenZipDownloadPattern` | 7-Zip download URLs (the `7zr.exe` asset) |
| `SupportedLanguages` | 16 languages mapping NMS language names to BCP 47 tags (English to en-GB, French to fr-FR, Italian to it-IT, German to de-DE, Spanish to es-ES, Russian to ru-RU, Polish to pl-PL, Dutch to nl-NL, Portuguese to pt-PT, LatinAmericanSpanish to es-419, BrazilianPortuguese to pt-BR, SimplifiedChinese to zh-CN, TraditionalChinese to zh-TW, Korean to ko-KR, Japanese to ja-JP, USEnglish to en-US). TencentChinese is excluded. |
| `LocaleFileStems` | 8 baseline locale stems for update 3 and earlier: the `nms_` prefixed `loc1`, `loc4` through `loc9`, and `update3` names |
| `MetadataMbinFilters` | 36 metadata MBIN patterns under `METADATA/` (product, substance, recipe, technology, reward, title, frigate trait, settlement perk, wiki, space POI, creature and pet tables, catalogue tables, etc.) |
| `LocaleMbinFilters` | 1 wildcard pattern: `*LANGUAGE/nms_*.mbin`, capturing every locale table the game ships, including tables added after update 3 |
| `GlobalsMbinFilters` | 2 root-level patterns: `*GCGAMETABLEGLOBALS.mbin`, `*GCCREATUREGLOBALS.mbin` |
| `SceneMbinFilters` | 4 creature and robot `*.SCENE.MBIN` patterns for the EntitySceneMBIN paks |
| `MbinFilters` | Combined MBIN filter set: metadata + locale + globals + scene (43 patterns) |
| `TextureFilters` | 1 DDS pattern: `*TEXTURES/*.DDS` |
| `AllExtractionFilters` | Combined MBIN + texture filters (44 patterns); used at runtime only for hex-named paks via `GetFiltersForPak` |
| `ExpectedMxmlFiles` | 39 MXML files that must exist after conversion; a missing file is fatal |
| `OptionalMxmlFiles` | Non-English locale MXML files (15 languages); missing files warn but do not abort |
| `LangSubfolder` | `"lang"` |
| `ResourcesFolder` / `ToolsFolder` / `BanksFolder` | `"Resources"`, `"tools"`, `"banks"` |
| `ImageMagickSubfolder` | `"imagemagick"` |
| `MbinSubfolder` / `JsonSubfolder` / `ImagesSubfolder` / `MapSubfolder` / `ExtractedSubfolder` | `"mbin"`, `"json"`, `"images"`, `"map"`, `"extracted"` |

| Method | Description |
|--------|-------------|
| `GetLocaleMxmlFiles(language)` | Returns the known baseline locale file names for a language, e.g. `nms_loc4_japanese.MXML`. Used for optional-file warnings only. |
| `GetLocaleMxmlFiles(mbinDir, language)` | Enumerates every extracted locale MXML matching `nms_*_{language}.MXML` in `mbinDir`, so newer language tables are picked up automatically. Used by `LocalisationBuilder`. |
| `GetFiltersForPak(pakFileName)` | Returns the filter set for one pak: `*Tex*` paks get `TextureFilters`, `*EntitySceneMBIN*` paks get `SceneMbinFilters`, other dot-named paks get `MbinFilters`, and hex-named paks (no dot) get `AllExtractionFilters`. |

---

### PakExtractor

| | |
|---|---|
| File | `NMSE.Extractor/Data/PakExtractor.cs` |
| Purpose | Extract files from NMS `.pak` archives using the external `hgpaktool` |

`IsPakRelevant(pakFileName)` skips paks whose type segment (the part after the first dot)
starts with a known irrelevant prefix: `ANIMMBIN`, `AUDIO`, `AUDIOBNK`, `FONTS`, `MESH`,
`MISC`, `PIPELINES`, `SCENES`, `SHADERS`, `UI`. Everything else is processed.

`ExtractPerPak(hgpaktoolPath, pcbanksPath, banksDir, getFiltersForPak)` processes one pak at
a time. For each relevant pak it:

1. Gets the pak-specific filters via `ExtractorConfig.GetFiltersForPak`
2. Copies the pak into `banks/`
3. Runs `hgpaktool` with `-f` filter arguments and a 120 second timeout
4. Deletes the copy immediately

Extracted content accumulates in `banks/extracted/`, which is kept until image extraction
has run. The `hgpaktool` output is parsed for an "Unpacked N files" count and an in-place
progress line is printed per pak.

Other members: `WriteProgress`, `FinishProgress`, `ParseUnpackedCount`, `CleanupPakFiles`,
`CleanupBanksDir`, `GetPakFilesSize`, `GetLargestPakFileSize` and
`EstimateMaxStorageBytes` (largest pak plus half the relevant pak size, matching the
storage estimate shown in Step 1).

Precache and MetadataEtc paks are dot-named and receive the full 43-pattern `MbinFilters`
set. `SPACEPOITABLE.mbin` is one of the metadata filters, so the Space POI table is
extracted as part of that normal filter set; no separate per-mode filtering is applied.

---

### MbinConverter

| | |
|---|---|
| File | `NMSE.Extractor/Data/MbinConverter.cs` |
| Purpose | Consolidate extracted MBINs and batch-convert them to `.mxml` |

`ConsolidateMbins(resourcesDir, banksDir)` gathers every extracted `.mbin` file into
`Resources/mbin/`. It uses `File.Move` (not copy) to avoid duplicating data, searches
`banks/extracted/`, then `banks/`, then `resourcesDir`, and keeps only the first file for
each name. If hgpaktool created staging directories under `Resources/` (`METADATA`,
`LANGUAGE`, `SIMULATION`), they are removed. Textures are deliberately left in place for
image extraction.

`ConvertMbinsToMxml(mbinCompilerPath, mbinDir)` runs `MBINCompiler` in parallel
(`MaxDegreeOfParallelism` equal to the processor count) with a 120 second timeout per file.
When conversion finishes it verifies that every file in `ExtractorConfig.ExpectedMxmlFiles`
(39 entries) exists; any missing expected file throws and aborts the pipeline. Files in
`ExtractorConfig.OptionalMxmlFiles` (non-English locales) only produce warnings.

---

### MxmlParser

| | |
|---|---|
| File | `NMSE.Extractor/Data/MxmlParser.cs` |
| Purpose | Parse `.mxml` files into dictionaries and resolve localised text |

The MXML format uses nested `<Property>` elements with `name`, `value` and `_index`
attributes. This parser reads the XML and produces `XElement` trees that the `Parsers`
class consumes.

- `LoadXml` caches parsed XML by full path and last write time; `ClearXmlCache` empties it.
- `LoadLocalisation` loads the English lookup from `Resources/json/lang/en-GB.json`, with a
  read-only fallback to the legacy `Localisation-EN.json.tbc` for older resource sets.
  `ClearLocalisationCache` resets it.
- `Translate` resolves a localisation key, retries without a redundant `_NAME` segment,
  applies `MissingLocalisationOverrides`, strips markup tags (leaving control tokens such as
  `FE_ALT1` in place for the app to resolve at display time) and title-cases `_NAME` values.
- Helpers: `GetPropertyValue`, `GetNestedEnum`, `ParseValue`, `ParseColour`,
  `NormalizeGameIconPath`, `TitleCaseName`, `FormatStatTypeName`,
  `LooksLikeLocalisationKey` and `UnresolvedLocalisationKeyCount`.

---

### LocalisationBuilder

| | |
|---|---|
| File | `NMSE.Extractor/Data/LocalisationBuilder.cs` |
| Purpose | Build per-language localisation JSON files from NMS language MXML files |

NMS ships each language as a set of MXML files where only the target language property
contains values (for example the Japanese tables have data only in the `Japanese`
property).

`BuildLocalisationJson()` iterates all 16 supported languages defined in
`ExtractorConfig.SupportedLanguages`. For each language it:

1. Enumerates the language's extracted MXML files via the wildcard
   `ExtractorConfig.GetLocaleMxmlFiles(mbinDir, languageName)`
2. Calls `ParseLocalisation(mxmlPath, languageName)` to extract key-value pairs from the
   specified language property, stripping markup tags and title-casing English `_NAME` keys
3. Writes the merged dictionary to `Resources/json/lang/{bcp47}.json` using unescaped
   UTF-8 (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`)

All 16 language files live under the `lang/` subfolder, keyed by BCP 47 tag. The builder no
longer writes the legacy `Localisation-EN.json.tbc`; that name survives only as a read
fallback in `MxmlParser`.

---

### Parsers

| | |
|---|---|
| File | `NMSE.Extractor/Data/Parsers.cs` |
| Purpose | MXML-to-JSON parsers for all game data types |

A large static class containing 31 public `Parse*` functions. Each parser reads a specific
MXML file and produces a list of standardised dictionaries. The 30 entries below are wired
into `Program.RunParsers`; `ParseBuildings` exists for tests and is not used by the
pipeline (building metadata is applied by `EnrichBuildingsMetadata` instead).

| Parser | Input MXML | Output |
|--------|-----------|--------|
| `ParseProducts` | `nms_reality_gcproducttable.MXML` | Products with prices, crafting data, icons and flags |
| `ParseRawMaterials` | `nms_reality_gcsubstancetable.MXML` | Elements and resources |
| `ParseTechnology` | `nms_reality_gctechnologytable.MXML` | Technology with stats, category and flags |
| `ParseRefinery` | `nms_reality_gcrecipetable.MXML` | Refining recipes |
| `ParseNutrientProcessor` | `nms_reality_gcrecipetable.MXML` | Nutrient processor recipes |
| `ParseBuildings` | `basebuildingobjectstable.MXML` | Building entries (tests only) |
| `ParseCooking` | `consumableitemtable.MXML` | Food and cooking items |
| `ParseFish` | `fishdatatable.MXML` | Fish entries |
| `ParseTrade` | `nms_reality_gcproducttable.MXML` | Trade goods |
| `ParseShipComponents` | `nms_modularcustomisationproducts.MXML` | Starship components |
| `ParseBaseParts` | `nms_basepartproducts.MXML` | Base parts |
| `ParseSpacePoiTable` | `spacepoitable.MXML` | Space POI type rules and item types |
| `ParseProceduralTech` | `nms_reality_gcproceduraltechnologytable.MXML` | Procedural technology |
| `ParsePetEggTraitModifiers` | `peteggtraitmodifieroverridetable.MXML` | Egg trait modifiers |
| `ParseAllRecipes` | `nms_reality_gcrecipetable.MXML` | Unified refining and cooking recipes |
| `ParseTitles` | `PLAYERTITLEDATA.MXML` | Player titles with localisation keys |
| `ParseFrigateTraits` | `FRIGATETRAITTABLE.MXML` | Frigate traits with numeric strength and stat types |
| `ParseSettlementPerks` | `SETTLEMENTPERKSTABLE.MXML` | Settlement perks with stat changes |
| `ParseWikiGuide` | `WIKI.MXML` | Wiki guide topics organised by category |
| `ParseRewards` | `UNLOCKABLESEASONREWARDS.MXML` | Season, Twitch and platform rewards |
| `ParseWords` | `nms_dialog_gcalienspeechtable.MXML` | Alien words with per-race group mappings |
| `ParsePetAccessories` | `CHARACTERCUSTOMISATIONDESCRIPTORGROUPSDATA.MXML` | Companion accessories |
| `ParseShipCustomisation` | `modularcustomisationdatatable.MXML` | Ship customisation configs |
| `ParseShipColourPalettes` | `customisationcolourpalettes.MXML` | Ship colour palettes |
| `ParseBaseColourPalettes` | `basecolourpalettes.MXML` | Base colour palettes |
| `ParsePetBattleMoves` | `PETBATTLERMOVESTABLE.MXML` | Pet battle moves |
| `ParsePetBattleMovesets` | `PETBATTLERMOVESETSTABLE.MXML` | Pet battle movesets |
| `ParseGameTableGlobals` | `GCGAMETABLEGLOBALS.MXML` | Game table globals |
| `ParseCreatureSpecies` | `creaturedatatable.MXML` | Creature species |
| `ParseRobotSpecies` | `robotdatatable.MXML` | Robot species (merged into Creature Species.json) |
| `ParseCreatureDescriptors` | `creaturefilenametable.MXML` | Creature descriptor appearance data |

Helper methods include `BuildCanonicalProductDict()` for standardised field ordering and
`NullIfEmpty()` for empty-string-to-null conversion.

**Localisation string enrichment:** Parser outputs include `_LocStr` keys alongside the
resolved English text fields. These keys hold the raw MXML localisation key values so that
consumers can look up translated text from the per-language JSON files:

| Key | Source |
|-----|--------|
| `Name_LocStr` | Raw MXML `Name` property value (e.g. `"UI_FUEL_1_NAME"`) |
| `NameLower_LocStr` | Raw MXML `NameLower` property value |
| `Subtitle_LocStr` | Raw MXML `Subtitle` property value |
| `Description_LocStr` | Raw MXML `Description` property value |
| `UnlockDescription_LocStr` | Title unlock description key |
| `AlreadyUnlockedDescription_LocStr` | Title already-unlocked description key |
| `RecipeName_LocStr` | Recipe name localisation key |
| `RecipeType_LocStr` | Recipe type localisation key |
| `Category_LocStr` | Category/heading localisation key (wiki guides, items) |
| `Text_LocStr` | Word text localisation key (first group key by race ordinal, e.g. `"TRA_ABOMINATION"`) |
| `NameLocKey` | Space POI table raw name key (Space POI.json) |

---

### Categorizer

| | |
|---|---|
| File | `NMSE.Extractor/Data/Categorizer.cs` |
| Purpose | Assign parsed items to output JSON category files |

`CategorizeAndSave` feeds the Products, Technology, Cooking, ShipComponents, BaseParts and
ProceduralTech parser outputs through `Categorizer.CategorizeItem`. Other tables are
written to their standalone files directly. Items that match no rule are skipped and later
written to `none.json` for review.

Classification uses:

- **Exact rules** - `ExactRules` maps an output filename to the set of item groups it
  accepts (Food, Constructed Technology, Buildings, Curiosities, Technology Module,
  Others, Products, Technology, Corvette, Fish, Trade and Raw Materials)
- **Prefix rules** - `PrefixRules` (currently Corvette groups beginning with `Corvette `)
- **Regex patterns** - `TechModuleClassPattern`, `DeployableSalvageClassPattern`,
  `StarshipComponentGroupPattern`
- **Inclusion/exclusion sets** - `StarshipExactGroups`, `StarshipExcludedGroups`,
  `StarshipUpgradeGroups`, `ShipComponentGroups`, `NameFilterExemptGroups`, `JunkKeywords`
- **Item-based routing** - `IsBuildingPartProduct` (BuildingPart products), and
  `VehicleKeywordMatch` (exocraft/submarine/nautilon names, `up_veh`/`u_exo` IDs),
  with Space Station routing (`Space Station Decoration`, `Orbital Base Module`) applied
  first so station parts are not captured by the vehicle keywords
- **Fallbacks** - `SourceTable` routes unknown Substance and Technology groups to Raw
  Materials and Technology respectively, then Others.json; `U_TECHPACK_` and `U_TECHBOX_`
  items are intentionally left uncategorised for `none.json`

**Output files:**

Pre-seeded files (bypass categorisation):

| File | Content |
|------|---------|
| `Fish.json` | Fish entries from the fish data table |
| `Trade.json` | Trade goods |
| `Raw Materials.json` | Elements and resources; items that match another rule are rerouted during post-processing |

Standalone files (written directly):

| File | Content |
|------|---------|
| `Recipes.json` | Crafting and refining recipes |
| `Rewards.json` | Expedition, Twitch and platform rewards |
| `Words.json` | Alien words with per-race group mappings |
| `Titles.json` | Player titles with localisation keys (346 entries) |
| `Frigate Traits.json` | Frigate traits with stat types (178 entries) |
| `Settlement Perks.json` | Settlement perks with stat changes (90 entries) |
| `Wiki Guide.json` | Wiki guide topics by category (58 entries) |
| `Companion Accessories.json` | Companion accessory variants |
| `Space POI.json` | Space POI type rules |
| `Ship Customisation.json` | Ship customisation configs |
| `Colour Palettes.json` | Ship colour palettes merged with base colour palettes, deduplicated by `PaletteID` |
| `Pet Battle Moves.json` | Pet battle moves |
| `Pet Battle Movesets.json` | Pet battle movesets |
| `Game Table Globals.json` | Game table globals |
| `Creature Species.json` | Creature species merged with robot species (86 entries) |
| `Creature Descriptors.json` | Creature descriptor data (64 entries) |
| `Egg Modifiers.json.tbc` | Egg trait modifiers, not loaded by the editor yet |

Categorised files:

| File | Content |
|------|---------|
| `Buildings.json` | Buildings, furniture and decoration |
| `Constructed Technology.json` | Constructed technology types |
| `Food.json` | Carnivore Bait, Edible Product, Raw Ingredient etc. |
| `Corvette.json` | Corvette parts |
| `Curiosities.json` | Curiosities, fossils etc. |
| `Exocraft.json` | Exocraft parts |
| `Station.json` | Space station decorations and orbital base modules |
| `Starships.json` | Starship components and spacecraft |
| `Others.json` | Trails, cartographic data and miscellaneous items |
| `Products.json` | Manufactured products |
| `Technology.json` | Technologies |
| `Technology Module.json` | Unusual technology modules |
| `Upgrades.json` | Ship, weapon and suit upgrade modules |

Other outputs:

| File | Content |
|------|---------|
| `none.json` | Uncategorised items, written with two-space indenting for review |
| `Catalogue Pack.json` | Derived catalogue completion data (see `CataloguePackBuilder`) |
| `lang/{bcp47}.json` | Per-language localisation files (see `LocalisationBuilder`) |

Output filenames for multi-word item types contain spaces (for example
`Frigate Traits.json`, `Settlement Perks.json`, `Space POI.json`, `Colour Palettes.json`).
The editor treats each filename as a `GameItem.ItemType`; standalone files such as
`Rewards.json`, `Recipes.json`, `Words.json`, `Frigate Traits.json`,
`Settlement Perks.json`, `Wiki Guide.json` and `Titles.json` are skipped by
`GameItemDatabase` because dedicated databases load them.

---

### Post-processing and normalisation

`Program.CategorizeAndSave` runs the following passes after items are categorised and
before the files are saved. They live in `Program.cs`:

| Pass | Description |
|------|-------------|
| Raw Materials rerouting | Re-runs `Categorizer.CategorizeItem` on pre-seeded Raw Materials items and moves items that now belong elsewhere (for example Reward Item to Others.json) |
| `ReclassifyByDeploysInto` | Moves Upgrades items with a `DeploysInto` value to Technology Module.json, and Technology Module items without one to Upgrades.json |
| `MoveExocraftUpgrades` | Moves Exocraft items whose name or group contains "upgrade" to Upgrades.json |
| `NormalizeUpgradeDisplayNames` | For qualified groups (`A-Class ... Upgrade`, `Significant ... Upgrade` etc.) sets Group to `{qualifier} Upgrade` and Name to `{name} {short group}` |
| `CorrectUpgradeRarities` | Sets Rarity from group/description keywords (Banned to Illegal, Supreme to Legendary, Powerful to Epic etc.) and from `_C`/`_B`/`_A`/`_S` ID suffixes |
| `EnrichUpgradeDescriptions` | Replaces placeholder descriptions (`Up Up_...`, `Ut Cr ...`) with wrapper text or generated text |
| `EnrichFishWithCookingData` | Adds `CdnUrl` to fish that also exist as products, and reward fields from cooking data |
| `ApplySlugs` | Adds a `Slug` field (`{prefix}{Id}`) per output file, before fields added by later enrichment passes |
| `EnrichTechnologyCategory` | Copies `Category`, `Upgrade`, `Core`, `Procedural` and charge fields from Technology/ProceduralTech data matched by Id or `DeploysInto` |
| `EnrichUpgradeStats` | Copies `StatBonuses`, `StatLevels`, `NumStatsMin`/`NumStatsMax` and `WeightingCurve` into Upgrades items that have no stats |
| `EnrichCorvetteMetadata` | Adds product metadata from `nms_basepartproducts` and `nms_modularcustomisationproducts` |
| `EnrichCorvetteBuildableTechLabels` | Links `BuildableShipTechID` to the matching Upgrades name, group and description |
| `EnrichExocraftMetadata` | Adds product metadata from `nms_reality_gcproducttable` and `nms_basepartproducts` |
| `EnrichBuildingsMetadata` | Adds `IconOverrideProductID`, buildable flags, `Groups`, `LinkGridData`, `CanPickUp` and `IsTemporary` from `basebuildingobjectstable` |
| `DeduplicateAll` | Removes duplicate IDs per file (Food keeps the last value at the first position) and cross-file duplicates, skipping Recipes.json |
| `StripCookingFieldsFromNonFood` | Removes `CookingValue`, `RewardID`, `EffectCategory`, `RewardEffectStats` and `NameLower` from items outside Food.json and Fish.json |
| `DedupeStarshipAdornmentDisplayDuplicates` | Removes `T_BOBBLE_*` tech variants when the matching `BOBBLE_*` product exists |

---

### CataloguePackBuilder

| | |
|---|---|
| File | `NMSE.Extractor/Data/CataloguePackBuilder.cs` |
| Purpose | Build the derived catalogue completion data written to `Catalogue Pack.json` |

`WriteCataloguePack(jsonDir, baseData, mbinDir)` writes `Resources/json/Catalogue Pack.json`
with these keys:

| Key | Source |
|-----|--------|
| `KnownRefinerRecipes` | Recipe IDs from the parsed Recipes data |
| `KnownWordGroups` | Word groups with per-race flags, using race ordinals 0, 1, 2, 4 and 8 |
| `Fossils` | `FOS_*` IDs from the ShipComponents data |
| `KnownPortalRunes` | Constant 65535 |
| `CatalogueMaterials` / `CatalogueBuilding` / `CatalogueCrafting` | Explicit item lists read from `cataloguematerials.MXML`, `cataloguebuilding.MXML` and `cataloguecrafting.MXML` |

Values that cannot be derived from game files (save-observed wonder data, discovery stat
targets, story completers and the account Seen lists) are compiled into the editor as
`CatalogueKnownValues`, not emitted by the extractor. Missing lists are omitted so the
editor falls back to its own known values.

---

### CuratedItemNames

| | |
|---|---|
| File | `NMSE.Extractor/Data/CuratedItemNames.cs` |
| Purpose | Curated display names for items whose localisation keys are missing |

Some product, technology and base part entries reference `Name` keys that no shipped
language table provides, so no official name can be extracted. `CuratedItemNames` holds
verified display names and groups for those entries, and `ProductLookup` keeps them in the
output with the `[?] ` prefix (the `Prefix` constant) so the editor can flag them and
explain the marker. This is why a small number of items in the JSON databases show names
such as `[?] Spider Brain`. Curated entries are only used while the official keys are
missing; if a later game update ships the strings, the official name wins and the prefix
disappears.

---

### JsonWriter

| | |
|---|---|
| File | `NMSE.Extractor/Data/JsonWriter.cs` |
| Purpose | Serialise categorised data into formatted JSON database files |

`SaveJson(data, outputDir, filename, useSpaceIndent)` writes to the supplied directory (the
pipeline passes `Resources/json`). Files are tab-indented, UTF-8 and written with
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. A `DoubleStyleConverter` ensures floating
point values always include a decimal point (`0` becomes `0.0`) and that scientific
notation uses a lowercase `e`. `none.json` is written with two-space indenting via
`useSpaceIndent`. `SaveJsonRaw` serialises arbitrary objects with the same options.

---

### ImageExtractor

| | |
|---|---|
| File | `NMSE.Extractor/Data/ImageExtractor.cs` |
| Purpose | Convert DDS textures to PNG icons using ImageMagick |

NMS stores item icons as `.dds` (DirectDraw Surface) textures. `CollectIdIconPairs` reads
the `Id` and `IconPath` fields from 16 JSON files (Buildings, Constructed Technology, Food,
Corvette, Curiosities, Exocraft, Fish, Others, Products, Raw Materials, Starships,
Technology, Technology Module, Trade, Upgrades and none). `NormalizeExtracted` moves the
extracted texture tree to lowercase `textures/` so paths match the game references.
`ExtractIcons` looks up each icon in `banks/extracted/` and invokes `magick.exe` (15 second
timeout) to write `{sanitisedId}.png` files into `Resources/images/`. Icons are emitted as
128 px thumbnails (`-thumbnail 128x128>`), matching the editor's icon cache size, which keeps
the asset set small and the runtime preload fast. `FindMagickExe`
locates the portable ImageMagick under `tools/imagemagick/` (or directly in `tools/`), and
`SanitizeFilename` replaces characters that are invalid in file names.

---

### ProductLookup

| | |
|---|---|
| File | `NMSE.Extractor/Data/ProductLookup.cs` |
| Purpose | Parse product table elements and build product lookups |

`ParseProductElement` converts one MXML product element into the standard product row used
across the JSON databases (localisation, icons, categories, usages, prices and flags). It
resolves curated names from `CuratedItemNames`, drops entries with too many unresolved
localisation keys, and can include requirements and raw key fields. `LoadProductLookup`
builds an ID-keyed dictionary from a product table MXML file. `AsDouble` ensures parsed
values stay floating point where the game data uses them.

---

### TeeTextWriter

| | |
|---|---|
| File | `NMSE.Extractor/Data/TeeTextWriter.cs` |
| Purpose | Duplicate console output to a log file |

A `TextWriter` subclass that writes to both the original console writer and a log writer
simultaneously. `Program.Main` installs one instance on `Console.Out` and another on
`Console.Error`, both writing to `log.txt`, so all standard output and error output is
captured for debugging. The log writer is owned by the caller, which restores the original
writers and disposes the stream in `finally`.

---

### SteamLocator

| | |
|---|---|
| File | `NMSE.Extractor/Util/SteamLocator.cs` |
| Purpose | Locate the NMS game installation via the Windows registry |

| Method | Description |
|--------|-------------|
| `FindPcBanksPath()` | Returns the full path to `GAMEDATA\PCBANKS`, throwing if Steam or the directory cannot be found |
| `GetSteamInstallPath()` | Reads the Steam install directory from the registry (64-bit Wow6432Node key first, then the native key) |
| `ReadRegistryValue(subKey, valueName)` | Private helper for Windows registry queries |

Used in Step 0 of the pipeline to find the game files without user input.

---

### ToolManager

| | |
|---|---|
| File | `NMSE.Extractor/Util/ToolManager.cs` |
| Purpose | Download and version-manage external tools from GitHub releases |

Manages `hgpaktool`, `MBINCompiler`, `7zr` and ImageMagick. Each ensure method checks the
cached version file against the latest GitHub release tag and downloads the asset if the
tool is missing or outdated.

| Method | Description |
|--------|-------------|
| `EnsureHgPakToolAsync(toolsDir)` | Download and extract `hgpaktool.exe` from the release zip |
| `EnsureMbinCompilerAsync(toolsDir)` | Download `MBINCompiler.exe` from the release |
| `Ensure7zrAsync(toolsDir)` | Download `7zr.exe` for archive extraction |
| `EnsureImageMagickAsync(toolsDir)` | Download and validate the portable ImageMagick build, falling back to older versions (up to `ImageMagickMaxFallbacks`) when a build fails the DDS/BC7 validation. Currently pinned to 7.1.2-22 because 7.1.2-24 has a BC7 regression. |
| `DownloadMappingJsonAsync(mapDir)` | Download `mapping.json` to `Resources/map/` |
| `GetLatestReleaseTagAsync(latestUrl)` | Resolve a GitHub `/releases/latest/` redirect to the actual tag |

Private helpers include `ReadVersionFile`, `WriteVersionFile`, the `ValidateMagick` checks
(format listing and a BC7 round trip), `GetFallbackImageMagickTags`,
`ExtractAndValidateImageMagick` and `RunMagickSilent`. Two `HttpClient` instances are used:
one without auto-redirect (fast tag resolution via the `Location` header) and one with
auto-redirect and a longer timeout (file downloads).

---

## Data Flow Diagram

```
NMS .pak archives (Steam PCBANKS)
    |
    v
PakExtractor.ExtractPerPak (hgpaktool, per-pak filters, relevance skip)
    |
    +--> .mbin files --> Resources/mbin/ (ConsolidateMbins)
    |                        |
    |                        v
    |                 ConvertMbinsToMxml (MBINCompiler)
    |                        |
    |                        v
    |                 Resources/mbin/*.MXML
    |                        |
    |          +-------------+----------------------+
    |          |                                    |
    |          v                                    v
    |   LocalisationBuilder                  RunParsers (30 parsers in
    |          |                             parallel, XML cache pre-warmed)
    |          v                                    |
    |   Resources/json/lang/                        v
    |   {bcp47}.json                    Categorizer + post-processing passes
    |                                               |
    |                                               v
    |                     Resources/json/*.json (+ Catalogue Pack.json, none.json)
    |                                               |
    |                        +----------------------+----------------------+
    |                        |                                             |
    |                        v                                             v
    |              GenerateTechPackPartialClass               ImageExtractor (ImageMagick)
    |                        |                                             |
    |                        v                                             v
    |              Data/TechPackDatabase.Generated.cs         Resources/images/*.png
    |
    +--> .dds textures --> banks/extracted/ (used by ImageExtractor in Step 11)

Step 12: ToolManager.DownloadMappingJsonAsync -> Resources/map/mapping.json
```

---

## Testing

`NMSE.Extractor.Tests/` contains xUnit tests for the extractor's parsing, categorisation,
configuration and tooling logic:

| Test class | Coverage |
|------------|----------|
| `CataloguePackBuilderTests` | Catalogue pack key derivation |
| `CategorizerTests` | Item classification rules and routing |
| `ExtractorConfigTests` | Filter groups, per-pak routing and expected files |
| `ImageExtractorTests` | Icon pair collection and file-name handling |
| `JsonWriterTests` | Serialisation formatting |
| `LocalizationBuilderTests` | Localisation parsing and output |
| `MbinConverterTests` | MBIN consolidation and conversion checks |
| `MxmlParserTests` | Property parsing, translation and value helpers |
| `ParsersIntegrationTests` | End-to-end parser behaviour against MXML fixtures |
| `TeeTextWriterTests` | Console/log mirroring |
| `ToolManagerTests` | Version handling and download logic |
