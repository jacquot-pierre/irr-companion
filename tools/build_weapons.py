r"""Generates src/Resources/weapons.json: weapons, their attachment slots and what each slot accepts.

Usage:
  python -I tools/build_weapons.py <extracted root>

<extracted root> is the folder given to `repak unpack -o`; it must contain
  Test_C/Content/Blueprints/InventorySystem/   (Items/ and Objects/, from every pak)
Extract with (for each pak of Test_C/Content/Paks):
  repak unpack -o <root> -f -i "Test_C/Content/Blueprints/InventorySystem" <pak>

How the game stores it (the gunsmith uses the same data):
- Every item has an item definition Items/<Category>/.../ID_<Name> (an IRRItemDefinition). <Name> is the row
  name that `additem` takes. Its first FName is the item's own gameplay tag
  (Inventory.Items.Attachments.Handguard.AK SAG MK1 Handguard) and its first FText is the display name.
- The definition owns container objects (subobjects whose class is one of Objects/ItemContainers/**/U_*):
  every container is one attachment slot (Handguard, Muzzle, Sight...). Attachments have containers too
  (a handguard has rail and foregrip slots), so a weapon is a tree.
- A container lists "supported items" objects (property 18 = array of subobjects). Each one is either an
  InventorySupportedItems with an inline tag list, or a UISI_* blueprint (Objects/InventorySupportedItems)
  whose class defaults hold the tag list. A slot accepts the items whose own tag is in that list.
- When a container in a definition does not override property 18, the container class defaults apply
  (Objects/ItemContainers/**/U_*.uasset, walking up the parent classes). The slot name is the FText of the
  first class default that has one.
- `additem <row> <count>` does not take the item name: it takes a row name of the legacy vendor table
  Vendor/DT_Vendor_Old (825 rows). A row points either at an item definition or at an item preset
  (Presets/**/DA_*, an IRRItemPreset): the item plus what is put in it. Weapon rows give the weapon's default
  preset (fitted with its default parts and a loaded magazine), magazine rows a loaded magazine (the x300
  variants of the AR-15 magazines are the same magazine loaded with x300 rounds). The row names often differ
  from the item names (MP5_Picatinny_Rail gives MP5_Picanny_Scope_Mount, 545x39_7N22 gives 545x39_BP).
  Written per item: "give" (the row), "defaults" for weapons (the parts of the default preset, placed in the
  slots), "loads" for magazines (row, ammo, rounds). Items without a row cannot be given with additem.
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
from build_hideout import package_full  # noqa: E402

OUT = os.path.join(HERE, '..', 'src', 'Resources', 'weapons.json')
SUPPORTED = 18          # property index of the supported items array in a container
CATEGORIES = ('Weapons', 'Attachments', 'Ammunition')
AMMO_SLOTS = {'U_Chamber_C', 'U_Ammunition_C'}
# Display order of the slots (by name, numbers ignored); slots not listed come last, in the game's order
SLOT_ORDER = ['Barrel', 'Handguard', 'Gas Block', 'Gas Tube', 'Muzzle', 'Front Sight', 'Receiver', 'Dust Cover',
              'Rail Mount', 'Sight', 'Canted Sight', 'Rail', 'Grip', 'Tactical', 'Stock', 'Pistol Grip', 'Mag', 'Ammo']
# Slot names where the game's text is unclear (the rail mount of the AKs reuses the dust cover's "Cover")
LABELS = {'U_DustCover_C': 'Dust Cover', 'U_RailMount_AK_C': 'Rail Mount', 'U_Sight_FrontSight_C': 'Front Sight',
          'U_Sight_Canted_C': 'Canted Sight', 'U_Ammunition_C': 'Ammo', 'U_Chamber_C': 'Ammo'}
# Weapon categories (folder under Items/Weapons) -> label, in display order
WEAPON_CLASSES = [('Rifle', 'Assault rifles'), ('DMR', 'DMRs'), ('SniperRifle', 'Sniper rifles'), ('SMG', 'SMGs'),
                  ('Lmg', 'LMGs'), ('Shotgun', 'Shotguns'), ('Pistol', 'Pistols')]


def header(b):
    """Unversioned property header -> ([(property index, is zero)], data offset)."""
    o, frags = 0, []
    while True:
        v = struct.unpack_from('<H', b, o)[0]
        o += 2
        frags.append((v & 0x7f, bool(v & 0x80), v >> 9))
        if v & 0x100:
            break
    # the zero mask has one bit per value of the fragments flagged "has zeroes" only
    idx, maskable, i = [], [], 0
    for skip, has_zero, n in frags:
        i += skip
        idx.extend(range(i, i + n))
        maskable.extend([has_zero] * n)
        i += n
    zero = [False] * len(idx)
    bits = sum(maskable)
    if bits:
        nb = 1 if bits <= 8 else 2 if bits <= 16 else (bits + 31) // 32 * 4
        mask = int.from_bytes(b[o:o + nb], 'little')
        o += nb
        j = 0
        for k, m in enumerate(maskable):
            if m:
                zero[k] = bool(mask >> j & 1)
                j += 1
    return list(zip(idx, zero)), o


class Package:
    def __init__(self, path):
        self.path = path
        self.names, self.imports, self.exports = package_full(path, numbered=True)
        self.uexp = open(os.path.splitext(path)[0] + '.uexp', 'rb').read()

    def data(self, k):
        """Serialized data of export k (1-based)."""
        e = self.exports[k - 1]
        return self.uexp[e['offset']:e['offset'] + e['size']]

    def fname(self, b, o):
        i, num = struct.unpack_from('<ii', b, o)
        if not 0 <= i < len(self.names):
            raise ValueError('bad name index %d in %s' % (i, self.path))
        n = self.names[i]
        return n if num == 0 else '%s_%d' % (n, num - 1)

    def subobject_refs(self, k):
        """Property 18 of export k: export indices of its supported-items subobjects, None if not set."""
        b = self.data(k)
        props, start = header(b)
        found = [z for i, z in props if i == SUPPORTED]
        if not found:
            return None
        if found[0]:
            return []
        if props[-1][0] != SUPPORTED or b[-4:] != b'\0\0\0\0':
            raise ValueError('unexpected container layout, export %d of %s' % (k, self.path))
        # The array is the last property: count, refs, then 4 zero bytes. Find the count that fits and whose
        # refs are all subobjects of this export (that check makes a false match practically impossible).
        end = len(b) - 4
        for c in range(0, 64):
            pos = end - 4 * c - 4
            if pos < start:
                break
            if struct.unpack_from('<i', b, pos)[0] != c:
                continue
            refs = list(struct.unpack_from('<%di' % c, b, pos + 4))
            if all(0 < r <= len(self.exports) and self.exports[r - 1]['outer'] == k for r in refs):
                return refs
        raise ValueError('supported items array not found, export %d of %s' % (k, self.path))

    def tags(self, k):
        """Property 0 (tag list) of a supported-items object, None if not set."""
        b = self.data(k)
        props, o = header(b)
        if not props or props[0][0] != 0:
            return None
        if props[0][1]:
            return []
        n = struct.unpack_from('<i', b, o)[0]
        if not 0 <= n < 1000:
            raise ValueError('bad tag count in export %d of %s' % (k, self.path))
        return [self.fname(b, o + 4 + 8 * j) for j in range(n)]

    def first_text(self, k):
        """Source string of the first localized FText of export k."""
        b = self.data(k)
        m = re.search(rb'\x21\x00\x00\x00[0-9A-F]{32}\x00', b)
        if not m:
            return None
        o = m.end()
        n = struct.unpack_from('<i', b, o)[0]
        if n < 0:
            return b[o + 4:o + 4 - 2 * n].decode('utf-16le').rstrip('\0')
        return b[o + 4:o + 4 + n].decode('latin1').rstrip('\0')


class Classes:
    """Blueprint classes of Objects/ (containers and supported-items lists), by class name."""

    def __init__(self, inv):
        self.files = {}
        for p in glob.glob(os.path.join(inv, 'Objects', '**', '*.uasset'), recursive=True):
            name = os.path.basename(p)[:-7] + '_C'
            if name in self.files:
                raise ValueError('two classes named ' + name)
            self.files[name] = p
        self.cache = {}

    def info(self, cls):
        """(parent class, Package, default object export) of a blueprint class, or None for a native class."""
        if cls not in self.files:
            return None
        if cls not in self.cache:
            pkg = Package(self.files[cls])
            gen = [k for k, e in enumerate(pkg.exports, 1) if e['name'] == cls]
            dflt = [k for k, e in enumerate(pkg.exports, 1) if e['name'] == 'Default__' + cls]
            if len(gen) != 1 or len(dflt) != 1:
                raise ValueError('class layout not understood: ' + pkg.path)
            sup = struct.unpack_from('<i', pkg.data(gen[0]), 6)[0]
            parent = pkg.imports[-sup - 1] if sup < 0 else None
            self.cache[cls] = (parent, pkg, dflt[0])
        return self.cache[cls]

    def is_container(self, cls):
        while cls:
            if cls in ('U_Handguard_C', 'U_Chamber_C') or self.files.get(cls, '').replace('\\', '/').find(
                    '/Objects/ItemContainers/') >= 0:
                return True
            i = self.info(cls)
            cls = i[0] if i else None
        return False

    def class_tags(self, cls):
        """Tag list of a supported-items class (class defaults, inherited)."""
        while cls:
            i = self.info(cls)
            if not i:
                return []
            t = i[1].tags(i[2])
            if t is not None:
                return t
            cls = i[0]
        return []

    def object_tags(self, pkg, k, archetypes):
        """Tags of supported-items subobject k. Without its own tag list, the archetype applies: the
        subobject with the same name in the defaults of the container class (archetypes = that class, then
        its parents), and in the end the defaults of the subobject's own class."""
        t = pkg.tags(k)
        if t is not None:
            return t
        e = pkg.exports[k - 1]
        same = [j for j, x in enumerate(pkg.exports, 1) if x['outer'] == e['outer'] and x['name'] == e['name']]
        nth = same.index(k)
        cls = archetypes
        while cls:
            i = self.info(cls)
            if not i:
                break
            subs = [j for j, x in enumerate(i[1].exports, 1) if x['outer'] == i[2] and x['name'] == e['name']]
            if len(subs) > nth:
                return self.object_tags(i[1], subs[nth], i[0])
            cls = i[0]
        return self.class_tags(e['class'])

    def container_tags(self, cls):
        """Supported tags of a container class (class defaults, inherited)."""
        while cls:
            i = self.info(cls)
            if not i:
                return []
            refs = i[1].subobject_refs(i[2])
            if refs is not None:
                return [t for r in refs for t in self.object_tags(i[1], r, i[0])]
            cls = i[0]
        return []

    def capacity(self, cls):
        """Property 3 (rounds) of an ammunition container class (class defaults, inherited)."""
        while cls:
            i = self.info(cls)
            if not i:
                break
            b = i[1].data(i[2])
            props, o = header(b)
            if props and props[0] == (3, False):
                return struct.unpack_from('<i', b, o)[0]
            cls = i[0]
        return None

    def label(self, cls):
        while cls:
            if cls in LABELS:
                return LABELS[cls]
            i = self.info(cls)
            if not i:
                break
            t = i[1].first_text(i[2])
            if t:
                return t
            cls = i[0]
        return 'Slot'


