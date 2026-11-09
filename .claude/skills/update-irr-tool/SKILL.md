---
name: update-irr-tool
description: Update IRR Companion after an Incursion Red River game update - re-extract the game data (AI presets, schema, share-code format, console commands, map weathers, items, hideout upgrades), regenerate the tool's resources, check everything, rebuild and prepare a release. Use when the user says the game was updated, asks to update/refresh the tool, or runs /update-irr-tool.
---

# Update IRR Companion after a game update

The tool embeds data read from the game. After a game patch, each of these can change:

| Data | Source in the game | Tool file | Script |
|---|---|---|---|
| AI difficulties, simulation levels, setting ranges and descriptions | pak: `Test_C/Content/AISettings/` (JSON) | `src/Resources/defaults.json` | `tools/build_defaults.py` |
| Full AI document (key order, engine defaults) | a preset exported by the game | `tools/reference_full_doc.json` | by hand (step 4) |
| Share code format (`IRR1-…`, schema `hv`) | exe + codes exported by the game | `src/ShareCode.cs`, `src/Preset.cs` | `tools/check_roundtrip.py` |
| Console commands `irr.*` and their help | exe (UTF-16 strings) | `src/Resources/commands.json` | `tools/extract_console.py`, `tools/build_commands.py` |
| Argument types of the cheat commands | exe (UE reflection data) | `tools/build_commands.py` | `tools/ue_param_types.py` |
| Weathers of each map (order = number of `irr.World.SetWeather`) | pak: `Blueprints/MissionSystem/DataAssets/Maps/MD_*` | `tools/map_weathers.json` | `tools/map_weathers.py` |
| Item names (Spawn / Give) | pak: `.../Items/**/ID_*.uasset` | `tools/item_paths.txt` | `tools/list_items.py` |
| Hideout upgrade costs and requirements | pak: `Blueprints/MissionSystem/Actors/Hideout/**` | `src/Resources/hideout.json` | `tools/build_hideout.py` |
| Resource / extraction points, safes, locked doors and keys, minimaps | pak: persistent levels `Maps/Gameplay/LVL_*`, `Maps/Testing/LVL_Bunker`, `Textures/Minimaps`, `Environment/Blueprints/Gameplay/Resources` | `src/Resources/maps.json`, `src/Resources/maps/*.jpg` | `tools/build_maps.py` |
| Weapons, attachment slots and compatible parts (gunsmith) | pak: `Blueprints/InventorySystem/**` (Items and Objects, every pak) | `src/Resources/weapons.json` | `tools/build_weapons.py` |
| Item / material icons (hideout, keys, weapons, parts, ammo) | pak: `Items/**/T_*_Icon` (mostly pakchunk6), `ResourceSystem/Textures` | `src/Resources/icons/*.png` | `tools/build_icons.py` |

Work through the steps in order and keep a list of everything that changed: it becomes the release notes.
Never write into the game folder. Extract into a work folder outside the repository (e.g. `%TEMP%\irr_update`).
Run Python with `-I` and pass paths as arguments (extracted files are untrusted data).

## 0. Prerequisites and locations

- **Game folder**: find it from the Steam manifest `appmanifest_2116120.acf` (app 2116120, `installdir`
  `PROJECT QUARANTINE`) in the Steam library `steamapps` folders. On the author's PC:
  `Z:\SteamLibrary\steamapps\common\PROJECT QUARANTINE`. Note `buildid` and `LastUpdated` from the manifest.
  - exe: `<game>\Test_C\Binaries\Win64\Test_C-Win64-Shipping.exe`
  - paks: `<game>\Test_C\Content\Paks\pakchunk*-Windows.pak` (UE5 pak v11, not encrypted, Oodle)
- **repak** (`https://github.com/trumank/repak`): `repak` on the PATH or `C:\Program Files\repak_cli\bin\repak.exe`.
  Unpacking needs `oo2core_9_win64.dll` next to `repak.exe`. If repak reports an Oodle loader / access denied
  error, ask the user to run one `repak unpack` as administrator once (repak downloads the DLL itself);
  do not download it yourself.
- Python 3, .NET SDK 8+ (`dotnet build`), git. PowerShell may need the PATH refreshed from the registry to
  see newly installed tools.
- Ask the user, if not already known: did the patch notes mention AI settings, presets, console commands,
  weather or maps? It tells you where to look harder.

## 1. Extract the game files

