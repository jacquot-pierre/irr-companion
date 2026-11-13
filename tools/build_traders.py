r"""Generates src/Resources/traders.json: what the traders can have in stock, with the chance of each item.

Usage:
  python -I tools/build_traders.py <extracted root>

<extracted root> is the folder given to `repak unpack -o` (the same as for build_weapons.py); it must contain
  Test_C/Content/Blueprints/InventorySystem/   (Items/, Presets/, Vendor/, from every pak)

How the game stores it: each trader is an item preset Presets/Container/Traders/DA_Trader_<faction>
(class IRRItemsPreset): an array of loot entries, each with
- FixedItems: items always added (one entry per unit, so an item listed 3 times comes 3 times);
- ItemChoices: weighted items, MinChoices..MaxChoices of them drawn (no repeat), with ChoiceChance;
- bGenerateRandomLoot + LootSettings (a LootGenerationSettings object): MinItemsToAdd..MaxItemsToAdd random
  items; each draw picks an item tag by its weight (ItemSpawnChances: an item or a category such as
  Inventory.Items.Attachments.Sight) and a rarity tier by its weight (RarityChanceDrop);
- GenerationChance: the chance that the entry is generated at all.
Weapons (entries with weapon presets or weapon categories) are left out: only their chance is written.

The traders themselves are blueprints BaseActors/Trader/BP_RaidTrader_<x> (subclasses of BP_RaidTrader): each has
a name tag (Trader.Lt Moroz) and its stock preset; two traders can share a preset (Cpt Hale and Voss: UICS).
The map of each trader is in TRADER_MAPS (read from the map levels, see the comment there).

Chances computed here:
- fixed items: 100 %;
- weighted draw: exact chance that the item is among the k drawn (k uniform in Min..Max, weighted draws
  without repeat);
- random loot: chance to get the item at least once in N draws (N uniform in Min..Max), each draw picking the
  tag by weight, then for a category a rarity tier by weight among the tiers that have items in it, then an
  item of that tier at random. The rarity of an item is its ItemRarityTag in the vendor table
  (Vendor/DT_Vendor_Old). This is our reading of the settings, not the game's code: shown as an estimate.
Prices: the market price of the item in the vendor table (MarketAvailability.DefaultMarketPrice, in cash; the
price of one unit: a row giving 60 rounds has the price of one round). A trader in a raid can apply its own
multiplier (PriceMultiplier and PriceOverrides of the trader actor, set in the maps): not read.
The assets are cooked with unversioned properties; every unexpected layout stops the script.
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

OUT = os.path.join(HERE, '..', 'src', 'Resources', 'traders.json')
# Trader preset suffix -> faction (as in the hideout data)
TRADERS = [('IGC', 'Iron Guard Corps'), ('UICS', 'United Intercontinental Security'), ('VLF', 'Vietnamese Liberation Front')]
TIERS = ['Inventory.Rarity Types.Tier %d' % i for i in range(1, 5)]
# Trader blueprint -> map. Found in the levels (2026-11-13): Bunker places BP_RaidTrader_VLF in its persistent level;
# Delta (IGC), Quarry (UICS_Quarry) and Outskirts (UICS_Outskirts) place theirs with the PLI_InRaidTrader prefab, in a
# World Partition cell of the map (their positions are in maps.json, tools/build_maps.py).
TRADER_MAPS = {'BP_RaidTrader_UICS_Quarry': 'Quarry', 'BP_RaidTrader_UICS_Outskirts': 'Outskirts',
               'BP_RaidTrader_VLF': 'Bunker', 'BP_RaidTrader_IGC': 'Delta'}
MAP_GUESSED = set()  # traders whose map is a guess (shown "probably")


# ------------------------------------------------------------------ unversioned reader
class Reader:
    def __init__(self, pkg, b, o=0):
        self.pkg, self.b, self.o = pkg, b, o

    def i32(self):
        v = struct.unpack_from('<i', self.b, self.o)[0]
        self.o += 4
        return v

    def f32(self):
        v = struct.unpack_from('<f', self.b, self.o)[0]
        self.o += 4
        return v

    def f64(self):
        v = struct.unpack_from('<d', self.b, self.o)[0]
        self.o += 8
        return v

    def u8(self):
        v = self.b[self.o]
        self.o += 1
        return v

    def fname(self):
        n = self.pkg.fname(self.b, self.o)
        self.o += 8
        return n

    def ref(self):
        v = self.i32()
        if v < 0:
            return self.pkg.imports[-v - 1]
        return v or None  # export index

    def array(self, fn):
        return [fn(self) for _ in range(self.i32())]

    def tmap(self, kf, vf):
        if self.i32() != 0:
            raise ValueError('map with removed keys at %d' % self.o)
        return [(kf(self), vf(self)) for _ in range(self.i32())]

    def tag_struct(self):
        props, n = header(self.b[self.o:])
        self.o += n
        if props == [(0, True)]:
            return None  # empty tag
        if props != [(0, False)]:
            raise ValueError('gameplay tag layout %s at %d' % (props, self.o))
        return self.fname()

    def struct(self, schema, what):
        props, n = header(self.b[self.o:])
        self.o += n
        out = {}
        for i, zero in props:
            if i >= len(schema):
                raise ValueError('%s: unknown field %d at %d' % (what, i, self.o))
            name, fn = schema[i]
            out[name] = None if zero else fn(self)
        return out


def _bool(r):
    return r.u8()


# IRRRandomItemParameters (parameters of an item entry: contents of containers, attachments...)
RANDOM = [('MinItems', Reader.i32), ('MaxItems', Reader.i32), ('MinValue', Reader.f32), ('MaxValue', Reader.f32),
          ('ItemSpawnChances', lambda r: r.tmap(Reader.fname, Reader.f32)),
          ('RarityChanceDrop', lambda r: r.tmap(Reader.fname, Reader.f32)),
          ('bCanAddSameItem', _bool), ('bForceMatchRarity', _bool), ('bForceVitalParts', _bool),
          ('SupportedFactions', lambda r: r.array(Reader.fname)), ('AttachmentMinDepth', Reader.i32),
          ('AttachmentMaxDepth', Reader.i32), ('BackpackMinItems', Reader.i32), ('BackpackMaxItems', Reader.i32),
          ('BackpackItemSpawnChances', lambda r: r.tmap(Reader.fname, Reader.f32)),
          ('ItemMaxDimensions', lambda r: (r.f64(), r.f64())),
          ('ItemCountOverrides', lambda r: r.tmap(Reader.fname, lambda r: (r.i32(), r.i32()))),
          ('f17', Reader.i32)]
# item entry (FIRRItemData): bool, preset, item definition, ..., random parameters, ...
ENTRY = [('f0', _bool), ('Preset', Reader.ref), ('Definition', Reader.ref), ('f3', Reader.i32),
         ('Random', lambda r: r.struct(RANDOM, 'random parameters')), ('f5', Reader.i32), ('f6', Reader.i32),
         ('f7', Reader.i32), ('f8', Reader.f32)]
WEIGHTED = [('Item', lambda r: r.struct(ENTRY, 'item')), ('Weight', Reader.f32)]
LOOT_ENTRY = [('f0', Reader.i32), ('f1', _bool),
              ('FixedItems', lambda r: r.array(lambda r: r.struct(ENTRY, 'item'))),
              ('ItemChoices', lambda r: r.array(lambda r: r.struct(WEIGHTED, 'weighted item'))),
              ('ChoiceChance', Reader.f32), ('MinChoices', Reader.i32), ('MaxChoices', Reader.i32),
              ('bAllowRepeatedChoice', _bool), ('bGenerateRandomLoot', _bool), ('GenerationChance', Reader.f32),
              ('LootSettings', Reader.ref), ('PostProcessors', lambda r: r.array(Reader.ref))]
# LootGenerationSettings object: 2 properties before the random item parameters
LOOT_SETTINGS = [('p0', Reader.i32), ('p1', Reader.i32)] + [
    (n, (lambda r: r.tmap(Reader.tag_struct, Reader.f32)) if n in ('ItemSpawnChances', 'RarityChanceDrop') else f)
    for n, f in RANDOM]
# vendor table row (IRRVendorDefaultItem)
FACTION = [('FactionTag', Reader.tag_struct), ('RequiredLoyaltyLevel', Reader.f32)]
MARKET = [('DefaultMarketPrice', Reader.f32), ('BlackMarketPrice', Reader.f32), ('bSoldInStockMarket', _bool),
          ('bSoldInBlackMarket', _bool), ('bCanBeSoldInStockMarket', _bool), ('bCanBeSoldInBlackMarket', _bool),
          ('bIsSold', _bool)]
VENDOR_ROW = [('ItemData', lambda r: r.struct(ENTRY, 'item')), ('bVariation', _bool), ('ItemRarityTag', Reader.tag_struct),
              ('bOverrideDefaultStockCount', _bool), ('DefaultStockCount', Reader.i32), ('bOverrideMaxStockCount', _bool),
              ('MaxStockCount', Reader.i32), ('bOverrideRestockRate', _bool), ('RestockRate', Reader.f32),
              ('bOverrideFactionAvailability', _bool), ('FactionAvailability', lambda r: r.struct(FACTION, 'faction')),
              ('MarketAvailability', lambda r: r.struct(MARKET, 'market'))]


def item_ref(e):
    """ID_x / DA_x of an item entry: field 0 set = the item definition is used, else the preset."""
    if e.get('f0') and e.get('Definition'):
        return e['Definition']
    return e.get('Preset') or e.get('Definition')


def read_trader(path):
    pkg = Package(path)
    r = Reader(pkg, pkg.data(1))
    props, n = header(r.b)
    r.o = n
    if props != [(1, False)]:
        raise ValueError('trader preset layout %s: %s' % (props, path))
    entries = r.array(lambda r: r.struct(LOOT_ENTRY, 'loot entry'))
    if r.b[r.o:] != b'\0\0\0\0':
        raise ValueError('trader preset: %d bytes left in %s' % (len(r.b) - r.o, path))
    settings = {}
    for e in entries:
        k = e.get('LootSettings')
        if isinstance(k, int):
            s = Reader(pkg, pkg.data(k))
            props, n = header(s.b)
            s.o = n
            out = {}
            for i, zero in props:
                if i >= 8:  # past the two maps: the rest is not needed
                    break
                name, fn = LOOT_SETTINGS[i]
                out[name] = None if zero else fn(s)
            settings[k] = out
    return entries, settings


def vendor_info(inv, presets=False):
    """Item definition (ID_x) -> (rarity tier tag, market price), from the vendor table (first row of the item).
    presets: also the rows that give a preset (weapons, loaded magazines): the rarity goes to the preset's item."""
    path = os.path.join(inv, 'Vendor', 'DT_Vendor_Old.uasset')
    pkg = Package(path)
    b = pkg.data(1)
    count = struct.unpack_from('<i', b, 10)[0]
    rarity, price, rows = {}, {}, 0
    for m in re.finditer(rb'\x80\x19(..)\x80\x13(..)', b, re.S):
        r = Reader(pkg, b, m.start())
        props, n = header(b[m.start():])
        r.o = m.start() + n
        row = {}
        for i, zero in props:
            if i >= len(VENDOR_ROW):
                raise ValueError('vendor row: unknown field %d' % i)
            name, fn = VENDOR_ROW[i]
            row[name] = None if zero else fn(r)
        # the next row name follows (or the end of the table)
        if r.o not in (len(b), len(b) - 4) and not re.match(rb'.{8}\x80\x19', b[r.o:r.o + 10], re.S):
            raise ValueError('vendor row %s: unexpected layout at %d' % (pkg.fname(b, m.start() - 8), r.o))
        rows += 1
        data = row.get('ItemData') or {}
        ref = item_ref(data)
        if ref and ref.startswith('DA_') and presets:
            from build_weapons import read_preset
            target = read_preset(inv, ref)[0]
            if target and target.startswith('ID_') and row.get('ItemRarityTag'):
                rarity.setdefault(target[3:], row['ItemRarityTag'])
            continue
        if not ref or ref.startswith('DA_'):
            continue  # presets: the item is in the preset (weapons, loaded magazines)
        k = ref[3:]
        if row.get('ItemRarityTag'):
            rarity.setdefault(k, row['ItemRarityTag'])
        market = row.get('MarketAvailability') or {}
        p = market.get('DefaultMarketPrice')
        if p and p > 0:
            per_unit = p  # the price is per unit (a row of 60 rounds has the price of one round)
            price.setdefault(k, round(per_unit, 2))
    if rows != count:
        raise ValueError('vendor table: %d rows read of %d' % (rows, count))
    return rarity, price


