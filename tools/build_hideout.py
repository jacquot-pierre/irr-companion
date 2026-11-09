r"""Generates src/Resources/hideout.json: upgrade levels of the hideout units (costs and conditions).

Usage:
  python -I tools/build_hideout.py <extracted root>

<extracted root> is the folder given to `repak unpack -o`; it must contain
  Test_C/Content/Blueprints/MissionSystem/Actors/Hideout/        (pakchunk0 + pakchunk7)
  Test_C/Content/Blueprints/MissionSystem/Objects/HideoutCondition/ (not required, only for reference)
Extract with:
  repak unpack -o <root> -i "Test_C/Content/Blueprints/MissionSystem/Actors/Hideout" pakchunk0-Windows.pak
  repak unpack -o <root> -i "Test_C/Content/Blueprints/MissionSystem/Actors/Hideout" pakchunk7-Windows.pak

How the data is stored: every hideout unit blueprint has a BP_IRRHideoutUnitComponent whose class defaults
hold the unit tag (Mission.Hideout Unit.X) and an array of levels. Each level has condition objects
(other unit level, faction loyalty level, player level), a map of required items (gameplay tag -> count,
cash included), a map of resources (Resource.X -> amount), a description and actions. The assets are cooked
with unversioned properties, so the layout below is read field by field and every unexpected byte stops the
script (better fail than show wrong costs).

Item tags are matched to the item definition names of tools/item_paths.txt (used by irr.Item.Give).
"""
import glob
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', 'src', 'Resources', 'hideout.json')
ITEMS = os.path.join(HERE, 'item_paths.txt')

# Display order of the units (dependencies first); units not listed come after, alphabetically.
UNIT_ORDER = ['Structure', 'Generator', 'Lighting', 'Stash', 'Repair Station', 'Gunsmith', 'Terminal',
              'Intel Center', 'Shooting Range']
# Faction tags (Mission.Faction.X) -> abbreviation used in game, full name
FACTIONS = {
    'OPFOR': ('IGC', 'Iron Guard Corps'),
    'BLUEFOR': ('UICS', 'United Intercontinental Security'),
    'GREYFOR': ('VLF', 'Vietnamese Liberation Front'),
    'FANG': ('FANG', 'FANG'),
}
RESOURCES = ['Concrete', 'Food', 'Metal', 'Oil']
CASH_TAG = 'Inventory.Items.Currency.Cash'


class Reader:
    def __init__(self, data, pos=0):
        self.d, self.o = data, pos

    def i32(self):
        v = struct.unpack_from('<i', self.d, self.o)[0]
        self.o += 4
        return v

    def i8(self):
        v = struct.unpack_from('<b', self.d, self.o)[0]
        self.o += 1
        return v

    def raw(self, n):
        v = self.d[self.o:self.o + n]
        self.o += n
        return v

    def expect(self, b, what):
        got = self.raw(len(b))
        if got != b:
            raise ValueError('%s: expected %s, got %s at %d' % (what, b.hex(), got.hex(), self.o - len(b)))

    def fstring(self):
        n = self.i32()
        if n == 0:
            return ''
        if n < 0:
            return self.raw(-2 * n).decode('utf-16le')[:-1]
        return self.raw(n)[:-1].decode('latin1')


def package(path):
    """Names and exports of a cooked UE5 package (unversioned)."""
    names, _, exports = package_full(path)
    return names, exports