def vendor_rows(inv):
    """additem row name -> item definition (ID_x) or item preset (DA_x) of Vendor/DT_Vendor_Old, in table order."""
    path = os.path.join(inv, 'Vendor', 'DT_Vendor_Old.uasset')
    pkg = Package(path)
    b = pkg.data(1)
    count = struct.unpack_from('<i', b, 10)[0]
    rows = {}
    # every row struct starts with the same header: (row name) 80 19 <mask> 80 13 <mask> [byte] <object ref>
    for m in re.finditer(rb'\x80\x19(..)\x80\x13(..)', b, re.S):
        row = pkg.fname(b, m.start() - 8)
        o = m.end() + (0 if struct.unpack_from('<H', m.group(2))[0] & 1 else 1)
        ref = struct.unpack_from('<i', b, o)[0]
        if not -len(pkg.imports) <= ref < 0:
            raise ValueError('vendor row %s: unexpected item reference %d' % (row, ref))
        rows[row] = pkg.imports[-ref - 1]
    if len(rows) != count:
        raise ValueError('vendor table: %d rows read of %d' % (len(rows), count))
    return rows


def read_preset(inv, name):
    """Item preset DA_x -> (item definition, [{parent, ref, count}]): parent = index of the element it goes in
    (-1 = the item itself), ref = ID_x or DA_x (a magazine preset in a weapon preset), count (rounds in a mag)."""
    found = glob.glob(os.path.join(inv, 'Presets', '**', name + '.uasset'), recursive=True)
    if len(found) != 1:
        raise ValueError('preset %s: %d files' % (name, len(found)))
    pkg = Package(found[0])
    b = pkg.data(1)
    props, o = header(b)
    if [i for i, _ in props][:2] != [0, 1] or any(z for _, z in props[:2]):
        raise ValueError('preset %s: unexpected layout %s' % (name, props))
    ref = lambda v: pkg.imports[-v - 1] if -len(pkg.imports) <= v < 0 else None
    item = ref(struct.unpack_from('<i', b, o)[0])
    n = struct.unpack_from('<i', b, o + 4)[0]
    o += 8
    elems = []
    for _ in range(n):
        fields, size = header(b[o:])
        o += size
        f = {}
        for i, zero in fields:
            if zero:
                f[i] = 0
            elif i in (0, 1, 3, 4, 5, 7, 8):  # ints and object references
                f[i] = struct.unpack_from('<i', b, o)[0]
                o += 4
            elif i == 2:  # bool
                f[i] = b[o]
                o += 1
            elif i == 6:  # struct of FVector2D (placement in the inventory grid)
                sub, so = header(b[o:])
                o += so + 16 * sum(1 for _, z in sub if not z)
            else:
                raise ValueError('preset %s: unknown field %d' % (name, i))
        r = ref(f.get(4, 0)) or ref(f.get(3, 0))
        if r is None:
            raise ValueError('preset %s: element without item' % name)
        elems.append({'parent': f.get(1, 0), 'ref': r, 'count': f.get(5, 0)})
    return item, elems