def read_items(inv):
    """Item name -> (display name, own tag) for every item definition."""
    items = {}
    for p in glob.glob(os.path.join(inv, 'Items', '**', 'ID_*.uasset'), recursive=True):
        pkg = Package(p)
        defs = [k for k, e in enumerate(pkg.exports, 1) if e['class'] == 'IRRItemDefinition']
        if len(defs) != 1:
            continue
        b = pkg.data(defs[0])
        tag = None
        for o in range(0, 48):
            i, num = struct.unpack_from('<ii', b, o)
            if 0 <= i < len(pkg.names) and num == 0 and pkg.names[i].startswith('Inventory.Items.'):
                tag = pkg.names[i]
                break
        items[os.path.basename(p)[3:-7]] = (pkg.first_text(defs[0]) or os.path.basename(p)[3:-7], tag)
    return items


# ------------------------------------------------------------------ chances
def choice_chances(weights, kmin, kmax):
    """Chance of each item to be among k drawn (weighted, no repeat), k uniform in kmin..kmax. Exact."""
    n = len(weights)
    w = [float(x) for x in weights]
    total = sum(w)
    ks = list(range(kmin, min(kmax, n) + 1))
    size = 1 << n
    pop = [0] * size
    wsum = [0.0] * size
    for m in range(1, size):
        low = m & -m
        i = low.bit_length() - 1
        pop[m] = pop[m ^ low] + 1
        wsum[m] = wsum[m ^ low] + w[i]
    # reach[m]: probability that the first popcount(m) draws are exactly the items of m (in any order)
    reach = [0.0] * size
    reach[0] = 1.0
    out = [0.0] * n
    for m in sorted(range(1, size), key=pop.__getitem__):
        if pop[m] > ks[-1]:
            break
        p = 0.0
        mm = m
        while mm:
            low = mm & -mm
            i = low.bit_length() - 1
            prev = m ^ low
            p += reach[prev] * w[i] / (total - wsum[prev])
            mm ^= low
        reach[m] = p
        if pop[m] in ks:
            mm = m
            while mm:
                low = mm & -mm
                out[low.bit_length() - 1] += p / len(ks)
                mm ^= low
    return out


