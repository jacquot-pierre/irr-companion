r"""Builds src/Resources/defaults.json from the files extracted from the game pak.

Usage: python -I tools/build_defaults.py <extracted AISettings dir> <reference full preset doc> [game build label]
  - extracted dir   : ...\IRR_AISettings\Test_C\Content\AISettings
  - reference doc   : a full AISettings doc saved by the game (gives key order and
                      the engine defaults for keys that Normal.json does not set)
  - game build label: shown in the tool's header, e.g. "2026-11-06" (default: keep the current one)

Prints what changed compared with the previous defaults.json (parameters, ranges, difficulty values).
"""
import json
import os
import sys

src, ref_path = sys.argv[1], sys.argv[2]
out = os.path.join(os.path.dirname(__file__), "..", "src", "Resources", "defaults.json")
previous = None
if os.path.exists(out):
    with open(out, encoding="utf-8") as f:
        previous = json.load(f)
game_build = sys.argv[3] if len(sys.argv) > 3 else (previous or {}).get("gameBuild", "unknown")


def load(*p):
    with open(os.path.join(src, *p), encoding="utf-8-sig") as f:
        return json.load(f)


with open(ref_path, encoding="utf-8-sig") as f:
    ref = json.load(f)

schema = load("Schema", "AISettings.json")
sim_schema = load("Schema", "Simulation.json")
loc = load("Loc", "en", "AISettings.json")
sim_loc = load("Loc", "en", "Simulation.json")
normal = load("AISettings", "Normal.json")


def lc(k):
    return k[0].lower() + k[1:]


# Full base document in the game's own key order.
base = {}
for k, v in ref.items():
    base[k] = normal[k] if k in normal else v
for k, v in normal.items():
    if k not in base:
        base[k] = v

ai = {}
for name in schema["__VariantOrder"]:
    over = load("AISettings", f"{name}.json")
    d = json.loads(json.dumps(base))
    for k, v in over.items():
        d[k] = v
    ai[name] = d

sim = {}
for name in sim_schema["__VariantOrder"]:
    sim[name] = load("Simulation", f"{name}.json")

# Names exactly as displayed by the in-game AI settings menu (screenshots, 2026-11-07).
NAMES = {
    "AIMultiplier": "AI Multiplier",
    "Aggressiveness": "Aggression",
    "AIMemoryLength": "AI Memory Length",
    "Coordination_PerceptionShareRadius": "Perception Share Radius",
    "bAISuppressTargets": "Suppress Targets",
    "bCanThrowGrenades": "Throw Grenades",
    "GrenadeInaccuracy": "Grenade Inaccuracy",
    "DamageMap.Boss": "Boss Damage Multiplier",
    "DamageMap.AI": "Standard AI Damage Multiplier",
    "DamageMap.Player": "Player Damage Multiplier",
    "OnTargetAimThreshold": "Target Aim Threshold",
    "AimInitialDelayMultiplier": "Aim Initial Delay Multiplier",
    "AimRecoveryDelayMultiplier": "Aim Recovery Delay Multiplier",
    "DeliberateMissTime": "Deliberate Miss Time",
    "DeliberateMissChance": "Deliberate Miss Chance",
    "FullAutoDistanceThreshold": "Full Auto Max Distance",
    "FullAutoBurstLengthMultiplier": "Full Auto Burst Length Multiplier",
    "bCanAimForHead": "Can Aim For Head",
    "MuzzleFlashScale": "AI Muzzle Flash Size",
    "PerceptionBaseDelay": "Base Sense Delay",
    "PerceptionInitialContactAdditionalDelay": "First Contact Additional Delay",
    "PerceptionBaseTimeToSee": "Time To See",
    "PerceptionRange": "Perception Range",
    "PerceptionAngle": "Perception Angle",
    "PerceptionMinScore": "Perception Min Score",
    "RepeatSenseMaxCoef": "Repeat Sense Coef",
    "bFlashlightDetectionToggle": "Light Detection",
    "bLaserDetectionToggle": "Laser Detection",
    "PerceptionDistanceFalloff": "Distance Sight Falloff",
    "MaxSearchDistance": "Max Search Distance",
    "MaxSearchersPerTarget": "Max Searchers Per Enemy",
    "MaxSearchersAtOnce": "Max Searchers At Once",
    "MaxSearchAreaSize": "Max Search Area Size",
    "FiredUponSearchTime": "Fired Upon Search Time",
    "SearchCasualtyHoldTime": "Casualty Hold Time",
}