def main():
    root = sys.argv[1]
    inv = os.path.join(root, 'Test_C', 'Content', 'Blueprints', 'InventorySystem')
    classes = Classes(inv)
    items, by_tag = {}, {}
    for cat in CATEGORIES:
        for p in sorted(glob.glob(os.path.join(inv, 'Items', cat, '**', 'ID_*.uasset'), recursive=True)):
            pkg = Package(p)
            rel = os.path.relpath(p, os.path.join(inv, 'Items')).replace('\\', '/').split('/')
            row = os.path.basename(p)[3:-7]
            defs = [k for k, e in enumerate(pkg.exports, 1) if e['class'] == 'IRRItemDefinition']
            if len(defs) != 1:
                raise ValueError('no item definition in ' + p)
            d = defs[0]
            b = pkg.data(d)
            tag = None
            for o in range(0, 48):
                i, num = struct.unpack_from('<ii', b, o)
                if 0 <= i < len(pkg.names) and num == 0 and pkg.names[i].startswith('Inventory.Items.'):
                    tag = pkg.names[i]
                    break
            if not tag:
                raise ValueError('item tag not found in ' + p)
            slots, capacity = [], None
            for k, e in enumerate(pkg.exports, 1):
                if e['outer'] != d or not e['class'].startswith('U_') or not classes.is_container(e['class']):
                    continue
                refs = pkg.subobject_refs(k)
                tags = ([t for r in refs for t in classes.object_tags(pkg, r, e['class'])] if refs is not None
                        else classes.container_tags(e['class']))
                slots.append({'class': e['class'], 'label': classes.label(e['class']), 'tags': tags})
                if e['class'] == 'U_Ammunition_C':  # magazine: property 3 = capacity (rounds)
                    props, o = header(pkg.data(k))
                    if props and props[0] == (3, False):
                        capacity = struct.unpack_from('<i', pkg.data(k), o)[0]
                    else:
                        capacity = classes.capacity(e['class'])
            icon = next((n for n in pkg.names if n.startswith('/Game/') and n.endswith('_Icon')), None)
            if tag in by_tag:
                print('WARNING: tag %s used by %s and %s' % (tag, by_tag[tag], row))
            by_tag[tag] = row
            items[row] = {'name': pkg.first_text(d) or row, 'tag': tag, 'cat': rel[1] if cat != 'Ammunition' else
                          'Ammo', 'kind': cat, 'slots': slots, 'capacity': capacity, 'icon': icon}

    # tags -> rows; a tag that is no item but the parent of item tags accepts all its children
    unknown = {}

    def rows(tags):
        out = []
        for t in tags:
            if t in by_tag:
                out.append(by_tag[t])
                continue
            kids = [r for tt, r in by_tag.items() if tt.startswith(t + '.')]
            if not kids:
                unknown[t] = unknown.get(t, 0) + 1
            out.extend(sorted(kids))
        seen = set()
        return [r for r in out if not (r in seen or seen.add(r))]

    out_items = {}
    for row, it in items.items():
        slots, count = [], {}
        for s in it['slots']:
            accepted = rows(s['tags'])
            if s['class'] in AMMO_SLOTS:
                kind = 'ammo'
            else:
                kind = 'part'
            label = s['label']
            count[label] = count.get(label, 0) + 1
            slots.append({'name': label, 'base': label, 'kind': kind, 'items': accepted})
        for s in slots:  # number duplicate slot names (Tactical 1, Tactical 2)
            if count[s['name']] > 1:
                n = count.setdefault('#' + s['name'], 0) + 1
                count['#' + s['name']] = n
                s['name'] = '%s %d' % (s['name'], n)
        rank = lambda s: SLOT_ORDER.index(s['base']) if s['base'] in SLOT_ORDER else len(SLOT_ORDER)
        slots = [s for _, s in sorted(enumerate(slots), key=lambda p: (rank(p[1]), p[0]))]
        for s in slots:
            del s['base']
        o = {'name': it['name'], 'cat': it['cat']}
        if it['capacity']:
            o['capacity'] = it['capacity']
        if it['icon']:  # texture of the icon (tools/build_icons.py)
            o['icon'] = it['icon']
        if slots:
            o['slots'] = slots
        out_items[row] = o

    # ---- additem rows, weapon default presets, loaded magazines
    vrows = vendor_rows(inv)
    presets = {t: read_preset(inv, t) for t in set(vrows.values()) if t.startswith('DA_')}
    gives = {}  # item -> rows giving it, in table order
    for r, t in vrows.items():
        target = t if t.startswith('ID_') else presets[t][0]
        if target and target.startswith('ID_'):
            gives.setdefault(target[3:], []).append(r)
    for row, o in out_items.items():
        g = gives.get(row, [])
        if g:
            o['give'] = row if row in g else g[0]
        if items[row]['cat'] == 'Mag':
            loads = []
            for r in g:
                t = vrows[r]
                ammo = [e for e in presets[t][1] if e['ref'].startswith('ID_')] if t.startswith('DA_') else []
                loads.append({'give': r, 'ammo': ammo[0]['ref'][3:] if ammo else None,
                              'rounds': ammo[0]['count'] if ammo else 0})
            if loads:
                o['loads'] = loads
    no_row = sorted(r for r in out_items if 'give' not in out_items[r])
    for w in (r for r, it in items.items() if it['kind'] == 'Weapons'):
        row = out_items[w].get('give')
        t = vrows.get(row) if row else None
        if not t or not t.startswith('DA_'):
            continue
        _, elems = presets[t]
        # element index -> (item, parts dict); an element can go in an element listed after it
        nodes = {}
        tree = {}
        todo = list(range(len(elems)))
        while todo:
            left = []
            for k in todo:
                e = elems[k]
                ref = e['ref']
                item = presets[ref][0][3:] if ref.startswith('DA_') else ref[3:]
                if e['parent'] == -1:
                    parent_item, parts = w, tree
                elif nodes.get(e['parent']):
                    parent_item, parts = nodes[e['parent']]
                elif e['parent'] in todo and e['parent'] != k:
                    left.append(k)
                    continue
                else:
                    print('WARNING: %s default preset: the part %s goes in a part that is not there' % (w, item))
                    nodes[k] = None
                    continue
                slot = next((s['name'] for s in out_items.get(parent_item, {}).get('slots', [])
                             if s['kind'] == 'part' and item in s['items'] and s['name'] not in parts), None)
                if slot is None:
                    print('WARNING: %s default preset: no slot of %s takes %s (left out)' % (w, parent_item, item))
                    nodes[k] = None
                    continue
                node = {'item': item}
                parts[slot] = node
                nodes[k] = (item, node.setdefault('parts', {}))
            if len(left) == len(todo):
                print('WARNING: %s default preset: parts in a loop: %s' % (w, left))
                break
            todo = left

        def clean(d):
            for v in d.values():
                if not v.get('parts'):
                    v.pop('parts', None)
                else:
                    clean(v['parts'])
        clean(tree)
        out_items[w]['defaults'] = tree

    order = {c: i for i, (c, _) in enumerate(WEAPON_CLASSES)}
    weapons = sorted((r for r, it in items.items() if it['kind'] == 'Weapons'),
                     key=lambda r: (order.get(items[r]['cat'], 99), items[r]['name'].lower()))
    # keep only what a weapon can reach
    reach, todo = set(), list(weapons)
    while todo:
        r = todo.pop()
        if r in reach:
            continue
        reach.add(r)
        for s in out_items[r].get('slots', []):
            todo.extend(s['items'])
    data = {'version': 1,
            'classes': [{'id': c, 'name': n} for c, n in WEAPON_CLASSES],
            'weapons': weapons,
            'items': {r: out_items[r] for r in sorted(reach, key=str.lower)}}
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, indent=1, ensure_ascii=False)
        f.write('\n')
    parts = [r for r in reach if items[r]['kind'] == 'Attachments']
    print('%d weapons, %d attachments, %d ammo -> %s' % (len(weapons), len(parts),
                                                         len([r for r in reach if items[r]['kind'] == 'Ammunition']),
                                                         os.path.normpath(OUT)))
    unused = sorted(r for r, it in items.items() if it['kind'] == 'Attachments' and r not in reach)
    if unused:
        print('attachments no weapon accepts (%d): %s' % (len(unused), ', '.join(unused)))
    if unknown:
        print('tags matching no item (%d): %s' % (len(unknown), ', '.join(sorted(unknown))))
    no_row = [r for r in sorted(reach, key=str.lower) if 'give' not in out_items[r]]
    if no_row:
        print('items additem cannot give (no vendor row) (%d): %s' % (len(no_row), ', '.join(no_row)))
    renamed = [(r, out_items[r]['give']) for r in sorted(reach, key=str.lower) if out_items[r].get('give') not in (None, r)]
    print('%d items have another additem name (e.g. %s)' % (len(renamed), ', '.join('%s -> %s' % x for x in renamed[:4])))
    print('%d weapons with default parts' % sum(1 for w in weapons if out_items[w].get('defaults')))


if __name__ == '__main__':
    main()