def random_chances(settings, items, rarity):
    """[{tag, label, weight, share, items: [{id, chance}]}] of a LootGenerationSettings (see the module doc)."""
    spawn = settings.get('ItemSpawnChances') or []
    tiers = dict(settings.get('RarityChanceDrop') or [])
    nmin, nmax = settings.get('MinItems') or 0, settings.get('MaxItems') or 0
    total = sum(w for _, w in spawn)
    draws = list(range(nmin, nmax + 1)) or [0]
    out = []
    for tag, wt in spawn:
        exact = [k for k, (_, t) in items.items() if t == tag]
        if exact:
            members = {k: 1.0 for k in exact}
        else:
            kids = [k for k, (_, t) in items.items() if t and t.startswith(tag + '.')]
            by_tier = {}
            for k in kids:
                by_tier.setdefault(rarity.get(k), []).append(k)
            usable = {t: v for t, v in tiers.items() if by_tier.get(t)}
            tw = sum(usable.values())
            members = {}
            for t, v in usable.items():
                for k in by_tier[t]:
                    members[k] = (v / tw) / len(by_tier[t])
        share = wt / total if total else 0
        row = {'tag': tag, 'weight': wt, 'share': round(share, 4), 'items': []}
        for k, frac in sorted(members.items(), key=lambda kv: -kv[1]):
            p = share * frac
            at_least_once = sum(1 - (1 - p) ** n for n in draws) / len(draws)
            row['items'].append({'id': k, 'chance': round(at_least_once, 4), 'tier': (rarity.get(k) or '')[-1:] or None})
        out.append(row)
    return out


