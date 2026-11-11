r"""Generates src/Resources/loot.json: where each item can be found on the maps (loot containers and loose loot),
with the chance of each place to have it. Used by the item heatmap of the MAPS tab.

Usage:
  python -I tools/build_loot.py <extracted root>

<extracted root> is the folder given to `repak unpack -o` (the same as for build_weapons.py / build_traders.py);
besides Test_C/Content/Blueprints/InventorySystem it must contain
  Test_C/Content/Environment/Blueprints/Gameplay/LootContainers/   (container blueprints)
  Test_C/Content/Environment/Blueprints/DeadBody/                   (dead bodies, loot containers too)
  Test_C/Content/Maps/Gameplay/LVL_{Delta,Quarry,Outskirts}.umap/.uexp and Test_C/Content/Maps/Testing/LVL_Bunker.*
and src/Resources/maps.json must exist (floors, from build_maps.py).

How the game stores it (cooked, unversioned properties):
- Loot containers are actors of the persistent levels (BP_LootContainer_* and BP_DeadBody_*). Their contents come
  from an item preset (Presets/Container/Final/DA_LootContainer_*, an IRRItemsPreset): the class of the container
  (or a parent class) imports it; BP_LootContainer uses DA_LootContainer_General. A placed container can import
  another preset (checked, see `instance presets`).
- An IRRItemsPreset has ContainerTags, ContainerSlots (loot entries: fixed items, weighted choices, random loot
  with a LootGenerationSettings object) and DefaultItemsContainers_LEGACY (older settings, not used here: the
  ContainerSlots settings differ from them and are the live ones; a preset without ContainerSlots - the large
  safe - uses them).
- LootGenerationSettings: bUseCurve + ItemCountChanceCurve (chance of each number of items), Min/MaxGeneratedItems,
  Min/MaxTotalValue, ItemTypeSpawnWeights (item tag or family -> weight), RaritySpawnWeights (tier -> weight),
  bForceExactRarityMatch, LootConditions, ItemCountOverrides, ItemSpawnOverrides.
- Loose loot: BP_LooseLoot(_Generic) actors with a SpawnChance (0.5) and their own LootGenerationSettings (the
  class defaults: barter, medical, provisions). One item per point (the settings set no count: our assumption).

Chance of an item in a place (our reading of the settings, an estimate; shown as such in the app):
  number of items N: from the curve at each whole number between Min and Max (or uniform if no curve);
  each item: a type by its weight, then a rarity tier by its weight among the tiers that have items of that type,
  then an item of that type and tier at random (rarity from the vendor table, items without rarity are left out);
  chance to get the item at least once = sum over N of P(N) * (1 - (1 - p)^N), times the chance of the entry.
Not taken into account: loot contexts and modifiers of some areas or raids (night, more collectibles, more rarity),
the value limits of safes (MinTotalValue) and the item size limit of loose loot (LootCondition_ItemDimensions).
"""
import glob
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_weapons import Package, header  # noqa: E402
from build_traders import Reader, LOOT_ENTRY, RANDOM, read_items, vendor_info, item_ref, choice_chances, _bool  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..'))
OUT = os.path.join(ROOT, 'src', 'Resources', 'loot.json')
MAPS_JSON = os.path.join(ROOT, 'src', 'Resources', 'maps.json')
MAPS = [('Delta', 'Maps/Gameplay/LVL_Delta'), ('Quarry', 'Maps/Gameplay/LVL_Quarry'),
        ('Outskirts', 'Maps/Gameplay/LVL_Outskirts'), ('Bunker', 'Maps/Testing/LVL_Bunker')]
CLASS_DIRS = ['Environment/Blueprints/Gameplay/LootContainers', 'Environment/Blueprints/DeadBody',
              'Blueprints/InventorySystem/ExampleActors', 'Blueprints/InventorySystem/BaseActors/LooseLoot']