```
repak unpack -o <work> -i "Test_C/Content/AISettings" "<paks>\pakchunk0-Windows.pak"
repak unpack -o <work> -i "Test_C/Content/Blueprints/MissionSystem/DataAssets/Maps" "<paks>\pakchunk0-Windows.pak"
repak unpack -o <work> -i "Test_C/Content/Blueprints/MissionSystem/Actors/Hideout" "<paks>\pakchunk0-Windows.pak"
repak unpack -o <work> -i "Test_C/Content/Blueprints/MissionSystem/Actors/Hideout" "<paks>\pakchunk7-Windows.pak"
```

(The hideout unit blueprints are split between pakchunk0 and pakchunk7.)

For the weapons, extract the whole inventory system from every pak (about 12 GB with the meshes; a separate
folder is fine, it is only read by `build_weapons.py`):

```
for each <pak> in <paks>\*.pak:
  repak unpack -o <work> -f -i "Test_C/Content/Blueprints/InventorySystem" <pak>
```

If a path is not in pakchunk0 any more, find it with `repak list <pak>` on each pak.

## 2. AI presets data (`defaults.json`)

```
python -I tools/build_defaults.py <work>\Test_C\Content\AISettings tools\reference_full_doc.json "<YYYY-MM-DD of the game build>"
```

The script prints what changed against the previous `defaults.json` (difficulty values, ranges, descriptions,
schema versions). Check:

- **Assertion "new menu params"**: the game shows new settings in its AI menu. Ask the user for screenshots of
  the in-game AI settings menu (every section), then add each new key to `NAMES` (exact in-game label) and
  `SECTIONS` (in-game section and order) in `tools/build_defaults.py`. Do not guess the label from the key.
- **Assertion "no longer in the game schema"**: remove the key from `NAMES` / `SECTIONS`.
- **WARNING "no longer shown in the game menu"**: confirm with the user's screenshots before removing it.
  (`DamageMap.*` is shown in game although not flagged, it is excluded from that warning.)
- **ADVANCED** (settings of the AI document that the game menu does not show, section hidden by default in
  the editor): `ADVANCED` in `tools/build_defaults.py` lists them with a name, a description and a slider
  range. **Assertion "params of ADVANCED no longer in the game schema / document"**: remove the key.
  **WARNING "shown nowhere in the tool, add them to ADVANCED"**: a new key in the document; add it to
  `ADVANCED` (game name / description from `Loc/en` if it has one, otherwise write them from the key and the
  difficulty values; they are flagged as unofficial in the tooltip). A key that moves into the game menu
  goes from `ADVANCED` to `NAMES` / `SECTIONS`.
- `aiOrder` / `simOrder` changed (new difficulty or simulation level): update `AI_LABELS` (in-game names:
  Easy=Recruit, Normal=Regular, Hard=Veteran, Extreme=Elite) and check `src/MainForm.cs`, which builds the
  DIFFICULTY / SIMULATION segmented rows from these lists.
- `aiHardVersion` changed: the share code document changed incompatibly - step 3 and 4 are mandatory, and
  old codes may no longer import in game. Mention it in the release notes.
- Simulation params are hard-coded (`BotMovementTicksPerSec`, `OptimizationRanges.1-4`): check
  `Schema/Simulation.json` for new keys.

## 3. Share code format

1. Check the exe still writes `IRR1` codes with the same layout (format string `%s%d-%s-%s`):
   ```
   python -I -c "import sys,re;d=open(sys.argv[1],'rb').read();[print(m.group().decode('utf-16le')) for m in re.finditer(rb'(?:[\x20-\x7e]\x00){2,}',d) if re.search(r'IRR\d|%s%d-%s-%s',m.group().decode('utf-16le'))]" "<exe>"
   ```
2. Ask the user to export two presets from the game (AI settings menu > preset > export, it copies a code):
   one custom AI preset and one with a CUSTOM simulation. Save each code as a `.txt` in `tools/testdata/`
   (e.g. `game_<build>_preset.txt`). Without game access, the existing test codes still catch regressions in
   the tool but not changes in the game.
3. Build the Debug exe and run the round-trip check:
   ```
   dotnet build src -c Debug
   python -I tools/check_roundtrip.py
   ```
   It must print `All codes round-trip.`. `note: number formatting differs` is accepted by the game.
   - `hv=…, tool has …`: schema version changed, see step 2 and 4.
   - `keys not in the tool` / `tool keys missing`: the AI document gained or lost keys: do step 4, rebuild
     `defaults.json` (step 2), rebuild and re-run.
   - checksum / not IRR1 errors: the code format changed. Reverse-engineer it from the exe again (look at
     the strings around `IRR` and the base64 / checksum code), then update `src/ShareCode.cs`,
     `tools/irr_sharecode.py` and `tools/check_roundtrip.py` together.