def trader_people(inv):
    """Stock preset (IGC...) -> [{name, map, guessed}] from the trader blueprints."""
    people = {}
    for p in sorted(glob.glob(os.path.join(inv, 'BaseActors', 'Trader', 'BP_RaidTrader_*.uasset'))):
        pkg = Package(p)
        bp = os.path.basename(p)[:-7]
        names = [n[len('Trader.'):] for n in pkg.names if n.startswith('Trader.')]
        presets = [n[len('DA_Trader_'):] for n in pkg.imports if n.startswith('DA_Trader_') and not n.endswith('_Appearance')
                   and '_Appearance_' not in n]
        if len(names) != 1 or len(presets) != 1:
            print('WARNING: %s: name %s, stock preset %s' % (bp, names, presets))
            continue
        if bp not in TRADER_MAPS:
            print('WARNING: %s (%s): map unknown, add it to TRADER_MAPS' % (bp, names[0]))
        people.setdefault(presets[0], []).append({'name': names[0], 'map': TRADER_MAPS.get(bp),
                                                  'guessed': bp in MAP_GUESSED})
    return people


def main():
    root = sys.argv[1]
    inv = os.path.join(root, 'Test_C', 'Content', 'Blueprints', 'InventorySystem')
    people = trader_people(inv)
    items = read_items(inv)
    rarity, price = vendor_info(inv)
    print('%d item definitions, %d with a rarity tier, %d with a price' % (len(items), len(rarity), len(price)))
    traders = []
    for abbr, faction in TRADERS:
        path = os.path.join(inv, 'Presets', 'Container', 'Traders', 'DA_Trader_%s.uasset' % abbr)
        entries, settings = read_trader(path)
        t = {'id': abbr, 'name': faction, 'people': people.get(abbr, []), 'fixed': [], 'choices': [], 'random': [], 'weapons': None}
        for e in entries:
            gen = e.get('GenerationChance') if e.get('GenerationChance') is not None else 0.0
            fixed = e.get('FixedItems') or []
            choices = e.get('ItemChoices') or []
            weaponish = [x for x in choices if (item_ref(x['Item']) or '').startswith('DA_')]
            if weaponish or (isinstance(e.get('LootSettings'), int) and not e.get('bGenerateRandomLoot')):
                t['weapons'] = round(gen * (e.get('ChoiceChance') or 0), 4)  # weapon presets: left out
                continue
            for x in fixed:
                ref = item_ref(x)
                if not ref or not ref.startswith('ID_'):
                    raise ValueError('%s: fixed item %s' % (abbr, ref))
                k = ref[3:]
                hit = next((f for f in t['fixed'] if f['id'] == k), None)
                if hit:
                    hit['count'] += 1
                else:
                    t['fixed'].append({'id': k, 'count': 1})
            if choices:
                ws = [x.get('Weight') or 0 for x in choices]
                cc = choice_chances(ws, e.get('MinChoices') or 0, e.get('MaxChoices') or 0)
                factor = gen * (e.get('ChoiceChance') or 0)
                group = {'min': e.get('MinChoices'), 'max': e.get('MaxChoices'), 'chance': round(factor, 4), 'items': []}
                for x, w, c in zip(choices, ws, cc):
                    group['items'].append({'id': item_ref(x['Item'])[3:], 'weight': w, 'chance': round(c * factor, 4)})
                group['items'].sort(key=lambda i: -i['chance'])
                t['choices'].append(group)
            if e.get('bGenerateRandomLoot') and isinstance(e.get('LootSettings'), int):
                s = settings[e['LootSettings']]
                cats = random_chances(s, items, rarity)
                for c in cats:
                    for i in c['items']:
                        i['chance'] = round(i['chance'] * gen, 4)
                t['random'].append({'min': s.get('MinItems'), 'max': s.get('MaxItems'), 'chance': round(gen, 4),
                                    'tiers': [[tag[-1:], w] for tag, w in (s.get('RarityChanceDrop') or [])],
                                    'categories': cats})
        traders.append(t)
        print('%s (%s): %d fixed items (%d units), %s weighted, %s random categories, weapon chance %s' % (
            abbr, ', '.join('%s on %s' % (x['name'], x['map']) for x in t['people']) or 'no trader', len(t['fixed']), sum(f['count'] for f in t['fixed']),
            '+'.join(str(len(g['items'])) for g in t['choices']) or '0',
            '+'.join(str(len(g['categories'])) for g in t['random']) or '0', t['weapons']))
    used = {f['id'] for t in traders for f in t['fixed']} | {i['id'] for t in traders for g in t['choices'] for i in g['items']} | \
           {i['id'] for t in traders for g in t['random'] for c in g['categories'] for i in c['items']}
    missing = sorted(k for k in used if k not in items)
    if missing:
        raise ValueError('items without definition: %s' % missing)
    cat_names = {}
    for t in traders:
        for g in t['random']:
            for c in g['categories']:
                exact = [k for k, (_, tg) in items.items() if tg == c['tag']]
                c['label'] = items[exact[0]][0] if exact else c['tag'].split('.')[-1]
                c['item'] = exact[0] if exact else None
    data = {'version': 1, 'traders': traders,
            'items': {k: dict({'name': items[k][0]}, **({'price': price[k]} if k in price else {}))
                      for k in sorted(used, key=str.lower)}}
    no_price = sorted(k for k in used if k not in price)
    if no_price:
        print('items without a market price (%d): %s' % (len(no_price), ', '.join(no_price)))
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print('%d items -> %s' % (len(used), os.path.normpath(OUT)))


if __name__ == '__main__':
    main()