LOOSE = {'BP_LooseLoot_C': 'Loose loot', 'BP_LooseLoot_Generic_C': 'Loose loot (generic)'}
# preset -> label shown in the app
LABELS = {
    'DA_LootContainer_General': 'Crate', 'DA_LootContainer_General_IncreasedRarity': 'Rare crate / backpack',
    'DA_LootContainer_AmmoBox': 'Ammo box', 'DA_LootContainer_CashRegister': 'Cash register',
    'DA_LootContainer_DeadBody': 'Dead body', 'DA_LootContainer_Gear': 'Gear box',
    'DA_LootContainer_Jacket_01': 'Jacket', 'DA_LootContainer_Jacket_02': 'Jacket (small)',
    'DA_LootContainer_MedBox_01': 'Medical box', 'DA_LootContainer_MedBox_02': 'Medical box (large)',
    'DA_LootContainer_Safe_Compact': 'Compact safe', 'DA_LootContainer_Safe_Large': 'Large safe',
    'DA_LootContainer_WeaponAccBox': 'Weapon accessory box', 'DA_LootContainer_WeaponCase': 'Weapon case',
}
DEFAULT_PRESET = 'DA_LootContainer_General'
LOOSE_COUNT = (1, 1)  # items per loose loot point when the settings give no count (assumption)
LOCATION_INDEX = 5    # RelativeLocation of a SceneComponent (as in build_maps.py)


# ------------------------------------------------------------------ loot generation settings
_TAGMAP = lambda r: r.tmap(Reader.tag_struct, Reader.f32)
# random item parameters inside a legacy container: the maps are keyed by gameplay tag structs
RANDOM_TAGGED = [(n, _TAGMAP if n in ('ItemSpawnChances', 'RarityChanceDrop', 'BackpackItemSpawnChances') else
                  (lambda r: r.tmap(Reader.tag_struct, lambda r: (r.f64(), r.f64()))) if n == 'ItemCountOverrides' else f)
                 for n, f in RANDOM]
# DefaultItemsContainers_LEGACY element: container index, generate random loot, items, ?, generation chance, parameters
LEGACY = [('f0', Reader.i32), ('f1', _bool), ('Items', lambda r: r.array(lambda r: r.struct([], 'item'))), ('f3', _bool),
          ('f4', Reader.f32), ('Random', lambda r: r.struct(RANDOM_TAGGED, 'random parameters')), ('f6', Reader.i32)]

def read_settings(pkg, k):
    """LootGenerationSettings export -> {min, max, curve [(n, w)], types [(tag, w)], tiers {tier: w}}."""
    b = pkg.data(k)
    props, o = header(b)
    r = Reader(pkg, b, o)
    out = {'curve': None, 'min': None, 'max': None, 'types': [], 'tiers': {}}
    use_curve = False
    for i, zero in props:
        if zero:
            if i == 2:
                out['min'] = 0
            elif i == 3:
                out['max'] = 0
            continue
        if i == 0:
            use_curve = bool(r.u8())
        elif i == 1:  # FRuntimeFloatCurve > FRichCurve > Keys (native, 27 bytes each)
            if b[r.o:r.o + 4] != b'\x00\x03\x00\x03':
                raise ValueError('count curve layout at %d: %s' % (r.o, b[r.o:r.o + 8].hex()))
            r.o += 4
            keys = []
            for _ in range(r.i32()):
                t, v = struct.unpack_from('<ff', b, r.o + 3)
                keys.append((t, v))
                r.o += 27
            out['curve'] = keys
        elif i == 2:
            out['min'] = r.i32()
        elif i == 3:
            out['max'] = r.i32()
        elif i in (4, 5):
            r.f32()
        elif i == 6:
            out['types'] = r.tmap(Reader.tag_struct, Reader.f32)
        elif i == 7:
            out['tiers'] = dict(r.tmap(Reader.tag_struct, Reader.f32))
        elif i == 8:
            r.u8()
        elif i in (9, 11):
            r.array(Reader.ref)
        elif i == 10:
            r.tmap(Reader.tag_struct, lambda r: (r.f64(), r.f64()))
        else:
            raise ValueError('loot settings: unknown property %d' % i)
    if not use_curve:
        out['curve'] = None
    return out