def package_full(path, numbered=False):
    """Names, import object names and exports of a cooked UE5 package (unversioned).
    numbered: import names with their number (ID_Glock_19 is stored as name ID_Glock + number 20)."""
    d = open(path, 'rb').read()
    r = Reader(d, 4)
    legacy = r.i32()
    if legacy != -4:
        r.i32()
    ue4, ue5, _ = r.i32(), r.i32(), r.i32()
    if ue4 == 0:
        ue5 = 1012  # unversioned: recent UE5 layout
    if legacy <= -9:
        r.raw(20)
        r.i32()
    r.raw(r.i32() * 20)  # custom versions
    if legacy > -9:
        r.i32()
    r.fstring()
    flags = struct.unpack('<I', r.raw(4))[0]
    name_count, name_off = r.i32(), r.i32()
    if ue5 >= 1008:
        r.raw(8)
    if not flags & 0x80000000:
        r.fstring()
    r.raw(8)
    export_count, export_off = r.i32(), r.i32()
    import_count, import_off = r.i32(), r.i32()
    names = []
    n = Reader(d, name_off)
    for _ in range(name_count):
        names.append(n.fstring())
        n.raw(4)
    imports = []
    q = import_off
    for _ in range(import_count):
        _, _, cn, _, outer, on, onum = struct.unpack_from('<iiiiiii', d, q)
        q += 28 + (0 if flags & 0x80000000 else 8) + (4 if ue5 >= 1003 else 0)
        imports.append(names[on] + ('_%d' % (onum - 1) if numbered and onum > 0 else ''))
    uexp = os.path.splitext(path)[0] + '.uexp'
    uexp_size = os.path.getsize(uexp) if os.path.exists(uexp) else 1 << 62
    for stride in range(72, 140, 4):
        exports = []
        for k in range(export_count):
            b = export_off + k * stride
            cls, _, _, outer, ni, nn = struct.unpack_from('<iiiiii', d, b)
            size, off = struct.unpack_from('<qq', d, b + 28)
            # name numbers can be huge in levels (generated actor names): check the serial range instead
            if not (-len(imports) <= cls <= export_count and 0 <= ni < len(names) and 0 <= outer <= export_count
                    and size >= 0 and 0 <= off - len(d) and off - len(d) + size <= uexp_size):
                exports = None
                break
            exports.append({'class': imports[-cls - 1] if cls < 0 else '', 'offset': off - len(d), 'size': size,
                            'outer': outer, 'name': names[ni], 'class_index': cls})
        if exports:
            return names, imports, exports
    raise ValueError('export table not understood: ' + path)


def plain(text):
    """Rich text (<strong>x</>, <Red>x</>) -> plain text."""
    return re.sub(r'<[^>]*>', '', text or '').strip()


def load_item_ids():
    ids = {}
    if os.path.exists(ITEMS):
        for line in open(ITEMS, encoding='utf-8-sig'):
            line = line.strip()
            if '/ID_' in line:
                n = line.rsplit('/', 1)[-1][3:].rsplit('.', 1)[0]
                ids[re.sub(r'[^a-z0-9]', '', n.lower())] = n
    return ids


def item_id(tag, ids):
    parts = tag.split('.')
    for cand in (parts[-1], parts[-2] + ' ' + parts[-1]):
        key = re.sub(r'[^a-z0-9]', '', cand.lower())
        if key in ids:
            return ids[key], cand
    return None, parts[-1]