## 4. Reference document (`tools/reference_full_doc.json`)

Needed when the AI document's keys changed (step 3). Take the full AISettings document of a preset saved by
the game, either decoded from a fresh code:
```
python -I tools/irr_sharecode.py decode tools\testdata\<fresh code>.txt -o <work>\decoded.json
```
(then copy `vars[0].doc` - the `AISettings` var - into `tools/reference_full_doc.json`), or directly from
`%LOCALAPPDATA%\Test_C\Saved\AISettings\Presets\<id>\AISettings\<id>.json`. Keep the game's key order.
Then re-run step 2 and step 3.

## 5. Console commands

```
python -I tools/extract_console.py "<exe>" <work>\console
python -I tools/ue_param_types.py "<exe>"
```

- `extract_console.py` lists **NEW** names (in the exe, neither in the tool nor in `OMITTED`) and
  **REMOVED** commands. For each, read its help text in `<work>\console\console_commands.txt` (the help is the
  string just before or after the name).
  - New command useful to players: add it in `tools/build_commands.py` with the right category, arguments
    (see the helpers `toggle`, `num`, `choice`, `text`, `item` and `cv` for console variables) and the
    developer's help text. Add it to `ESSENTIALS` only if the user agrees.
  - Developer-only tuning variable: add it to `OMITTED`.
  - Removed command: delete it from `build_commands.py` (and from `ESSENTIALS`). Favorites that use it keep
    working as plain text; mention it in the release notes.
  - Changed help text (`console_commands.txt` vs `desc` in `build_commands.py`): update the description,
    especially when the accepted values changed.
- `ue_param_types.py` must end with `All parameter types as expected.`. A `!!` line means a cheat command
  argument changed type (e.g. weather index -> name): fix the argument in `build_commands.py` and the
  `EXPECTED` table. `Anchor parameter … not found` means the cheat functions were renamed: look at the
  reflection names around `SetWeatherType` / `GiveItem` with the strings dump and check them by hand.
- New cheat functions appear as ASCII reflection names next to `SetWeatherType`, `ForceExtraction`,
  `GiveItem` in the exe; their console names are the `irr.*` strings above.

## 6. Map weathers

```
python -I tools/map_weathers.py <work>\Test_C\Content\Blueprints\MissionSystem\DataAssets\Maps
```

It prints each map's weather list (order and weight) and rewrites `tools/map_weathers.json`
(`git diff tools/map_weathers.json` shows the changes).
- `NO WEATHER LIST FOUND` for a map: its asset layout changed; inspect the `.uexp` by hand.
- New map: add it to `MAP_ORDER` in `tools/build_commands.py` (or to `MAP_SKIP` if it is not playable, like
  `Lobby`). New UDS weather preset: add its display name to `WEATHER_NAMES`.
- `WEATHER_INDEX_BASE` (first number accepted by `irr.World.SetWeather`) is 0, checked in game on Delta
  (2026-11-07). Keep it unless the user tests in game that the numbers are shifted; then change it here only.

## 7. Items

```
python -I tools/list_items.py "<paks>"
```

Rewrites `tools/item_paths.txt` and prints the added / removed item definitions. New top-level item
folders become new categories automatically (see `load_items` in `build_commands.py`).

## 7b. Hideout upgrades

Run after step 7 (it uses `tools/item_paths.txt` to find the item names for the GIVE buttons):

```
python -I tools/build_hideout.py <work>
```

It reads the `BP_IRRHideoutUnitComponent` defaults of every blueprint in `Actors/Hideout` and rewrites
`src/Resources/hideout.json` (`git diff src/Resources/hideout.json` shows the cost changes). The JSON keeps
the game's level numbers; the app hides a free first level (nothing to pay, no requirement: Structure,
Shooting Range today) and renumbers that unit's levels and the requirements on it (`HideoutData.Load`). The
saved progress (`hideout.json` version 2) uses the shown numbers; version 1 files are converted on load. Check:
- every unit of `UNIT_ORDER` is listed (`unit X not found` = renamed or moved blueprint; a new unit is
  listed after the known ones: add it to `UNIT_ORDER` at the right place);
- no `WARNING` (unknown unit in a requirement, item without item definition: fix the matching in
  `item_id()` or check the tag in the game);
- a `ValueError` (unexpected header, unknown condition class, text history) means the cooked layout changed:
  look at the bytes at the reported offset and update the reader instead of skipping the check.
