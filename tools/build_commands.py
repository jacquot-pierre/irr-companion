r"""Generates src/Resources/commands.json: the console commands shown in the COMMANDS tab.

Usage:  python -I tools/build_commands.py [tools/item_paths.txt]

The command names and help texts come from the game executable (UTF-16 strings next to each
`irr.*` name); argument types come from the UE reflection data of the cheat functions; the
weather lists come from Blueprints/MissionSystem/DataAssets/Maps/MD_<Map>.uasset.
item_paths.txt is the list of item definition assets (`repak list pakchunk*.pak | grep /ID_`).

Argument kinds understood by the tool:
  toggle   0/1 (OFF/ON)               int / float  number (min/max -> slider)
  choice   fixed or editable list     text         free text
  item     item picker (items list)   weather      map + weather -> index
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'src', 'Resources', 'commands.json')

# First weather number accepted by irr.World.SetWeather (not verified in game yet).
WEATHER_INDEX_BASE = 1

# Weather lists come from tools/map_weathers.json (written by tools/map_weathers.py):
# for each map, the UDS weather presets in the order of WeatherTypeProbabilities, with their weight.
MAP_WEATHERS = os.path.join(HERE, 'map_weathers.json')
MAP_ORDER = ['Delta', 'Quarry', 'Outskirts', 'Bunker', 'Hideout']  # maps not listed here come after
MAP_SKIP = {'Lobby'}  # menu map, not a playable map
# Display names of the UDS weather presets (unknown presets are shown with "_" replaced by spaces)
WEATHER_NAMES = {
    'ClearSkies': 'Clear skies', 'Clear_Skies': 'Clear skies', 'Cloudy': 'Cloudy', 'Foggy': 'Foggy',
    'Overcast': 'Overcast', 'Partly_Cloudy': 'Partly cloudy', 'Rain': 'Rain', 'Rain_Light': 'Light rain',
    'Rain_Thunderstorm': 'Thunderstorm', 'Snow': 'Snow', 'Snow_Light': 'Light snow', 'Snow_Blizzard': 'Blizzard',
    'Sand_Dust_Calm': 'Dust', 'Sand_Dust_Storm': 'Dust storm',
}


def load_maps():
    raw = json.load(open(MAP_WEATHERS, encoding='utf-8'))
    names = [m for m in MAP_ORDER if m in raw] + sorted(m for m in raw if m not in MAP_ORDER and m not in MAP_SKIP)
    return [(m, [(WEATHER_NAMES.get(w, w.replace('_', ' ')), weight) for w, weight in raw[m]]) for m in names]


ESSENTIALS = [
    'irr.World.SetWeather', 'irr.World.SetTimeOfDay', 'irr.Player.ForceExtraction', 'irr.Item.Spawn', 'irr.Item.Give',
    'irr.Player.Invulnerable', 'irr.Player.Invisible', 'irr.Player.InfiniteAmmo', 'irr.Player.KeepInventory',
    'irr.Player.CheatMoveSpeed', 'irr.Item.SetDurability', 'irr.Raid.TimerAdd', 'irr.Raid.TimerPause',
    'irr.AI.KillAll', 'irr.AI.Enable', 'irr.Player.Perspective', 'irr.AI.Headlamps', 'irr.Menu.Freecam',
    'irr.CineCam', 'irr.Menu.SpawnMenu', 'irr.Menu.CommandMenu',
]

# irr.* names of the exe deliberately left out of the tool (fine-tuning variables for the developers,
# sub-settings of a listed command, prototype tweaks, crash helper). tools/extract_console.py reports
# any exe name that is neither a command below nor in this list.
OMITTED = [
    'irr.Debug.ForceCrash',
    'irr.AI.NewTargetPaths.TestSplice', 'irr.Traversal.CompareLegacyScan', 'irr.Audio.VoiceLoopbackPlay',
    'irr.AI.VisCache.BinDegrees', 'irr.AI.VisCache.Lifetime', 'irr.AI.VisCache.MaxOrigins', 'irr.AI.VisCache.OriginCellSize',
    'irr.AICover.ClaimedReTraceDistance', 'irr.AICover.EscalationRadius', 'irr.AICover.FailedScopeRetryInterval',
    'irr.AICover.FullColumnChunkRays', 'irr.AICover.FullReTraceDistance', 'irr.AICover.MaxScopedCovers',
    'irr.AICover.MinScopedCovers', 'irr.AICover.QueryLeaseSeconds', 'irr.AICover.RescopeDistance',
    'irr.AICover.ReservationSeconds', 'irr.AICover.ScopeRadius', 'irr.AICover.TraceBudgetPerTick',
    'irr.AICover.UpdateInterval',
    'irr.AIDebug.CoverCache.Range', 'irr.AIDebug.DoorLinks.Range', 'irr.AIDebug.SearchGrid.Range',
    'irr.AIDebug.TreeCrowns.Range', 'irr.Audio.AcousticDebug.Range', 'irr.Audio.DebugRooms.Range',
    'irr.Nav.DebugRooms.Range', 'irr.Nav.DebugRooms.Actors',
    'irr.Avoidance.EnemyKnownLocation', 'irr.Avoidance.FriendlyPosition', 'irr.Avoidance.FriendlyPositionPeaceGate',
    'irr.FX.CosmeticBullets.Send', 'irr.Net.ContainedItemCullDistance', 'irr.Outfit.MergeKeepAlive',
    'irr.Perf.TickHygiene.Interval',
    'irr.SpawnQueue.ActivationBudgetMs', 'irr.SpawnQueue.CharacterSpawnBudgetMs', 'irr.SpawnQueue.EquipBudgetMs',
    'irr.SpawnQueue.PossessionBudgetMs', 'irr.SpawnQueue.ValidationBudgetMs', 'irr.SpawnQueue.ValidationHoldSeconds',
    'irr.SpawnQueue.ValidationRecheckSeconds',
    'irr.Spectator.Bone', 'irr.Spectator.HeadOffset', 'irr.Spectator.Menu', 'irr.Spectator.Mount',
]

CATEGORIES = ['World & Raid', 'Player', 'Items & Progression', 'Missions & Operations', 'AI',
              'Cameras & Menus', 'Graphics & Audio', 'Debug']


# ---------------------------------------------------------------- argument helpers
def toggle(name='Enabled', default=1, help=None, off='OFF', on='ON'):
    a = {'name': name, 'kind': 'toggle', 'default': default, 'off': off, 'on': on}
    if help: a['help'] = help
    return a


def num(name, kind='int', min=None, max=None, default=None, step=None, unit=None, help=None, optional=False):
    a = {'name': name, 'kind': kind}
    for k, v in (('min', min), ('max', max), ('default', default), ('step', step), ('unit', unit), ('help', help)):
        if v is not None: a[k] = v
    if optional: a['optional'] = True
    return a


def choice(name, options, default=None, editable=False, help=None, optional=False):
    opts = [[str(o[0]), o[1]] if isinstance(o, (list, tuple)) else [str(o), str(o)] for o in options]
    a = {'name': name, 'kind': 'choice', 'options': opts, 'default': str(default if default is not None else opts[0][0])}
    if editable: a['editable'] = True
    if help: a['help'] = help
    if optional: a['optional'] = True
    return a


def text(name, default='', help=None, optional=False):
    a = {'name': name, 'kind': 'text', 'default': default}
    if help: a['help'] = help
    if optional: a['optional'] = True
    return a


def item(name='Item', help=None):
    a = {'name': name, 'kind': 'item', 'default': 'Bandage'}
    if help: a['help'] = help
    return a


CMDS = []


def c(cmd, cat, title, desc, args=(), host=False, warn=None, cvar=False):
    e = {'cmd': cmd, 'cat': cat, 'title': title, 'desc': desc, 'args': list(args)}
    if host: e['host'] = True
    if warn: e['warn'] = warn
    if cvar: e['cvar'] = True
    CMDS.append(e)


def cv(cmd, cat, title, desc, arg=None, **kw):
    """Console variable: the value is optional (alone, the command prints the current value)."""
    if arg is None: arg = text('Value', optional=True)
    arg = dict(arg)
    arg['optional'] = True
    c(cmd, cat, title, desc, [arg], cvar=True, **kw)


ONOFF = lambda d=1: toggle(default=d)

# ---------------------------------------------------------------- World & Raid
W = 'World & Raid'
c('irr.World.SetWeather', W, 'Set weather',
  'Set weather type with optional transition. The weather number depends on the map: pick the map and the weather, the tool writes the number.',
  [{'name': 'Weather', 'kind': 'weather', 'default': 'Delta:0'},
   num('Transition', 'float', 0, 120, 10, 1, 's', 'Duration of the change, in seconds.', optional=True)], host=True)
c('irr.World.SetTimeOfDay', W, 'Set time of day', 'Set time of day (24h hour).',
  [num('Hour', 'int', 0, 23, 12, 1, 'h')], host=True)
c('irr.Player.ForceExtraction', W, 'Force extraction', 'Force extraction.')
c('irr.Raid.Timer', W, 'Raid timer', 'Raid time limit; out of time = missing in action. Saved per user.', [ONOFF()])
c('irr.Raid.TimerDuration', W, 'Raid duration', 'Raid length in minutes (restarts a running clock).',
  [num('Minutes', 'int', 1, 180, 45, 1, 'min')])
c('irr.Raid.TimerAdd', W, 'Add raid time', 'Add minutes to the running clock (negative removes).',
  [num('Minutes', 'int', -60, 120, 10, 1, 'min')])
c('irr.Raid.TimerPause', W, 'Pause raid timer', 'Pause/resume the running clock.')
c('irr.Raid.TimerRestart', W, 'Restart raid timer', 'Restart the running clock at the full duration.')
c('irr.World.EnableLoot', W, 'Enable loot', 'Enable/disable loot.', [ONOFF()])
c('irr.World.EnableStreamingVolumes', W, 'Streaming volumes', 'Enable/disable streaming volumes.', [ONOFF()])
c('irr.World.ToggleExtendedParty', W, 'Extended party', 'Toggle extended party.', [ONOFF()])
c('irr.World.Reset', W, 'Reset world', 'Reset world state.', warn='Resets the world state of your save.')
c('irr.World.ResetSafehouse', W, 'Reset safehouse', 'Reset safehouse.', warn='Resets your safehouse.')

# ---------------------------------------------------------------- Player
P = 'Player'
c('irr.Player.Invulnerable', P, 'Invulnerable', 'Make the local character invincible.', [ONOFF()])
c('irr.Player.TechnicalInvulnerable', P, 'Technical invulnerable',
  'Hits still open bleeds and fractures, but health is not reduced.', [ONOFF()])
c('irr.Player.Invisible', P, 'Invisible to AI', 'Hide the local player from AI sensing.', [ONOFF()])
c('irr.Player.InfiniteAmmo', P, 'Infinite ammo', 'Infinite ammunition (+ durability). Always on in hideout.', [ONOFF()])
c('irr.Player.KeepInventory', P, 'Keep inventory', 'Keep inventory on death.', [ONOFF()])
c('irr.Player.CheatMoveSpeed', P, 'Fast movement', 'Boost local movement speed.', [ONOFF()])
c('irr.Player.Perspective', P, 'First / third person', 'First/third person perspective.',
  [toggle('View', 1, off='THIRD PERSON', on='FIRST PERSON')])
c('irr.Player.DealDamage', P, 'Deal damage to yourself', 'Deal damage to the local player.',
  [num('Amount', 'float', 0, 200, 50, 1), toggle('Wound', 0, 'Also open a wound.', off='NO', on='YES')])
c('irr.Player.ForceDanger', P, 'Force danger state',
  "Overrides every local server-side player's danger state.",
  [choice('State', [('1', 'IN DANGER'), ('0', 'SAFE'), ('-1', 'NORMAL')], '1')])
c('irr.Player.CinematicCamera', P, 'Cinematic camera (pawn)', 'Toggle cinematic camera (re-possess pawn when leaving).')
c('irr.Menu.CharacterDebug', P, 'Character debug menu',
  'Toggle the character debug menu (deal damage, apply/clear status effects on the local player).')

# ---------------------------------------------------------------- Items & Progression
I = 'Items & Progression'
c('irr.Item.Spawn', I, 'Spawn item', 'Spawn item pickup(s) in front of you. The name can be part of the item name; if several items match, the game lists them in the console.',
  [item(), num('Count', 'int', 1, 100, 1, 1)], host=True)
c('irr.Item.Give', I, 'Give item', 'Add an item to stash/inventory by name/tag. The name can be part of the item name; if several items match, the game lists them in the console.',
  [item(), num('Count', 'int', 1, 100, 1, 1)])
c('irr.Item.Categories', I, 'List item categories', 'Log spawnable item categories (in the console).')
c('irr.Item.List', I, 'List items', 'Log items in a category (paged), with the names to use in Spawn / Give.',
  [text('Category', 'All'), num('Page', 'int', 1, 50, 1, 1)])
c('irr.Item.AddResource', I, 'Add resource', 'Add a resource.',
  [choice('Resource', ['Concrete', 'Food', 'Metal', 'Oil'], 'Food', editable=True), num('Amount', 'int', 1, 10000, 100, 10)])
c('irr.Item.SetDurability', I, 'Set durability', 'Set durability of all weapons & armor.',
  [num('Percent', 'float', 0, 100, 100, 5, '%')])
c('irr.Item.ResetInventory', I, 'Reset inventory', 'Reset inventory to default.', warn='Your inventory goes back to the default one.')
c('irr.Item.ResetResources', I, 'Reset resources', 'Reset resources.', warn='Your resources are reset.')
c('irr.Item.ResetVendor', I, 'Reset vendor', 'Reset vendor stock.')
c('irr.Item.Add', I, 'Add item (legacy)', 'Legacy vendor-table AddItemByRowName. Prefer Give item.', [text('Row name', 'Bandage')])
c('irr.Menu.SpawnMenu', I, 'Spawn menu', 'Toggle the debug item spawn/give menu.')
c('irr.SpawnMenu.AllowClients', I, 'Spawn menu for clients', 'Server: accept debug spawn menu spawn/give requests from remote clients.', [ONOFF()])
c('irr.World.SetLevel', I, 'Set player level', 'Set player level and XP.',
  [num('Level', 'int', 1, 100, 10, 1), num('XP', 'float', 0, 1000000, 0, 100)])
c('irr.World.SetHideoutUnitLevel', I, 'Set hideout unit level', "Set a hideout unit's level.",
  [choice('Unit', ['Generator', 'Gunsmith', 'Intel Center', 'Lighting', 'Repair Station', 'Shooting Range', 'Stash',
                   'Structure', 'Terminal'], 'Stash', editable=True), num('Level', 'int', 0, 10, 1, 1)])
c('irr.World.SetSuccessRate', I, 'Set success rate', 'Set success rate of the last 8 missions.',
  [num('Rate', 'float', 0, 100, 100, 5, '%')])
c('irr.World.SetOverallFactionReputation', I, 'Set overall reputation', 'Set overall faction reputation.',
  [num('Reputation', 'float', -100, 100, 50, 1)])
c('irr.World.SetFactionReputation', I, 'Set faction reputation', 'Set reputation for one faction.',
  [num('Reputation', 'float', -100, 100, 50, 1),
   choice('Faction', ['BLUEFOR', 'OPFOR', 'GREYFOR', 'FANG', 'IGC', 'UICS', 'VLF'], 'OPFOR', editable=True)])

# ---------------------------------------------------------------- Missions & Operations
M = 'Missions & Operations'
c('irr.Mission.Start', M, 'Start mission', 'Start a debug mission by name (Project Settings > Mission System > DebugMissions).',
  [text('Mission name')])
c('irr.Mission.CompleteTracked', M, 'Complete tracked missions', 'Complete currently tracked missions.')
c('irr.Mission.FailTracked', M, 'Fail tracked missions', 'Fail currently tracked missions.')
c('irr.Mission.ResetTracked', M, 'Reset tracked missions', 'Reset currently tracked missions.')
c('irr.Mission.ResetSystem', M, 'Reset mission system', 'Reset mission system (optional progress wipe).',
  [toggle('Reset progress', 1, off='KEEP', on='WIPE')], warn='Can wipe your mission progress.')
c('irr.Mission.Regenerate', M, 'Regenerate missions', 'Regenerate missions.')
c('irr.Operations.Start', M, 'Start operation',
  "Start one start point of the level manager's entry with this id now, skipping the roll, the gate and the pacing rules. No arguments lists the ids.",
  [text('Operation id', optional=True), text('Start point', optional=True)])
c('irr.Operations.StartSelected', M, 'Start armed operations',
  'Start every automatic operation the level manager armed for this raid, now.')
c('irr.Operations.StartAll', M, 'Start all operations',
  'Start every placed operation start point that has not already fired, including points the raid never armed. Can put several operations on the same route.')
c('irr.AI.Spawn.TestReinforcements', M, 'Call reinforcements',
  'Request director reinforcements around the first player.',
  [choice('Allegiance', ['IGC', 'UICS', 'VLF', 'FANG'], 'IGC', editable=True, optional=True)])

# ---------------------------------------------------------------- AI
A = 'AI'
c('irr.AI.Enable', A, 'Enable AI', 'Enable/disable AI.', [ONOFF()])
c('irr.AI.KillAll', A, 'Kill all AI', 'Kill every AI pawn.')
c('irr.AI.HideAll', A, 'Hide all AI', 'Show/hide all AI characters.', [toggle('Hidden', 1, off='SHOW', on='HIDE')])
c('irr.AI.ShowAll', A, 'Show AI count', 'Print AI count and debug locations.')
c('irr.AI.Optimization', A, 'AI optimization', 'Enable/disable AI LOD optimization.', [ONOFF()])
c('irr.AI.OptimizationLevel', A, 'Force AI optimization level', 'Force AI optimization level.', [num('Level', 'int', 0, 4, 1, 1)])
c('irr.AI.OptimizationMinMax', A, 'AI optimization min/max', 'Clamp AI optimization min/max levels.',
  [num('Min', 'int', 0, 4, 0, 1), num('Max', 'int', 0, 4, 4, 1)])
cv('irr.AI.Headlamps', A, 'Bot headlamps', 'Bot headlamps. Applies on the next visibility tick.',
   choice('Mode', [('0', 'OFF'), ('1', 'TIME OF DAY'), ('2', 'ALWAYS ON')], '1'))
cv('irr.AI.TreeCrowns', A, 'Tree crown concealment', 'Tree crown concealment for AI sight. 0 = crowns ignored.', ONOFF())
cv('irr.AI.SpawnDirector', A, 'Spawn director', 'Native AI spawn director. 0 = off (legacy spawner only), 1 = on (director spawns too, in parallel).', ONOFF())
c('irr.Menu.AISettings', A, 'AI settings menu', 'Toggle the AI settings menu (preset selection + value editor).')
c('irr.Menu.AIDebug', A, 'AI debug menu', 'Toggle the AI debug overlay menu (per-category show/detail + global overlay settings).')
cv('irr.AIDebug.Overlay', A, 'AI state overlay', 'AI state overlay, works in packaged builds.',
   choice('Mode', [('0', 'OFF'), ('1', 'LABELS'), ('2', '+ PERCEPTION')], '1'))
c('irr.AI.Allegiances.List', A, 'List allegiances', 'Dump registered allegiances.')
c('irr.AI.BotTypes.List', A, 'List bot types', 'Dump registered bot types.')
c('irr.AI.Zones.List', A, 'List zones', 'Dump zone documents for the level.')
c('irr.AI.Zones.Reload', A, 'Reload zones', 'Reload the AI zones of the level.')

# ---------------------------------------------------------------- Cameras & Menus
C = 'Cameras & Menus'
c('irr.Menu.Freecam', C, 'Free camera', 'Toggle the engine debug free camera (flying spectator; works in the released game).')
c('irr.CineCam', C, 'Cinematic camera', 'Toggle the cinematic trailer camera (Tab in the camera opens every setting, H shows the keys).')
c('irr.CineCam.Menu', C, 'Cinematic camera settings', 'Open/close the cinematic camera settings menu.')
c('irr.CineCam.AddKey', C, 'Add camera keyframe', 'Add a path keyframe at the current camera pose.')
c('irr.CineCam.Play', C, 'Play camera path', 'Play/stop the keyframed camera path.')
c('irr.CineCam.ClearKeys', C, 'Clear camera keyframes', 'Delete every path keyframe.')
c('irr.CineCam.Pause', C, 'Pause world', 'Pause/resume the world while the camera keeps flying.', host=True)
c('irr.CineCam.TimeDilation', C, 'World speed (camera)', 'World speed while the camera runs (1 = normal; restored on exit).',
  [num('Speed', 'float', 0.05, 4, 0.5, 0.05, 'x')])
c('irr.CineCam.ResetSettings', C, 'Reset camera settings', 'Reset every cinematic camera setting to its default.')
c('irr.Spectator.Helmet', C, 'Bot eye view', 'PROTOTYPE spectator. Watch a bot from its own eye. No argument = closest, then next farther.',
  [choice('Target', [('next', 'NEXT'), ('prev', 'PREVIOUS'), ('off', 'OFF')], 'next', editable=True, optional=True)])
c('irr.Spectator.List', C, 'List spectate targets', 'PROTOTYPE spectator. List spectate targets closest-first.')
c('irr.Spectator.Engaged', C, 'Jump to engaged bot', 'PROTOTYPE spectator. Jump to the bot most recently in contact.')
c('irr.Spectator.Mode', C, 'Spectator framing', 'PROTOTYPE spectator. Framing for the active spectate.',
  [choice('Mode', [('helmet', 'HELMET'), ('sensor', 'SENSOR'), ('chase', 'CHASE')], 'helmet')])
c('irr.Menu.Spectator', C, 'Spectator settings', 'PROTOTYPE spectator. Toggle the spectator settings overlay.')
c('irr.Menu.Watermark', C, 'Watermark', 'Show/hide the watermark overlay.', [toggle('Visible', 0, off='HIDE', on='SHOW')])
c('irr.Menu.CommandMenu', C, 'Command menu', 'Toggle the debug console command menu (most commands, with a search field).')
c('irr.Menu.LogMenu', C, 'Log menu', 'Toggle the debug log category menu (Verbose on/off).')
c('irr.Menu.VoiceChat', C, 'Voice chat menu', 'Toggle the voice chat debug menu (devices, push-to-talk, processing, mic level).')
c('irr.ui.ThemeEditor', C, 'Theme editor', 'Toggle the Theme Editor (menu styling: palette, typography, row layout).')

# ---------------------------------------------------------------- Graphics & Audio
G = 'Graphics & Audio'
c('irr.Graphics.LumenLow', G, 'Lumen low', 'Set Lumen scalability low/normal.', [toggle('Quality', 1, off='NORMAL', on='LOW')])
c('irr.Graphics.MeshPack', G, 'MeshPack actors', 'Toggle actors tagged MeshPackActor. No argument toggles.',
  [choice('Mode', [('hide', 'HIDE'), ('show', 'SHOW')], 'hide', optional=True)])
cv('irr.TextureQuality.VRAMCap', G, 'Texture VRAM cap',
   'Texture quality capped by dedicated video memory (<=4.5 GB Low, <=8.5 GB Medium, <=12.5 GB High). 0: no cap.', ONOFF())
c('irr.FrameGen.Status', G, 'Frame generation status', 'Logs frame generation support, the session swap chain and the vendor cvars.')
cv('irr.UDS.SkyTickInterval', G, 'Sky tick interval', "Tick interval (s) for the sky actor. < 0 keeps the Blueprint's own interval.",
   num('Seconds', 'float', -1, 5, 0.1, 0.05, 's'))
cv('irr.UDS.WeatherTickInterval', G, 'Weather tick interval', "Tick interval (s) for the weather actor. < 0 keeps the Blueprint's own interval.",
   num('Seconds', 'float', -1, 5, 0.1, 0.05, 's'))
cv('irr.FX.CosmeticBullets', G, 'Cosmetic bullets', 'Client master switch for the cosmetic bullet system (tracers/whiz cosmetics).', ONOFF())
cv('irr.FX.ImpactFX.CullDistance', G, 'Impact FX distance', 'Bullet impact FX farther than this from every camera are skipped. <= 0 disables.',
   num('Distance', 'float', 0, 50000, 10000, 500, 'cm'))
cv('irr.FX.ImpactFX.MaxPerWeaponPerFrame', G, 'Impact FX per frame', 'Max bullet impact FX one weapon plays per frame. <= 0 = unlimited.',
   num('Count', 'int', 0, 32, 4, 1))
c('irr.FX.BulletDebugTraces', G, 'Bullet debug traces', 'Toggle bullet debug traces.', [ONOFF()])
c('irr.Audio.TPSoundsOnly', G, 'Third-person sounds only', 'Only play third-person weapon sounds.', [ONOFF()])
c('irr.Audio.LocalPlayerWeaponSounds', G, 'Own weapon sounds', 'Toggle local player weapon sounds.', [ONOFF()])
c('irr.Audio.ResetEngine', G, 'Reset sound engine', 'Reset the sound engine (if the sound stops working).')
c('irr.Audio.UnregisterMainDevice', G, 'Unregister audio device', 'Unregister the main audio device.')
c('irr.Audio.RegisterMainDevice', G, 'Register audio device', 'Register the main audio device.')
cv('irr.Perf.TickHygiene', G, 'Tick hygiene', 'Turn off ticks that do no work. 0 restores them.', ONOFF())
c('irr.Perf.TickHygiene.Report', G, 'Tick hygiene report', 'Print what the tick hygiene subsystem has turned off in this world.')
cv('irr.Outfit.MergesPerFrame', G, 'Outfit merges per frame', "Outfit merges built per frame; each is a few ms of game-thread work.",
   num('Count', 'int', 1, 16, 2, 1))

# ---------------------------------------------------------------- Debug
D = 'Debug'
LVL3 = lambda labels: choice('Mode', [(str(i), l) for i, l in enumerate(labels)], '1')
cv('irr.AIDebug.CoverCache', D, 'Cover cache overlay', 'Faction cover cache overlay, colored by protection.', LVL3(['OFF', 'MARKERS', '+ DETAIL']))
cv('irr.AIDebug.SearchGrid', D, 'Search grid overlay', 'Shared search-grid overlay (green = searched, red = open).', ONOFF())
cv('irr.AIDebug.TreeCrowns', D, 'Tree crowns overlay', 'Tree crown concealment volumes.', LVL3(['OFF', 'CROWNS', '+ DENSITY']))
cv('irr.AIDebug.DoorLinks', D, 'Door links overlay', 'Smart nav links (doors) vs the navmesh.', LVL3(['OFF', 'LINKS', '+ DETAIL']))
cv('irr.AIDebug.Overlay.Range', D, 'AI overlay range', 'AI overlay cull distance from the view origin.', num('Range', 'int', 500, 30000, 6000, 500, 'cm'))
cv('irr.AIDebug.Overlay.MaxBots', D, 'AI overlay max bots', 'Max labeled bots per frame, nearest first.', num('Bots', 'int', 1, 100, 20, 1))
cv('irr.AIDebug.SpawnRally', D, 'Spawn rally gizmo', 'Draw reinforcement rally solves.', ONOFF())
cv('irr.AIDebug.SpawnValidation', D, 'Spawn validation gizmo', 'Draw held spawn-validation candidates (colour = reject reason).', ONOFF())
c('irr.AI.Headlamps.Dump', D, 'Dump headlamps', 'Dump the headlamp hardware state of the first few registered bots.')
c('irr.AI.LightSense.Dump', D, 'Dump light sources', "Logs every light source, each bot's sighted beams and the players' visible lasers.")
c('irr.AI.TreeCrowns.Stats', D, 'Tree crown stats', 'Logs the tree crown index of the current world.')
c('irr.AI.Nav.CheckDoorLinks', D, 'Check door links', 'Checks every door nav link against the navmesh.', [text('Owner filter', optional=True)])
c('irr.AI.Spawn.DumpChassis', D, 'Dump AI bodies', 'Dump chassis/clothing render state for every AI character (invisible-body hunt).')
cv('irr.AI.AvoidanceMode', D, 'Bot avoidance mode', 'Global avoidance mode override for all bots, for A/B testing.',
   choice('Mode', [('-1', 'PER BOT'), ('0', 'NONE'), ('1', 'DETOUR CROWD')], '-1'))
cv('irr.AI.TurnInertia', D, 'Bot turn inertia', 'Global override for the AI turn-inertia yaw spring.',
   choice('Mode', [('-1', 'PER BOT'), ('0', 'OFF'), ('1', 'ON')], '-1'))
cv('irr.AI.AtAngleRotation', D, 'Bot at-angle rotation', 'Global override for the AI at-angle body rotation mode.',
   choice('Mode', [('-1', 'PER BOT'), ('0', 'FOLLOW DESIRED'), ('1', 'TURN AT ANGLE')], '-1'))
cv('irr.AI.OffMeshRecovery', D, 'Off-navmesh recovery', 'Push bots back toward the last valid nav location.', ONOFF())
cv('irr.AI.SimpleAim', D, 'Bot aim mode', 'Bone-free bot aiming.', choice('Mode', [('-1', 'AUTO'), ('0', 'PRECISE'), ('1', 'SIMPLE')], '-1'))
cv('irr.AI.AimCommitLog', D, 'Aim commit log', 'Log the aim commitment lifecycle per bot.', ONOFF())
cv('irr.AI.SharedNoiseEstimate', D, 'Shared noise estimate', 'All AI listeners of one noise share a single location estimate.', ONOFF())
cv('irr.AI.PerceptionSharing.SkipIdleTeams', D, 'Skip idle teams', 'Skip cluster building for teams with no contact reports.', ONOFF())
cv('irr.AI.NewTargetPaths.Debug', D, 'Target paths debug', 'Path manager debug.', choice('Mode', [('0', 'OFF'), ('1', 'STATS'), ('2', '+ DRAW'), ('3', '+ FAILURES')], '1'))
cv('irr.AI.VisCache.Debug', D, 'Visibility cache debug', 'Draw traced directions: green = clear, red = blocked.', ONOFF())
cv('irr.Avoidance.Debug', D, 'Avoidance claims', 'Draw all avoidance claims, color-coded by type.', ONOFF())
cv('irr.Nav.DebugRooms', D, 'Navmesh rooms', 'Room map debug drawing.', LVL3(['OFF', 'ROOMS', '+ OPEN SPACE']))
c('irr.Nav.BuildRooms', D, 'Build navmesh rooms', 'Forces a (re)build of the navmesh room map.')
cv('irr.Vehicle.Debug', D, 'Vehicle debug', 'Every vehicle debug draw at once.', choice('Mode', [('-1', 'OFF'), ('0', 'PER COMPONENT'), ('1', 'ON')], '1'))
cv('irr.Traversal.Debug', D, 'Traversal debug', 'Traversal obstacle scan debug.', LVL3(['OFF', 'LOG', '+ DRAW']))
cv('irr.FireSupport.Debug', D, 'Fire support debug', 'Show the designation state machine.', choice('Mode', [('0', 'OFF'), ('1', 'DRAW'), ('2', 'DRAW + LOG')], '1'))
cv('irr.FX.CosmeticBullets.Debug', D, 'Cosmetic bullets debug', 'Cosmetic bullet debug drawing.', LVL3(['OFF', 'LINES', '+ LOG']))
cv('irr.Laser.DotDebug', D, 'Laser dot debug', 'On-screen readout per laser dot.', ONOFF())
cv('irr.Inventory.SwapLog', D, 'Item swap log', 'Log every item-swap decision with the reason it was declined.', ONOFF())
cv('irr.Audio.DebugRooms', D, 'Audio rooms overlay', 'Wwise room/portal/ambient overlay.', LVL3(['OFF', 'WIREFRAMES', '+ LINKS']))
cv('irr.Audio.AcousticDebug', D, 'Acoustic debug', 'Draws the Wwise room and indoor/outdoor state above every pawn.', ONOFF())
cv('irr.Audio.DebugVoice', D, 'Voice input overlay', 'Voice chat input overlay (mic level / PTT / talking players).', ONOFF())
cv('irr.Perf.ObjectTickTiming', D, 'Object tick timing', 'Time every IRR object tick by class. Read with Dump object ticks.', ONOFF())
c('irr.Perf.DumpObjectTicks', D, 'Dump object ticks', 'Print per-class timings gathered by Object tick timing, sorted by cost.',
  [choice('Action', [('reset', 'RESET')], 'reset', optional=True)])
c('irr.Showcase.Profile', D, 'Showcase profile', 'Separate showcase loadout/stash/progress; real saves untouched. Reloads the level, not in a raid.', [ONOFF()])
c('irr.Showcase.Reset', D, 'Reset showcase', 'Restore the showcase saves from the template. Reloads the level.')
c('irr.Showcase.CaptureTemplate', D, 'Capture showcase template', 'Save the current showcase state as what Reset restores. Use it in the hideout.')
c('irr.Showcase.Status', D, 'Showcase status', 'Log showcase profile + raid timer state.')
c('irr.Spectator.Debug', D, 'Spectator debug', 'PROTOTYPE spectator. Dump world/controller/roster resolution.')
c('irr.Debug.Operations', D, 'Debug operations flag', 'Enable debug operations flag.', [ONOFF()])
c('irr.Debug.EnableGCLogging', D, 'Enable GC logging', 'Enable GC logger.')
c('irr.Debug.DisableGCLogging', D, 'Disable GC logging', 'Disable GC logger.')
c('irr.Debug.ResetAchievements', D, 'Reset achievements', 'Reset achievements.', warn='Resets your achievements.')


# ---------------------------------------------------------------- items
def load_items(path):
    items = []
    if not path or not os.path.exists(path): return items
    seen = set()
    for line in open(path, encoding='utf-8-sig'):
        line = line.strip().replace('\\', '/')
        if '/Items/' not in line or '/ID_' not in line: continue
        rel = line.split('/Items/', 1)[1]
        parts = rel.split('/')
        name = parts[-1][3:].rsplit('.', 1)[0]
        cat = parts[0]
        if cat in ('Attachments', 'Gear') and len(parts) > 3:
            cat = cat + ' / ' + parts[1]
        if name in seen: continue
        seen.add(name)
        items.append([name, cat])
    items.sort(key=lambda x: (x[1].lower(), x[0].lower()))
    return items


def main():
    items_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'item_paths.txt')
    names = [x['cmd'] for x in CMDS]
    assert len(names) == len(set(names)), 'duplicate command'
    for e in ESSENTIALS: assert e in names, e
    for x in CMDS: assert x['cat'] in CATEGORIES, x['cat']
    assert not set(n.lower() for n in names) & set(n.lower() for n in OMITTED), 'command both listed and omitted'
    maps = load_maps()
    doc = {
        'weatherIndexBase': WEATHER_INDEX_BASE,
        'maps': [{'name': m, 'weathers': [{'name': w, 'weight': p} for w, p in ws]} for m, ws in maps],
        'categories': CATEGORIES,
        'essentials': ESSENTIALS,
        'commands': CMDS,
        'items': load_items(items_path),
        'omitted': OMITTED,
    }
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
    print('%d commands, %d essentials, %d items -> %s' % (len(CMDS), len(ESSENTIALS), len(doc['items']), os.path.normpath(OUT)))


if __name__ == '__main__':
    main()
