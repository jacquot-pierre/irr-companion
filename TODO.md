# TODO

What is still to do or to verify for IRR Companion. Tick items as they are done.

## Verify in game

Console commands (the values were read from the game files, not tried in game):

- [ ] `irr.World.SetWeather`: does the first weather of a map have number **1** or **0**? Try
      `irr.World.SetWeather 1` on Delta: clear skies means 1 is right. If not, set `WEATHER_INDEX_BASE = 0`
      in `tools/build_commands.py`, run it and rebuild.
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

- [ ] Starting levels: Structure and Shooting Range at level 1 (free level 1 in the files), every other unit
      at level 0 (not built). Check on a new character.
- [ ] Costs and requirements of a few levels match the in-game upgrade screen (e.g. Generator 1, Stash 2,
      Intel Center 3).
- [ ] Faction loyalty levels: the requirement numbers (1 to 4) are the loyalty levels shown in game.
- [ ] GIVE buttons: `irr.Item.Give <item> <count>` with the internal names (`GasCan`, `PowerCable`…),
      `irr.Item.Give Cash <n>` and `irr.Item.AddResource Oil <n>` work in game.

Maps:

- [ ] Resource points on Delta / Quarry / Outskirts / Bunker match the game (a few spots checked in a raid).
- [ ] About 40 % of the points have a resource in a raid (no other limit applied by the game).
- [ ] Bunker floors: points shown on the right floor.
- [ ] Safes and locked doors: positions and keys match the game (e.g. an Outskirts large safe, a HAS door).
- [ ] Doors without a key in the files are hidden (8 on Delta, 26 on Quarry): check they really never open.

## Verify the app

The interface was only checked on screenshots; nothing was clicked yet.

- [ ] EDIT: NEW, DUPLICATE, DELETE, SAVE (and `Ctrl+S`), REVERT, the save prompt when closing.
- [ ] COPY CODE, VIEW CODE, IMPORT CODE (pre-filled from the clipboard).
- [ ] COMPARE: both lists, swap, *Only differences*.
- [ ] COMMANDS: search, COPY, double-click / Enter to copy, DEFAULTS, optional arguments (tick box),
      item list filter and autocomplete, editing the command text by hand.
- [ ] Favorites: add (name prompt), SAVE after changing values, RENAME, REMOVE, ▲ / ▼, they survive a restart.
- [ ] HIDEOUT: player level and loyalty fields, MY LEVEL buttons, requirement links, NEXT UPGRADES /
      EVERYTHING LEFT, GIVE buttons; the progress survives a restart (`%APPDATA%\IRR Companion\hideout.json`).
- [ ] MAPS: map and floor lists, filters, hover tooltip, zoom with the wheel, drag, double-click to fit.
- [ ] First start: presets and favorites copied from `%APPDATA%\IRR Preset Tool` to `%APPDATA%\IRR Companion`.
- [ ] Display scaling 125 % / 150 % and the minimum window size (1000 × 620): nothing cut or overlapping.
- [ ] Windows 10 and a PC without development tools (download from the release page, SmartScreen).

## Repository and releases

- [ ] First GitHub Actions run: the build passes and the `v1.0.0` release has the exe and the notes.
- [ ] Delete the old local folder `Documents\IRRPresetTool` once everything is in this repository.
- [ ] Before making the repository public: the test codes in `tools/testdata/` contain the author name of the
      presets; decide whether to keep them.
- [ ] Optional: code-sign the exe to avoid the SmartScreen warning.

## Ideas

- [ ] Names of the extraction points (not found in the level data; maybe from the game's map setup).
- [ ] More map layers: loot containers, mission spawn points (positions are in the same level files).

- [ ] More icons: item picker of the COMMANDS tab, favorites (add item names to `EXTRA_ITEMS` in
      `tools/build_icons.py`, a few at a time). The Honey Badger has no icon in the game files.

- [ ] Support `IRR2-` share codes (seen in the game's text, format unknown - probably compressed).
- [ ] List of mission names and operation ids for `irr.Mission.Start` / `irr.Operations.Start`.
- [ ] Show an item's display name and icon next to its internal name.
- [ ] Unit tests for the share code checksum and the command line builder.