- New faction tag in a requirement: add it to `FACTIONS` (abbreviation used in game, full name).

## 7d. Weapons (WEAPONS tab)

```
python -I tools/build_weapons.py <work>
```

Reads every item definition (`Items/**/ID_*`) of the weapons, attachments and ammo, their attachment slots
(container subobjects, classes in `Objects/ItemContainers`) and the parts each slot accepts (supported items:
inline tag lists or `UISI_*` classes in `Objects/InventorySupportedItems`; a subobject without its own list takes
the one of the same subobject in the container class defaults). Writes `src/Resources/weapons.json` and prints
the counts (49 weapons, 513 attachments, 21 ammo today), the attachments no weapon accepts and the tags that
match no item (tags left in the game data for removed items: expected, 4 today). It also reads the vendor table
`Vendor/DT_Vendor_Old` (the row names `additem` takes, 825 rows) and the item presets `Presets/**/DA_*` (default
parts of each weapon, loaded magazines) and prints the items additem cannot give (1 today), how many have another
additem name (34) and the preset parts that fit no slot (10 `WARNING ... (left out)`: data of the game). Check:
- the counts against the previous run; `git diff src/Resources/weapons.json` shows the compatibility changes;
- a `ValueError` (unexpected container layout, supported items array not found, two classes with the same name)
  means the cooked layout changed: look at the bytes and update the reader instead of skipping the check;
- new slot class: its name comes from the game text of its class (`LABELS` overrides unclear ones, `SLOT_ORDER`
  sets the display order); new weapon class folder: add it to `WEAPON_CLASSES`;
- the WEAPONS screenshots (`_weapons`, `_weapons_aug`): the test builds keep their parts (if a part is gone,
  the slot names or the part names changed).

Run it before `build_icons.py` (the weapon, part and ammo icons come from `weapons.json`).

Then refresh the icons (it lists the paks, extracts only the needed textures, ~2 min with the weapons):

```
python -I tools/build_icons.py "<paks>"
```

It writes one 64 px PNG per hideout item, map key, part and ammo (`item_<name>.png`, weapons in 128 px), cash
and material (`res_<name>.png`) and removes icons no longer used. `WARNING: no *_Icon texture` is expected for
items the game has no icon for (18 weapons and parts today: AS Val, VSS, M14, SR-25, MPX...): the app shows the
silhouette of the item's class (`cat_<class or part folder>.png`, from `CATEGORIES`: the game's inventory slot
textures, some in BC7 - modes 0 and 2 are not decoded, the script says so if one shows up). `pixel format … not handled`: add the format to
`read_texture` / `decode`. Look at a few new icons before committing. Icons are added little by little:
to ship more, add item names to `EXTRA_ITEMS`.

## 7c. Resource maps

```
python -I tools/build_maps.py "<paks>"
```

Takes ~2-3 minutes and ~1.5 GB in %TEMP% (it extracts the persistent levels). It prints, per map, the floors,
the number of resource points per type, of extraction points and of locks (safes / doors per kind), the spawn
chance / amount of each type and the number of key items. Run it before `build_icons.py` (the key icons come
from `maps.json`).
- Map size: like the in-game PDA (W_GCPS_Map / MPC_Minimap), half size = `MapSize` / 2 of the minimap volume,
  or box extent + `MAP_MARGIN` (200) when MapSize is not set. Delta 90 000, Quarry 62 500, Outskirts 85 419,
  Bunker 16 000 today.
- Locks: classes of `LOCKS` + any `BP_Door_*Locked*_C`; the key is the `Inventory.Items.Keys.*` tag the actor
  references. Doors without a key are skipped (they never open). `references several keys` or `key without
  item definition`: check the actor by hand. New lock class (new safe type...): add it to `LOCKS` and to
  `MapData.LockKinds` / labels / colours in `src/Maps.cs` and `src/MapsPanel.cs`.
- New map: add it to `MAPS` (display name, persistent level path). A map whose minimap is a grey plan goes in
  `PLAN_MAPS` (drawn inverted, like the Bunker).
- `first property ... expected 161` (or 5 / 0): the component layout changed; check `LOCATION_INDEX`.
- `overrides a blueprint property`: a point has its own chance or amount - read it from the actor instead of
  using the class default.
- Spawn chance / amount changed: compare with the previous `maps.json` (`git diff`), it is shown in the app.
- Check the MAPS screenshots: points must sit on buildings / the quarry rocks. If they are rotated or mirrored,
  the minimap capture changed: fix `MapFloor.ToImage` in `src/Maps.cs`.

## 8. Regenerate and build