# Sections and order of the in-game menu. EquipmentLevel is not shown by the game.
SECTIONS = [
    ("General", ["AIMultiplier"]),
    ("Behavior", ["Aggressiveness", "AIMemoryLength", "Coordination_PerceptionShareRadius",
                  "bAISuppressTargets", "bCanThrowGrenades", "GrenadeInaccuracy"]),
    ("Damage", ["DamageMap.Boss", "DamageMap.AI", "DamageMap.Player"]),
    ("Aim and Shoot", ["OnTargetAimThreshold", "AimInitialDelayMultiplier", "AimRecoveryDelayMultiplier",
                       "DeliberateMissTime", "DeliberateMissChance", "FullAutoDistanceThreshold",
                       "FullAutoBurstLengthMultiplier", "bCanAimForHead", "MuzzleFlashScale"]),
    ("Perception and Senses", ["PerceptionBaseDelay", "PerceptionInitialContactAdditionalDelay",
                               "PerceptionBaseTimeToSee"]),
    ("Vision", ["PerceptionRange", "PerceptionAngle", "PerceptionMinScore", "RepeatSenseMaxCoef",
                "bFlashlightDetectionToggle", "bLaserDetectionToggle", "PerceptionDistanceFalloff"]),
    ("Search", ["MaxSearchDistance", "MaxSearchersPerTarget", "MaxSearchersAtOnce",
                "MaxSearchAreaSize", "FiredUponSearchTime", "SearchCasualtyHoldTime"]),
]

