# TODO

What is still to do or to verify for IRR Companion. Tick items as they are done.

## Verify in game

Console commands (the values were read from the game files, not tried in game):

- [x] `irr.World.SetWeather`: the first weather of a map has number **0** (checked in game on Delta);
      `WEATHER_INDEX_BASE = 0` in `tools/build_commands.py`.
- [ ] `irr.Item.Spawn` / `irr.Item.Give`: are the internal item names accepted (e.g. `Bandage`, `Glock_19`)?
      `irr.Item.List All 1` shows the names the game expects.
- [ ] Arguments with spaces: does the console accept quoted values, e.g.
      `irr.World.SetHideoutUnitLevel "Repair Station" 2`?
- [ ] `irr.World.SetFactionReputation`: which faction names work (`BLUEFOR`, `OPFOR`, `GREYFOR`, `FANG`, or the
      allegiances `IGC`, `UICS`, `VLF`)?
- [ ] `irr.Item.AddResource`: `Food` or `Resource.Food`?
- [ ] `irr.World.SetHideoutUnitLevel`: unit names (`Stash`, `Generator`…) and level range.
- [ ] Commands marked **HOST**: really host only? Which Essentials work as a client in multiplayer?
- [ ] Every Essential command works in the released game (some debug commands may be disabled in Shipping).
- [ ] `irr.Mission.Start`: list of mission names that work (could become a list in the app).

Share codes:

- [ ] Paste a code made by the app with a **custom simulation** in the game's import (only a custom AI
      preset has been imported in game so far).
- [ ] Import in the app a code exported by another player.

Hideout (costs read from the game files):

- [ ] Starting levels: Structure and Shooting Range built from the start (free level 1 in the files, hidden in the
      app, which counts their levels from the first paid upgrade); every other unit
      at level 0 (not built). Check on a new character.
- [ ] Costs and requirements of a few levels match the in-game upgrade screen (e.g. Generator 1, Stash 2,
      Intel Center 3).
- [ ] Faction loyalty levels: the requirement numbers (1 to 4) are the loyalty levels shown in game.
- [ ] GIVE buttons: `irr.Item.Give <item> <count>` with the internal names (`GasCan`, `PowerCable`…),
      `irr.Item.Give Cash <n>` and `irr.Item.AddResource Oil <n>` work in game.

Weapon builder (compatibility read from the game files):

- [ ] `additem <name> <count>` and several commands joined with ` | ` work in the console. The names now come
      from the vendor table `DT_Vendor_Old` (`additem MP5_Picanny_Scope_Mount 1` did not work in game: the row
      is `MP5_Picatinny_Rail`); check a few renamed ones (`MP5_Picatinny_Rail`, `545x39_7N22`, `AS_VAL`).
- [ ] A weapon given with `additem` comes with the parts of its default preset and a loaded magazine (read from
      the presets, the builder leaves them out of the command). 10 presets name parts that fit no slot of the
      weapon (e.g. an MP5 end cap on the SCAR-H): left out, what does the game do with them?