```
python -I tools/build_commands.py
dotnet build src -c Debug
python -I tools/check_roundtrip.py
src\bin\Debug\net462\IRRCompanion.exe --screenshot <work>\shot
```

`--screenshot <prefix> [ui size]` (Debug build only, e.g. `1.25` to check the UI size setting) writes PNGs of every view: `_edit`, `_edit_scrolled`,
`_edit_advanced_closed`, `_edit_advanced`, `_edit_performance`, `_edit_changed_only`, `_builtin`, `_compare`, `_compare_diff`, `_compare_advanced`, `_compare_difficulties`, `_commands_weather`, `_commands_item`,
`_commands_player`, `_commands_cvar`, `_commands_favorites`, `_hideout_unit`, `_hideout_stash`,
`_weapons`, `_weapons_aug`, `_hideout_shopping`, `_hideout_shopping_all`, `_maps_quarry`, `_maps_delta_hover`, `_maps_bunker`, `_maps_safe_hover` (and `.error.txt` if it crashed). Look at each image:
- PRESETS: header (name, state line, SAVE / COPY CODE / COMPARE / ⋯), every section of the in-game menu, names not cut, values formatted like the game, nothing scrolled;
  the ADVANCED and PERFORMANCE bars at the bottom, closed, then open with their sections in order;
  *Only changed settings* hides the equal rows and empty sections.
- COMPARE (opened from the editor, BACK TO PRESET button): section rows, colors, range bars.
- COMMANDS: categories and counts, weather lists, item picker, favorites buttons.
- WEAPONS: base weapon with its icon, slot rows with the sub-slots indented under their part, magazines and
  rounds fields, the console line (`additem X 1 | ...`).
- HIDEOUT: unit list states, level cards (requirements green/red, items, materials), shopping list totals.
If the screenshots are fine and the UI changed, refresh the README images: `docs/editor.png` (`_edit`),
`docs/editor-advanced.png` (`_edit_advanced`),
`docs/compare.png` (`_compare_difficulties`), `docs/commands.png` (`_commands_weather`),
`docs/commands-items.png` (`_commands_item`), `docs/favorites.png` (`_commands_favorites`),
`docs/weapons.png` (`_weapons`), `docs/hideout.png` (`_hideout_stash`), `docs/hideout-shopping.png` (`_hideout_shopping`), `docs/maps.png`
(`_maps_quarry`), `docs/maps-hover.png` (`_maps_delta_hover`), `docs/maps-bunker.png` (`_maps_bunker`), `docs/maps-safe.png` (`_maps_safe_hover`).

The real GUI cannot be clicked from the sandbox: tell the user what to test by hand (save, import a code,
copy a code and import it in game, copy a console command and run it in game).

## 9. Release

1. Bump `<Version>` in `src/IRRCompanion.csproj` (minor for new data/features, patch for data-only updates).
2. Build Release and check the test code is not in it:
   ```
   dotnet build src -c Release
   ```
   `src/bin/Release/net462/IRRCompanion.exe` must not contain `RenderTestShots` / `TestSelect`.
3. Update `README.md` if features, sources or steps changed.
4. Show the user the summary of changes (data, commands, items, weathers, format) and what is not verified
   in game. Commit only when the user asks; tag `vX.Y.Z` and push only when the user asks - the GitHub Actions
   workflow (`.github/workflows/build.yml`) builds the exe and publishes the release for `v*` tags.
5. Update the memory notes about the game (share code format, file locations) if something changed.

## Checklist

- [ ] Game build noted (Steam `buildid`, date) and passed to `build_defaults.py`
- [ ] `build_defaults.py` runs without assertion; changes reviewed (new settings named from in-game screenshots)
- [ ] Share code format unchanged (or code updated); `check_roundtrip.py` passes with a fresh game export
- [ ] `reference_full_doc.json` refreshed if the AI document changed
- [ ] `extract_console.py`: no NEW / REMOVED left unhandled; `ue_param_types.py` all ok
- [ ] `map_weathers.py`: every map has a list; new maps/weathers named
- [ ] `list_items.py` run; `build_commands.py` run
- [ ] `build_hideout.py`: all units found, no warning; cost changes reviewed
- [ ] `build_weapons.py`: counts reviewed, no `ValueError`; compatibility changes reviewed
- [ ] `build_icons.py` run; new icons look right
- [ ] `build_maps.py`: every map found, point counts reviewed, no warning; MAPS screenshots show points on buildings
- [ ] Debug build, screenshots reviewed; Release build clean
- [ ] Version bumped, README updated, change summary given to the user