# Settings of the AI document that the game menu does not show (ADVANCED section of the editor, hidden by
# default). Grenade settings have a name / description in the game files; for the others the name and the
# description are written by us from the key and the difficulty values (flagged "unofficial" in the tool).
# (key, name, description, uiMin, uiMax, precision); None = take it from the game schema / loc.
ADVANCED = [
    ("Perception", [
        ("PerceptionMinVisibleBoneScore", "Min Visible Body Part",
         "How much of your body must be visible before a bot can start to see you. Higher values let you hide "
         "behind more partial cover. Lower on harder difficulties (0.25 Recruit → 0.05 Elite).", 0, 0.5, 2),
        ("PerceptionLookTowardsCoef", "Look Towards Coefficient",
         "Weight of where the bot is looking in how fast it notices you. Lower on harder difficulties "
         "(0.66 Recruit → 0.33 Elite).", 0, 1, 2),
        ("PerceptionGainDecayCoef", "Perception Decay",
         "How fast the awareness a bot is building up fades when it loses sight of you.", 0, 1, 2),
        ("PeripheralVisionDistance", "Peripheral Vision Distance",
         "Range (cm) of the wide peripheral vision cone.", 0, 5000, 0),
        ("PeripheralVisionAngle", "Peripheral Vision Angle",
         "Angle (degrees) of the peripheral vision cone.", 0, 180, 0),
        ("WatchWarmthCap", "Watch Alertness Cap",
         "Maximum alertness a bot builds up while watching an area. Higher on harder difficulties "
         "(0.5 Recruit → 1 Elite).", 0, 1, 2),
        ("PerceptionUnderFireDelay", "Under Fire Sense Delay",
         "Reaction delay (s) of a bot that is being shot at.", 0, 1, 2),
        ("UnderFireGlanceDuration", "Under Fire Glance Duration",
         "How long (s) a bot looks towards where the shots come from.", 0, 6, 1),
        ("RepeatSenseDistanceScaleEnd", "Repeat Sense Distance",
         "Distance (cm) over which Repeat Sense Coef applies: seeing or hearing you again speeds up detection.", 0, 3000, 0),
        ("MaxFootstepSoundRange", "Max Footstep Sound Range",
         "Maximum distance (cm) at which bots hear footsteps. 0 in every difficulty (game default).", 0, 5000, 0),
        ("Coordination_PerceptionShareMaxDistance", "Perception Share Max Distance",
         "Maximum distance (cm) of a target that bots pass on to the allies around them.", 0, 10000, 0),
    ]),
    ("Light and Laser", [
        ("LightReportCooldown", "Light Report Cooldown",
         "Minimum time (s) between two reports of your flashlight by a bot.", 0, 5, 1),
        ("LaserReportCooldown", "Laser Report Cooldown",
         "Minimum time (s) between two reports of your laser by a bot.", 0, 5, 1),
        ("LaserDotSightRange", "Laser Dot Sight Range",
         "Distance (cm) at which bots notice a laser dot.", 0, 10000, 0),
        ("LaserDotSightHalfAngle", "Laser Dot Sight Angle",
         "Half angle (degrees) of the cone in which bots notice a laser dot.", 0, 180, 0),
        ("LasedBacktrackDistance", "Laser Backtrack Distance",
         "How far (cm) back along a laser beam bots look for the one holding it.", 0, 5000, 0),
        ("DesignationCompromiseRadius", "Designation Compromise Radius",
         "Radius (cm) around a lased position in which the bots consider the shooter's area compromised.", 0, 10000, 0),
    ]),
    ("Aim and Shoot", [
        ("EquipmentLevel", "Equipment Level",
         "Quality of the bots' equipment. Listed in the game files but not shown in the game menu; 0.75 in every "
         "difficulty.", None, None, None),
        ("TriggerSignalDelay", "Trigger Delay",
         "Delay (s) before a bot that has you in its sights pulls the trigger. Lower on harder difficulties "
         "(0.4 Recruit → 0.15 Elite).", 0, 1, 3),
        ("DeliberateMissChanceTime", "Deliberate Miss Window",
         "How long (s) after first contact Deliberate Miss Chance applies. Shorter on harder difficulties "
         "(6 Recruit → 4 Elite).", 0, 15, 1),
        ("DeliberateMissPersistentChance", "Persistent Miss Chance",
         "Chance that a bot misses on purpose after that window. Lower on harder difficulties "
         "(0.15 Recruit → 0.06 Elite).", 0, 0.5, 2),
        ("bDisableDeliberateMissTimerVsAI", "No Deliberate Miss vs AI",
         "Bots do not miss on purpose when they shoot at other bots.", None, None, None),
        ("AimTimeBetweenShotsMin", "Time Between Shots Min",
         "Shortest pause (s) between two aimed shots.", 0, 1, 2),
        ("AimTimeBetweenShotsMax", "Time Between Shots Max",
         "Longest pause (s) between two aimed shots.", 0, 10, 1),
        ("FriendlyFireRadius", "Friendly Fire Radius",
         "A bot holds fire when an ally is closer than this (cm) to its line of fire.", 0, 200, 0),
        ("FriendlyFireOnsetDelay", "Friendly Fire Onset Delay",
         "Delay (s) before a bot reacts to an ally in its line of fire.", 0, 2, 2),
    ]),
    ("Suppression", [
        ("Suppress_Point_Max_Distance", "Suppress Max Distance",
         "Bots do not lay suppressive fire on a point further than this (cm).", 0, 10000, 0),
        ("Suppress_Point_Always_Distance", "Suppress Always Distance",
         "Bots always suppress a point closer than this (cm).", 0, 1000, 0),
        ("Suppress_Point_Max_PathLength", "Suppress Max Path Length",
         "Same as Suppress Max Distance, measured along the walking path (cm).", 0, 10000, 0),
        ("Suppress_Point_Always_PathLength", "Suppress Always Path Length",
         "Same as Suppress Always Distance, measured along the walking path (cm).", 0, 1000, 0),
    ]),
    ("Grenades", [(k, None, None, None, None, None) for k in (
        "GrenadeMinThrowDistance", "GrenadeMaxThrowDistance", "bGrenadeRequireClearArc", "GrenadeMaxLandingError",
        "GrenadeMinAimTime", "GrenadeMaxReleaseYawError", "GrenadeMaxTurnTime", "GrenadeReleaseCheckInterval")]),
]