def count_distribution(s, default=(1, 1)):
    """{n: probability} of the number of generated items."""
    lo = s['min'] if s['min'] is not None else default[0]
    hi = s['max'] if s['max'] is not None else default[1]
    hi = max(lo, hi)
    ns = list(range(lo, hi + 1))
    if s['curve']:
        keys = sorted(s['curve'])

        def at(x):
            if x <= keys[0][0]:
                return keys[0][1]
            if x >= keys[-1][0]:
                return keys[-1][1]
            for (x0, y0), (x1, y1) in zip(keys, keys[1:]):
                if x0 <= x <= x1:
                    return y0 + (y1 - y0) * (x - x0) / (x1 - x0) if x1 > x0 else y1
            return 0.0
        ws = [max(0.0, at(n)) for n in ns]
    else:
        ws = [1.0] * len(ns)
    tot = sum(ws)
    return {n: w / tot for n, w in zip(ns, ws)} if tot > 0 else {lo: 1.0}


def per_draw(s, items, rarity):
    """Item -> chance to be the item of one draw. Weights all at 0 (the gear box: Gear 0) count as equal weights;
    rarity weights all at 0 (the cash register) as no rarity filter."""
    types = [(t, w) for t, w in s['types'] if t]
    if types and all(w <= 0 for _, w in types):
        types = [(t, 1.0) for t, _ in types]
    total = sum(w for _, w in types if w > 0)
    out = {}
    if total <= 0:
        return out
    for tag, w in types:
        if w <= 0:
            continue
        members = [k for k, (_, t) in items.items() if t and (t == tag or t.startswith(tag + '.'))]
        by_tier = {}
        for k in members:
            tr = rarity.get(k)
            if tr:
                by_tier.setdefault(tr, []).append(k)
        usable = {t: v for t, v in s['tiers'].items() if v > 0 and by_tier.get(t)}
        tw = sum(usable.values())
        if tw <= 0:
            if members and not any(v > 0 for v in s['tiers'].values()):
                for k in members:  # no rarity filter
                    out[k] = out.get(k, 0.0) + (w / total) / len(members)
            continue
        for t, v in usable.items():
            for k in by_tier[t]:
                out[k] = out.get(k, 0.0) + (w / total) * (v / tw) / len(by_tier[t])
    return out


def at_least_once(p, dist):
    return sum(q * (1 - (1 - p) ** n) for n, q in dist.items())


def combine(acc, chances, factor=1.0):
    """acc[item] = chance of getting it from any of the independent sources added so far."""
    for k, p in chances.items():
        p *= factor
        acc[k] = 1 - (1 - acc.get(k, 0.0)) * (1 - p)


def preset_chances(path, items, rarity):
    """Item -> chance to be in a container filled from this preset."""
    pkg = Package(path)
    b = pkg.data(1)
    props, o = header(b)
    r = Reader(pkg, b, o)
    acc = {}
    has_slots = any(i == 1 and not z for i, z in props)
    for i, zero in props:
        if zero:
            continue
        if i == 0:
            r.array(Reader.fname)
        elif i == 1:
            for e in r.array(lambda r: r.struct(LOOT_ENTRY, 'slot')):
                gen = e.get('GenerationChance') if e.get('GenerationChance') is not None else 0.0
                for x in e.get('FixedItems') or []:
                    ref = item_ref(x)
                    if ref and ref.startswith('ID_'):
                        combine(acc, {ref[3:]: 1.0}, gen)
                choices = e.get('ItemChoices') or []
                if choices:
                    cc = choice_chances([c.get('Weight') or 0 for c in choices], e.get('MinChoices') or 0, e.get('MaxChoices') or 0)
                    combine(acc, {item_ref(c['Item'])[3:]: p for c, p in zip(choices, cc) if (item_ref(c['Item']) or '').startswith('ID_')},
                            gen * (e.get('ChoiceChance') or 0))
                if e.get('bGenerateRandomLoot') and isinstance(e.get('LootSettings'), int):
                    s = read_settings(pkg, e['LootSettings'])
                    dist = count_distribution(s)
                    combine(acc, {k: at_least_once(p, dist) for k, p in per_draw(s, items, rarity).items()}, gen)
        elif i == 2:
            if has_slots:
                break  # DefaultItemsContainers_LEGACY: replaced by ContainerSlots
            for leg in r.array(lambda r: r.struct(LEGACY, 'legacy container')):
                rp = leg.get('Random') or {}
                s = {'curve': None, 'min': rp.get('MinItems') or 0, 'max': rp.get('MaxItems') or 0,
                     'types': rp.get('ItemSpawnChances') or [], 'tiers': dict(rp.get('RarityChanceDrop') or [])}
                gen = leg.get('f4') if leg.get('f4') is not None else 1.0
                dist = count_distribution(s)
                combine(acc, {k: at_least_once(p, dist) for k, p in per_draw(s, items, rarity).items()}, gen)
        else:
            raise ValueError('items preset: unknown property %d in %s' % (i, path))
    return acc


