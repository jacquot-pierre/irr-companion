# IRR Preset Tool

An unofficial editor for **Incursion Red River** AI presets. One small portable `.exe`, nothing to install.

![Editor](docs/editor.png)

## Download

Grab `IRRPresetTool.exe` from the [latest release](../../releases/latest) and run it.
It needs .NET Framework 4.6.2 or newer, which is built into Windows 10 and Windows 11.

> **Windows SmartScreen:** the exe is not code-signed, so Windows may show *"Windows protected your PC"*
> the first time. Click **More info → Run anyway**. Every release is built by GitHub Actions from the
> source in this repository, and its SHA-256 hash is listed in the release notes.

## Features

- **Game presets built in**: the difficulties *Recruit, Regular, Veteran, Elite* and the simulation
  levels *Potato, Low, Normal, High*.
- **Same settings as the in-game menu**: the 35 AI settings and the simulation settings, in the game's
  order, with the game's descriptions as tooltips. Values outside the recommended range are shown in orange.
- **Reference values**: next to each setting, the value of a preset of your choice (▲ above / ▼ below).
- **Compare** two presets side by side: differences highlighted, change in value and %, and a range bar
  showing both values.
- **Your presets stay in the tool** (`%APPDATA%\IRR Preset Tool\presets.json`). Nothing is written to
  the game's files.
- **Share codes**: *Copy code* gives an `IRR1-...` code to paste in the game's preset import;
  *Import code* reads a code exported by the game or by another player.

![Compare](docs/compare.png)

## How to use

1. Select a game difficulty and click **DUPLICATE** (or **NEW**).
2. Change the settings, give the preset a name, click **SAVE**.
3. Click **COPY CODE** and paste the code in the game's AI settings preset import.

## Share code format

`IRR1-` + base64 of the compact JSON document (no `=` padding) + `-` + checksum.
The checksum is djb2-xor (`h = 5381; h = (h * 33) ^ c`, 32-bit) computed over the base64 text and
written as 4 base-36 digits (`0-9A-Z`), least significant digit first.
`tools/irr_sharecode.py` encodes and decodes codes from the command line.

The JSON document looks like:

```json
{ "t": "p", "id": "my_preset",
  "preset": { "presetId": "my_preset", "displayName": "My preset", "description": "", "author": "",
              "version": 1, "savedAt": "2026.11.07-13.21.49",
              "selections": { "aISettings": "my_preset", "weapons": "Normal", "barks": "Normal",
                              "simulation": "High", "global": "Normal", "menuStyle": "Normal" } },
  "vars": [ { "c": "AISettings", "n": "my_preset", "hv": 2, "doc": { "...": "..." } } ] }
```

A custom simulation adds `{ "c": "Simulation", "n": "my_preset", "hv": 1, "doc": { ... } }` to `vars`
and sets `selections.simulation` to the preset id.

## Building

Requires the .NET SDK (8 or newer).

```
dotnet build src -c Release
```

The output is `src/bin/Release/net462/IRRPresetTool.exe`.

### Updating the game data

`src/Resources/defaults.json` is generated from the game files. After a game update:

1. Extract `Test_C/Content/AISettings/` from `Test_C/Content/Paks/pakchunk0-Windows.pak`
   with [repak](https://github.com/trumank/repak):
   `repak unpack -o <out> -i "Test_C/Content/AISettings" pakchunk0-Windows.pak`
2. Run `python -I tools/build_defaults.py <out>/Test_C/Content/AISettings tools/reference_full_doc.json`
   (`reference_full_doc.json` is a full AISettings document saved by the game; it gives the key order and
   the engine defaults of keys that `Normal.json` does not set).
3. Rebuild.

## Releasing

Push a tag `vX.Y.Z`: the GitHub Actions workflow builds the exe and publishes a release with it.

## Disclaimer

This is a fan-made tool, not affiliated with or endorsed by the developers of Incursion Red River.
The default values and descriptions in `src/Resources/defaults.json` come from the game and belong to
their owners. The source code is under the MIT license (see `LICENSE`).