DAMAGE_DESC = {
    "DamageMap.Boss": "Damage multiplier applied to boss hits.",
    "DamageMap.AI": "Damage multiplier applied when bots hit other bots.",
    "DamageMap.Player": "Damage multiplier applied when bots hit players.",
}

# In-game names of the difficulty variants.
AI_LABELS = {"Easy": "Recruit", "Normal": "Regular", "Hard": "Veteran", "Extreme": "Elite"}

menu = [k for k, v in schema.items() if not k.startswith("__") and isinstance(v, dict) and v.get("ShowInSettingsMenu")]
listed = [k for _, ks in SECTIONS for k in ks]
missing = set(menu) - set(listed) - {"EquipmentLevel"}
assert not missing, f"new menu params, add them to NAMES and SECTIONS: {missing}"
gone = [k for k in listed if k.split(".")[0] not in schema]
assert not gone, f"params of SECTIONS no longer in the game schema, remove them: {gone}"
hidden = [k for k in listed if k.split(".")[0] in schema and k.split(".")[0] not in menu and not k.startswith("DamageMap.")]  # DamageMap is shown although not flagged
if hidden:
    print("WARNING: in SECTIONS but no longer shown in the game menu (ShowInSettingsMenu):", hidden)


def param(key, s, l, section, cat):
    p = {"key": key, "docKey": lc(key), "cat": cat, "section": section,
         "name": NAMES.get(key) or l.get("DisplayName") or key,
         "desc": l.get("Description", ""), "warn": l.get("Warning", ""),
         "type": "bool" if key.startswith("b") and key[1:2].isupper() else "number",
         "precision": s.get("Precision", 2)}
    for a, b in (("Min", "min"), ("Max", "max"), ("UIMin", "uiMin"), ("UIMax", "uiMax"), ("Step", "step")):
        if a in s:
            p[b] = s[a]
    return p


params = []
for section, keys in SECTIONS:
    for k in keys:
        if k.startswith("DamageMap."):
            sub = {"Boss": "boss", "AI": "aI", "Player": "player"}[k.split(".")[1]]
            p = param("DamageMap", schema["DamageMap"], {}, section, "AISettings")
            p.update(key=k, docKey="damageMap." + sub, name=NAMES[k], desc=DAMAGE_DESC[k])
        else:
            p = param(k, schema[k], loc.get(k, {}), section, "AISettings")
        params.append(p)

# ADVANCED: every other setting of the AI document (game key = doc key with an upper-case first letter,
# except the bXxx booleans).
doc_keys = {k if k in schema else k[0].upper() + k[1:] for k in base}
adv_listed = [k[0] for _, ks in ADVANCED for k in ks]
gone = [k for k in adv_listed if k not in schema or k not in doc_keys]
assert not gone, f"params of ADVANCED no longer in the game schema / document, remove them: {gone}"
new_hidden = sorted(doc_keys - {k.split(".")[0] for k in listed} - set(adv_listed)
                    - {"BehaviorConfig", "SchemaSoftVersion", "SchemaHardVersion"})
if new_hidden:
    print("WARNING: settings of the AI document shown nowhere in the tool, add them to ADVANCED:", new_hidden)