# ------------------------------------------------------------------ levels
def unversioned_first_vector(b):
    props, o = header(b)
    if not props or props[0][0] != LOCATION_INDEX or props[0][1]:
        return None
    return struct.unpack_from('<ddd', b, o)


def floor_of(z, floors):
    if not floors:
        return 0
    inside = [f for f in floors if abs(z - f['centerZ']) <= f['extentZ'] + 50]
    pool = inside or floors
    return min(pool, key=lambda f: abs(z - f['centerZ']))['index']


def main():
    root = sys.argv[1]
    content = os.path.join(root, 'Test_C', 'Content')
    inv = os.path.join(content, 'Blueprints', 'InventorySystem')
    items = read_items(inv)
    rarity, _ = vendor_info(inv, presets=True)
    print('%d item definitions, %d with a rarity tier' % (len(items), len(rarity)))

    # class -> package path
    class_files = {}
    for d in CLASS_DIRS:
        for p in glob.glob(os.path.join(content, d.replace('/', os.sep), '*.uasset')):
            class_files[os.path.basename(p)[:-7] + '_C'] = p
    presets = {os.path.basename(p)[:-7]: p for p in glob.glob(os.path.join(inv, 'Presets', 'Container', '**', 'DA_*.uasset'), recursive=True)}

    def class_preset(cls, seen=()):
        p = class_files.get(cls)
        if not p:
            return None
        pkg = Package(p)
        da = [n for n in pkg.imports if n.startswith('DA_LootContainer')]
        if da:
            return da[0]
        parents = [n for n in pkg.imports if n.endswith('_C') and n != cls and n in class_files and n not in seen]
        for parent in parents:
            r = class_preset(parent, seen + (cls,))
            if r:
                return r
        return DEFAULT_PRESET if cls == 'BP_LootContainer_C' else None

    def loose_settings(cls):
        pkg = Package(class_files[cls])
        cdo = [k for k, e in enumerate(pkg.exports, 1) if e['name'] == 'Default__' + cls][0]
        b = pkg.data(cdo)
        props, o = header(b)
        if [i for i, _ in props][:1] != [0]:
            raise ValueError('%s: spawn chance not found' % cls)
        chance = struct.unpack_from('<f', b, o)[0]
        ls = [k for k, e in enumerate(pkg.exports, 1) if e['class'] == 'LootGenerationSettings']
        if len(ls) != 1:
            raise ValueError('%s: loot settings not found' % cls)
        return chance, read_settings(pkg, ls[0])

    sources, source_index, chances = [], {}, []

    def source(key, label, kind, fn):
        if key not in source_index:
            source_index[key] = len(sources)
            ch = {k: round(v, 4) for k, v in fn().items() if v >= 0.00005}
            sources.append({'id': key, 'label': label, 'kind': kind, 'count': 0})
            chances.append(ch)
        return source_index[key]

    floors_by_map = {}
    if os.path.exists(MAPS_JSON):
        for m in json.load(open(MAPS_JSON, encoding='utf-8'))['maps']:
            floors_by_map[m['name']] = [dict(f, index=i) for i, f in enumerate(m['floors'])]
    maps_out = {}
    problems = []
    for name, lvl in MAPS:
        umap = os.path.join(content, lvl.replace('/', os.sep) + '.umap')
        if not os.path.exists(umap):
            problems.append('%s: level not found' % name)
            continue
        pkg = Package(umap)
        f = open(umap[:-5] + '.uexp', 'rb')
        children = {}
        for k, e in enumerate(pkg.exports, 1):
            children.setdefault(e['outer'], []).append(k)

        def blob(k):
            e = pkg.exports[k - 1]
            f.seek(e['offset'])
            return f.read(e['size'])
        da_imports = {-(j + 1): n for j, n in enumerate(pkg.imports) if n.startswith('DA_LootContainer')}
        points, overrides, no_pos = [], 0, 0
        for k, e in enumerate(pkg.exports, 1):
            cls = e['class']
            loose = cls in LOOSE
            if not loose and not (cls.startswith('BP_LootContainer') or cls.startswith('BP_LootContaine_') or cls.startswith('BP_DeadBody')):
                continue
            if loose:
                chance, settings = loose_settings(cls)
                dist = count_distribution(settings, LOOSE_COUNT)
                si = source(cls, LOOSE[cls], 'loose', lambda: {kk: chance * at_least_once(p, dist) for kk, p in per_draw(settings, items, rarity).items()})
            else:
                preset = class_preset(cls)
                # a placed container may use another preset (an import referenced by the actor or its components)
                if da_imports:
                    for kk in [k] + children.get(k, []):
                        if pkg.exports[kk - 1]['size'] > 65536:
                            continue
                        bb = blob(kk)
                        hit = [da_imports[v] for v in set(struct.unpack_from('<%di' % (len(bb) // 4), bb[:len(bb) // 4 * 4]))
                               if v in da_imports]
                        if hit:
                            preset, overrides = hit[0], overrides + 1
                            break
                if not preset or preset not in presets:
                    problems.append('%s: %s without preset (%s)' % (name, cls, preset))
                    continue
                si = source(preset, LABELS.get(preset, preset.replace('DA_LootContainer_', '')), 'container',
                            lambda: preset_chances(presets[preset], items, rarity))
            pos = None
            for kk in children.get(k, []):
                ce = pkg.exports[kk - 1]
                if ce['class'] == 'SceneComponent' and ce['name'] in ('DefaultSceneRoot', 'Root'):
                    pos = unversioned_first_vector(blob(kk))
                    if pos:
                        break
            if not pos:
                no_pos += 1
                continue
            x, y, z = pos
            points.append([round(x), round(y), floor_of(z, floors_by_map.get(name, [])), si])
            sources[si]['count'] += 1
        f.close()
        maps_out[name] = points
        by = {}
        for p in points:
            by[sources[p[3]]['label']] = by.get(sources[p[3]]['label'], 0) + 1
        print('%-10s %d places (%s)%s%s' % (name, len(points), ', '.join('%s %d' % kv for kv in sorted(by.items(), key=lambda kv: -kv[1])),
                                          ', %d use their own preset' % overrides if overrides else '',
                                          ', %d without position (skipped)' % no_pos if no_pos else ''))
    used = sorted({k for ch in chances for k in ch}, key=str.lower)
    data = {'version': 1, 'sources': [dict(s, chances=ch) for s, ch in zip(sources, chances)],
            'items': {k: {'name': items[k][0]} for k in used},
            'maps': maps_out}
    with open(OUT, 'w', encoding='utf-8', newline='\n') as fo:
        json.dump(data, fo, separators=(',', ':'), ensure_ascii=False)
    print('%d kinds of places, %d items that can be found -> %s (%d KB)' % (len(sources), len(used), os.path.normpath(OUT), os.path.getsize(OUT) // 1024))
    for p in problems:
        print('WARNING:', p)


if __name__ == '__main__':
    main()