def parse_unit(path, ids):
    names, exports = package(path)
    data = open(path[:-6] + 'uexp', 'rb').read()
    comp = [e for e in exports if e['class'] == 'BP_IRRHideoutUnitComponent_C']
    if not comp:
        return None
    r = Reader(data, comp[0]['offset'])
    head = r.raw(2)
    if head == b'\x01\x07':  # an extra object array is present before the unit tag
        for _ in range(r.i32()):
            r.i32()
    elif head != b'\x02\x05':
        # no unit tag / levels set here (e.g. BP_HideoutUnit_Master, the parent class)
        print('skipped %s: component without unit data (header %s)' % (os.path.basename(path), head.hex()))
        return None
    r.expect(b'\x00\x03', 'unit tag')
    unit_tag = names[r.i32()]
    r.i32()

    def condition(index):
        e = exports[index - 1]
        c = Reader(data, e['offset'])
        kind = e['class']
        if kind == 'BP_HideoutCondition_HideoutUnitLevel_C':
            c.expect(b'\x01\x05\x00\x03', 'unit condition')
            tag = names[c.i32()]
            c.i32()
            return {'type': 'unit', 'unit': tag.split('.')[-1], 'level': c.i32()}
        if kind == 'BP_HideoutCondition_Reputation_C':
            c.expect(b'\x01\x05', 'reputation condition')
            level = c.i32()
            c.expect(b'\x00\x03', 'faction tag')
            faction = names[c.i32()].split('.')[-1]
            return {'type': 'faction', 'faction': FACTIONS.get(faction, (faction,))[0], 'level': level}
        if kind == 'BP_HideoutCondition_PlayerLevel_C':
            c.expect(b'\x01\x03', 'player level condition')
            return {'type': 'player', 'level': c.i32()}
        raise ValueError('unknown condition class %s in %s' % (kind, path))

    def tag_map(what):
        if r.i32() != 0:
            raise ValueError(what + ': removed-keys count is not 0')
        out = []
        for _ in range(r.i32()):
            r.expect(b'\x00\x03', what + ' key')
            tag = names[r.i32()]
            r.i32()
            out.append((tag, r.i32()))
        return out

    levels = []
    for n in range(r.i32()):
        r.expect(b'\x00\x07\x00\x07', 'level %d header' % (n + 1))
        conds = [condition(r.i32()) for _ in range(r.i32())]
        items = tag_map('items')
        resources = tag_map('resources')
        r.i32()  # text flags
        history = r.i8()
        if history == -1:
            text = r.fstring() if r.i32() else ''
        elif history == 0:
            r.fstring()
            r.fstring()
            text = r.fstring()
        else:
            raise ValueError('text history %d not handled' % history)
        for _ in range(r.i32()):  # actions (visual changes in the hideout)
            r.i32()
        cash = sum(v for t, v in items if t == CASH_TAG)
        lst = []
        for t, v in items:
            if t == CASH_TAG:
                continue
            iid, name = item_id(t, ids)
            lst.append({'tag': t, 'name': name, 'id': iid, 'count': v})
        res = {}
        for t, v in resources:
            k = t.split('.')[-1]
            if k not in RESOURCES:
                raise ValueError('unknown resource ' + t)
            res[k] = v
        levels.append({'level': n + 1, 'description': plain(text), 'conditions': conds, 'items': lst,
                       'cash': cash, 'resources': res})
    free_first = bool(levels) and not (levels[0]['items'] or levels[0]['cash'] or levels[0]['resources']
                                        or levels[0]['conditions'])
    return {'name': unit_tag.split('.')[-1], 'tag': unit_tag, 'startLevel': 1 if free_first else 0,
            'levels': levels, 'source': os.path.basename(path)[:-7]}


def main():
    root = sys.argv[1]
    folder = os.path.join(root, 'Test_C', 'Content', 'Blueprints', 'MissionSystem', 'Actors', 'Hideout')
    ids = load_item_ids()
    units = []
    for p in sorted(glob.glob(os.path.join(folder, '**', '*.uasset'), recursive=True)):
        u = parse_unit(p, ids)
        if u and u['levels']:
            units.append(u)
    units.sort(key=lambda u: (UNIT_ORDER.index(u['name']) if u['name'] in UNIT_ORDER else 99, u['name']))
    known = {u['name'] for u in units}
    problems = ['unit %s not found (renamed or moved?)' % n for n in UNIT_ORDER if n not in known]
    for u in units:
        for L in u['levels']:
            for c in L['conditions']:
                if c['type'] == 'unit' and c['unit'] not in known:
                    problems.append('%s %d needs unknown unit %s' % (u['name'], L['level'], c['unit']))
            for it in L['items']:
                if not it['id']:
                    problems.append('%s %d: no item definition for %s' % (u['name'], L['level'], it['tag']))
    old = None
    if os.path.exists(OUT):
        old = json.load(open(OUT, encoding='utf-8'))
    doc = {'resources': RESOURCES,
           'factions': [{'tag': k, 'abbr': v[0], 'name': v[1]} for k, v in FACTIONS.items()],
           'units': units}
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
    for u in units:
        print('%-15s start %d, %d level(s)  (%s)' % (u['name'], u['startLevel'], len(u['levels']), u['source']))
    for p in problems:
        print('WARNING:', p)
    if old is not None:
        print('changed since the previous hideout.json' if old != doc else 'no change since the previous hideout.json')
    print('wrote', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