for section, keys in ADVANCED:
    for k, name, desc, lo, hi, prec in keys:
        l = loc.get(k, {})
        p = param(k, schema[k], l, "Advanced · " + section, "AISettings")
        p["advanced"] = True
        if name:
            p["name"] = name
        if desc:
            p["desc"] = desc
        if not l.get("Description"):
            p["unofficial"] = True
        if lo is not None and "UIMin" not in schema[k] and "Min" not in schema[k]:
            p["uiMin"] = lo
        if hi is not None and "UIMax" not in schema[k] and "Max" not in schema[k]:
            p["uiMax"] = hi
        if prec is not None and "Precision" not in schema[k]:
            p["precision"] = prec
        params.append(p)

sim_params = []
sim_params.append(param("BotMovementTicksPerSec", sim_schema["BotMovementTicksPerSec"],
                        {"DisplayName": "Max Movement Tick Rate", **sim_loc.get("BotMovementTicksPerSec", {})},
                        "AI Optimization Ranges", "Simulation"))
for i in range(1, 5):  # the game menu shows levels 1-4; level 5 is kept as-is in the document
    s = sim_schema["OptimizationRanges"]
    p = param("OptimizationRanges", s, {}, "AI Optimization Ranges", "Simulation")
    p.update(key=f"OptimizationRanges.{i}", docKey=f"optimizationRanges.{i}",
             name=f"Level {i} Range",
             desc=("Distance (cm) of AI optimization level %d. Bots beyond this distance are simulated "
                   "with less detail. Higher values look better at range but cost performance." % i))
    sim_params.append(p)

data = {
    "gameBuild": game_build,
    "aiHardVersion": normal["schemaHardVersion"],
    "aiSoftVersion": normal["schemaSoftVersion"],
    "simHardVersion": 1,
    "simSoftVersion": 1,
    "aiOrder": schema["__VariantOrder"],
    "aiLabels": AI_LABELS,
    "simOrder": sim_schema["__VariantOrder"],
    "ai": ai,
    "sim": sim,
    "params": params + sim_params,
}
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(data, f, indent=1, ensure_ascii=False)


def report(old, new):
    """Human-readable summary of the differences with the previous defaults.json."""
    if old is None:
        return ["(no previous defaults.json)"]
    lines = []
    for k in ("aiHardVersion", "aiSoftVersion", "aiOrder", "simOrder"):
        if old.get(k) != new.get(k):
            lines.append(f"{k}: {old.get(k)} -> {new.get(k)}")
    op = {p["key"]: p for p in old["params"]}
    np_ = {p["key"]: p for p in new["params"]}
    for k in np_.keys() - op.keys():
        lines.append(f"new param: {k}")
    for k in op.keys() - np_.keys():
        lines.append(f"removed param: {k}")
    for k in np_.keys() & op.keys():
        for f in ("min", "max", "uiMin", "uiMax", "step", "precision", "desc", "warn", "section"):
            if op[k].get(f) != np_[k].get(f):
                lines.append(f"{k}.{f}: {op[k].get(f)!r} -> {np_[k].get(f)!r}")

    def flat(d, prefix=""):
        r = {}
        for kk, v in d.items():
            if isinstance(v, dict):
                r.update(flat(v, prefix + kk + "."))
            else:
                r[prefix + kk] = v
        return r
    for group in ("ai", "sim"):
        for variant in new[group]:
            a, b = flat(old[group].get(variant, {})), flat(new[group][variant])
            for kk in sorted(a.keys() | b.keys()):
                if a.get(kk) != b.get(kk):
                    lines.append(f"{group}.{variant}.{kk}: {a.get(kk)!r} -> {b.get(kk)!r}")
    return lines or ["no change"]


print("Changes compared with the previous defaults.json:")
for line in report(previous, data):
    print("  " + line)
print("wrote", os.path.abspath(out), "-", len(params), "AI params,", len(sim_params), "sim params,",
      len(base), "doc keys")