- [ ] A few builds assemble at the gunsmith exactly as in the app (e.g. AKS-74U with the B-11 handguard, a grip
      and a PEQ-15; AUG with the 20" barrel and the SL7i suppressor).
- [ ] Magazines arrive loaded (magazine presets: 30 × 5.45 PS...), the x300 rows give the AR-15 magazines
      loaded with x300.
- [ ] Slots of the AKs: the "Rail Mount" slot (the game's text says "Cover", like the dust cover).

Maps:

- [ ] Resource points on Delta / Quarry / Outskirts / Bunker match the game (a few spots checked in a raid).
- [ ] About 40 % of the points have a resource in a raid (no other limit applied by the game).
- [ ] Bunker floors: points shown on the right floor.
- [ ] Safes and locked doors: positions and keys match the game (e.g. an Outskirts large safe, a HAS door).
- [ ] Doors without a key in the files are hidden (8 on Delta, 26 on Quarry): check they really never open.

Advanced settings (not in the game menu):

- [ ] A preset with changed advanced values imports in game and the bots behave accordingly (e.g. a high
      Trigger Delay or Min Visible Body Part).
- [ ] Our names / descriptions of the advanced settings match what they do (they were written from the key
      names and the difficulty values, not from the developers).

## Verify the app

The interface was only checked on screenshots; nothing was clicked yet.

- [ ] PRESETS: + NEW, MAKE MY OWN COPY, SAVE (and `Ctrl+S`), the save prompt when closing.
- [ ] PRESETS ⋯ menu: revert, view code, duplicate, author and description, delete.
- [ ] COPY CODE, IMPORT CODE (pre-filled from the clipboard).
- [ ] PRESETS: *Only changed settings*, the state line, ADVANCED and PERFORMANCE bars open / close.
- [ ] COMPARE (button of the editor): both lists, swap, *Only differences*, *Advanced* (and its "+N" count),
      BACK TO PRESET and `Esc`.
- [ ] COMMANDS: search, COPY, double-click / Enter to copy, DEFAULTS, optional arguments (tick box),
      item list filter and autocomplete, editing the command text by hand.
- [ ] Favorites: add (name prompt), SAVE after changing values, RENAME, REMOVE, ▲ / ▼, they survive a restart.
- [ ] HIDEOUT: player level and loyalty fields, MY LEVEL buttons, requirement links, NEXT UPGRADES /
      EVERYTHING LEFT, GIVE buttons; the progress survives a restart (`%APPDATA%\IRR Companion\hideout.json`).
- [ ] WEAPONS: + NEW BUILD, base weapon list (class headers cannot be picked), part lists and their sub-slots,
      magazines and rounds fields, SAVE (and `Ctrl+S`), the save prompt when switching builds and when closing,
      ⋯ menu (revert, duplicate, delete), COPY COMMAND; the builds survive a restart
      (`%APPDATA%\IRR Companion\weapons.json`). The mouse wheel scrolls the page, not the part lists.
- [ ] MAPS: map and floor lists, filters, hover tooltip, zoom with the wheel, drag, double-click to fit.
- [ ] First start: presets and favorites copied from `%APPDATA%\IRR Preset Tool` to `%APPDATA%\IRR Companion`.
- [ ] Display scaling 125 % / 150 % and the minimum window size (1000 × 620): nothing cut or overlapping.
- [ ] UI size setting (status bar): restart prompt, every tab at 150 % / 200 % (checked on screenshots at 125 %
      only), window size clamped to the screen.
- [ ] Windows 10 and a PC without development tools (download from the release page, SmartScreen).

## Repository and releases

- [x] First GitHub Actions run: the build passes and the `v1.0.0` release has the exe and the notes.
- [x] `v1.1.0` release: the exe and the notes are on the release page.
- [ ] `v1.2.0` release: the exe and the notes are on the release page.
- [ ] Delete the old local folder `Documents\IRRPresetTool` once everything is in this repository.
- [ ] Before making the repository public: the test codes in `tools/testdata/` contain the author name of the
      presets; decide whether to keep them.
- [ ] Optional: code-sign the exe to avoid the SmartScreen warning.

## Ideas

- [ ] Per-stance modifiers of `behaviorConfig` (patrol / investigate / combat: vision speed and distance,
      hearing, perception delay, aim) in the ADVANCED section; same in every difficulty today.

- [ ] Names of the extraction points (not found in the level data; maybe from the game's map setup).
- [ ] More map layers: loot containers, mission spawn points (positions are in the same level files).

- [ ] More icons: item picker of the COMMANDS tab, favorites (add item names to `EXTRA_ITEMS` in
      `tools/build_icons.py`, a few at a time). Weapons, parts and ammo have their icons now; 8 of them (VSS, M14,
      SR-25, MPX...) have no icon texture in the game files.
- [ ] Weapon builder: stats of the build (ergonomics, recoil, weight are in the item definitions: `U_Ergonomics`,
      `U_Recoil`, `U_Weight`).
- [ ] Weapon builder: share a build (copy / paste the build as text).

- [ ] Support `IRR2-` share codes (seen in the game's text, format unknown - probably compressed).
- [ ] List of mission names and operation ids for `irr.Mission.Start` / `irr.Operations.Start`.
- [ ] Show an item's display name and icon next to its internal name.
- [ ] Unit tests for the share code checksum and the command line builder.
