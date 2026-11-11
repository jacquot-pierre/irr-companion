r"""Builds the data of the MAPS tab: resource spawn points, extraction points, minimap images and bounds.

Usage:
  python -I tools/build_maps.py "<game>\Test_C\Content\Paks" [path\to\repak.exe]

Writes src/Resources/maps.json and src/Resources/maps/*.jpg (2048 px minimaps).
Needs about 1.5 GB of free space in %TEMP% while it runs (the persistent levels are big) and a few minutes.

Where the data comes from (cooked with unversioned properties):
- Resource points: BP_ResourceActor_<Type>_C actors of the persistent level of each map. Their position is the
  RelativeLocation of their StaticMeshComponent (first serialized value, property index 161). The spawn chance
  ("Random Spawn Chance", a double) and the amount (int) are class defaults of
  Environment/Blueprints/Gameplay/Resources/BP_ResourceActor_<Type>; the script also checks that no placed actor
  overrides a property of the resource blueprint itself.
- Extraction points: BP_IRRExtractionZone_C actors (RelativeLocation of their SceneComponent, index 5).
- Player spawn zones (the spawns offered when deploying): BP_IRRSpawnZone_C actors (a child class of the extraction
  zone, with 4 PlayerStart child actors around it). Position = RelativeLocation of DefaultSceneRoot (index 5); name
  = the first actor tag (Tags = [<name>, AutoBlend_HasPrimitiveData]), e.g. "Train Track" on Delta.
  The names offered in game ("INFILL LOCATION" of the deploy tab) are the InfillLocations (TArray<FName>) of the map
  setup Blueprints/MissionSystem/DataAssets/Maps/MD_<Map>; the game picks the zone whose tag is that name. Zones whose
  tag differs from every infill name (typo: "Tunnelystem"; placeholder: "SpawnZoneD") get the closest name, or the
  one given in SPAWN_GUESSES (shown as a guess).
- Minimaps: each BP_IRRMiniMapVolume_C actor gives a centre (SceneComponent RelativeLocation), a half size
  (BoxComponent BoxExtent, index 0) and its texture T_LVL_<Map>_Minimap_*. Several volumes = several floors
  (Bunker). Image orientation: world +X is up, +Y is right (checked against the buildings and the quarry).
"""
import glob
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_hideout import package_full  # noqa: E402
from build_icons import read_texture, repak_list, extract, _c565  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, '..'))
OUT_JSON = os.path.join(ROOT, 'src', 'Resources', 'maps.json')
OUT_IMG = os.path.join(ROOT, 'src', 'Resources', 'maps')
IMAGE_SIZE = 2048
# Maps whose minimaps are grey-scale plans (white = nothing, dark = rooms): drawn inverted on a dark palette.
PLAN_MAPS = {'Bunker'}
PLAN_BACK, PLAN_FRONT = (20, 18, 16), (214, 204, 182)
JPEG_QUALITY = 85

# Display name -> persistent level (without extension)
MAPS = [
    ('Delta', 'Test_C/Content/Maps/Gameplay/LVL_Delta'),
    ('Quarry', 'Test_C/Content/Maps/Gameplay/LVL_Quarry'),
    ('Outskirts', 'Test_C/Content/Maps/Gameplay/LVL_Outskirts'),
    ('Bunker', 'Test_C/Content/Maps/Testing/LVL_Bunker'),
]
RESOURCE_BP = 'Test_C/Content/Environment/Blueprints/Gameplay/Resources/'
RESOURCE_BASE = 'Test_C/Content/Blueprints/ResourceSystem/Actor/BP_ResourceActor'
MINIMAPS = 'Test_C/Content/Blueprints/MissionSystem/Textures/Minimaps/'
RESOURCES = ['Concrete', 'Food', 'Metal', 'Oil']
MAP_MARGIN = 200  # MapMargins default of BP_IRRMiniMapVolume
MAP_SETUPS = 'Test_C/Content/Blueprints/MissionSystem/DataAssets/Maps/'
# Spawn zones whose tag matches no infill name and cannot be told apart by the tag: (map, rounded x, y) -> name,
# from the place on the minimap. Quarry: two zones are tagged "SpawnZoneD"; the one on the river bank is Riverside.
SPAWN_GUESSES = {
    ('Quarry', -90078, -47848): 'Riverside',
    ('Quarry', -53408, -79037): 'Warehouse',
}
KEYS_DIR = 'Test_C/Content/Blueprints/InventorySystem/Items/Keys/'
KEY_TAG = 'Inventory.Items.Keys.'
# Locked things shown on the maps: class -> kind. A door's key is the one set on the placed actor, else the one of its
# class (BP_Door_Delta_Locked / BP_Door_Quarry_Locked hold "<Map>.General Key" in their class defaults); doors
# with neither never open and are left out.
DOORS_DIR = 'Test_C/Content/Blueprints/InteractionSystem/Actors/Door/'
LOCKS = {
    'BP_LootContainer_Safe_01_C': 'CompactSafe',
    'BP_LootContainer_Safe_02_C': 'LargeSafe',
    'BP_Door_Electric_C': 'HasDoor',
}
LOCKED_DOOR = 'LockedDoor'  # any other BP_Door_*Locked*_C

# first serialized property index expected for each location (see the docstring)
LOCATION_INDEX = {'StaticMeshComponent': 161, 'SceneComponent': 5}
BOX_EXTENT_INDEX = 0


def unversioned(b):
    """Serialized property indices and the offset of the first value of an unversioned export."""
    o, cur, idx, zero_values = 0, 0, [], 0
    while True:
        v = struct.unpack_from('<H', b, o)[0]
        o += 2
        skip, zeros, last, num = v & 0x7f, bool(v & 0x80), bool(v & 0x100), v >> 9
        cur += skip
        idx += list(range(cur, cur + num))
        cur += num
        if zeros:
            zero_values += num
        if last:
            break
    if zero_values:
        o += 1 if zero_values <= 8 else (2 if zero_values <= 16 else 4 * ((zero_values + 31) // 32))
    return idx, o


class Level:
    def __init__(self, umap):
        self.names, self.imports, self.exports = package_full(umap)
        self.f = open(os.path.splitext(umap)[0] + '.uexp', 'rb')

    def blob(self, e):
        self.f.seek(e['offset'])
        return self.f.read(e['size'])

    def children(self, index):
        return [e for e in self.exports if e['outer'] == index]

    def first_vector(self, comp, expected_index):
        b = self.blob(comp)
        idx, o = unversioned(b)
        if not idx or idx[0] != expected_index:
            raise ValueError('%s: first property %s, expected %d' % (comp['class'], idx[:1], expected_index))
        return struct.unpack_from('<ddd', b, o)


def actor_tags(L, b):
    """Actor Tags (TArray<FName>) of an actor blob: found as <count> <names...> ending with the engine tag
    AutoBlend_HasPrimitiveData, which every placed actor of these levels has."""
    try:
        auto = L.names.index('AutoBlend_HasPrimitiveData')
    except ValueError:
        return []
    for k in range(0, len(b) - 7):
        if struct.unpack_from('<ii', b, k) != (auto, 0):
            continue
        for n in range(1, 9):  # walk back over n-1 more names to the array count
            c = k - 8 * (n - 1) - 4
            if c < 0:
                break
            if struct.unpack_from('<i', b, c)[0] == n:
                tags = [L.names[struct.unpack_from('<i', b, c + 4 + 8 * j)[0]] for j in range(n)]
                return [t for t in tags if t != 'AutoBlend_HasPrimitiveData']
    return []


def infill_names(tmp, name):
    """InfillLocations of MD_<name> (the spawn names of the deploy tab, in game order), or []."""
    path = os.path.join(tmp, (MAP_SETUPS + 'MD_%s.uasset' % name).replace('/', os.sep))
    if not os.path.exists(path):
        return []
    names = package_full(path)[0]
    d = open(path[:-7] + '.uexp', 'rb').read()

    def plain(i):
        if not 0 <= i < len(names):
            return False
        n = names[i]
        return '/' not in n and not n.startswith(('Resource.', 'Mission.', 'Maps.', 'LVL_')) and n != 'None'
    found = []
    for k in range(0, len(d) - 12):
        n = struct.unpack_from('<i', d, k)[0]
        if not 2 <= n <= 12 or k + 4 + 8 * n > len(d):
            continue
        pairs = [struct.unpack_from('<ii', d, k + 4 + 8 * j) for j in range(n)]
        if all(num == 0 and plain(i) for i, num in pairs):
            found.append([names[i] for i, _ in pairs])
    return found[0] if len(found) == 1 else []


def name_spawns(map_name, spawns, infill, problems):
    """Gives each spawn zone its in-game infill name: same tag, else a close spelling, else SPAWN_GUESSES."""
    import difflib
    left = list(infill)
    for sp in spawns:
        if sp['tag'] in left:
            sp['name'] = sp['tag']
            left.remove(sp['tag'])
    for sp in spawns:
        if 'name' in sp:
            continue
        close = difflib.get_close_matches(sp['tag'], left, 1, 0.8)
        if close:
            sp['name'] = close[0]
            left.remove(close[0])
    for sp in spawns:
        if 'name' in sp:
            continue
        g = SPAWN_GUESSES.get((map_name, sp['x'], sp['y']))
        if g and g in left:
            sp['name'] = g
            sp['guessed'] = True
            left.remove(g)
        else:
            sp['name'] = sp['tag']
            problems.append('%s: spawn zone "%s" at %d, %d matches no infill name %s' % (map_name, sp['tag'], sp['x'], sp['y'], left))
    if left:
        problems.append('%s: infill names without a spawn zone: %s' % (map_name, left))
    order = {n: i for i, n in enumerate(infill)}
    spawns.sort(key=lambda sp: order.get(sp['name'], len(order)))


def resource_defaults(root):
    """Type -> spawn chance and amount, read from the class defaults of each resource blueprint."""
    out = {}
    for t in RESOURCES:
        path = os.path.join(root, (RESOURCE_BP + 'BP_ResourceActor_' + t + '.uasset').replace('/', os.sep))
        names, imports, exports = package_full(path)
        cdo = [e for e in exports if e['name'].startswith('Default__')]
        data = open(path[:-6] + 'uexp', 'rb').read()
        b = data[cdo[0]['offset']:cdo[0]['offset'] + cdo[0]['size']]
        found = None
        # ... <double chance> 00 03 <FName Resource.X> <int amount> ...
        for o in range(8, len(b) - 14):
            if b[o:o + 2] != b'\x00\x03':
                continue
            ni, nn = struct.unpack_from('<ii', b, o + 2)
            if 0 <= ni < len(names) and nn == 0 and names[ni] == 'Resource.' + t:
                chance = struct.unpack_from('<d', b, o - 8)[0]
                amount = struct.unpack_from('<i', b, o + 10)[0]
                if 0 < chance <= 1 and 0 < amount < 1000000:
                    found = (chance, amount)
        if not found:
            raise ValueError('spawn chance / amount not found in ' + path)
        out[t] = {'chance': round(found[0], 4), 'amount': found[1]}
    return out


def blocks_rgb(pf, w, h, data, size):
    """Decodes a DXT1/DXT5 texture directly at a lower resolution (average of 2x2 or 4x4 pixel groups)."""
    alpha = pf == 'PF_DXT5'
    if pf not in ('PF_DXT1', 'PF_DXT5'):
        raise ValueError('minimap format %s not handled' % pf)
    bs = 16 if alpha else 8
    step = w // size          # 2 -> 2x2 output pixels per block, 4 -> 1
    sub = 4 // step
    W, H = (w // 4) * sub, (h // 4) * sub
    out = bytearray(W * H * 3)
    p = 0
    for by in range(h // 4):
        for bx in range(w // 4):
            cb = data[p + 8:p + 16] if alpha else data[p:p + 8]
            p += bs
            c0, c1, bits = struct.unpack('<HHI', cb)
            p0, p1 = _c565(c0), _c565(c1)
            if c0 > c1 or alpha:
                cols = [p0, p1, tuple((2 * x + y) // 3 for x, y in zip(p0, p1)), tuple((x + 2 * y) // 3 for x, y in zip(p0, p1))]
            else:
                cols = [p0, p1, tuple((x + y) // 2 for x, y in zip(p0, p1)), (0, 0, 0)]
            for sy in range(sub):
                for sx in range(sub):
                    r = g = b = 0
                    for yy in range(step):
                        for xx in range(step):
                            c = cols[(bits >> (2 * ((sy * step + yy) * 4 + sx * step + xx))) & 3]
                            r += c[0]
                            g += c[1]
                            b += c[2]
                    n = step * step
                    q = ((by * sub + sy) * W + bx * sub + sx) * 3
                    out[q:q + 3] = bytes((r // n, g // n, b // n))
    return W, H, bytes(out)


def plan_palette(rgb):
    """Grey-scale plan -> dark background with light rooms."""
    lut = []
    for g in range(256):
        t = 1 - g / 255.0
        lut.append(bytes(int(PLAN_BACK[k] + t * (PLAN_FRONT[k] - PLAN_BACK[k])) for k in range(3)))
    out = bytearray(len(rgb))
    for q in range(0, len(rgb), 3):
        g = (rgb[q] + rgb[q + 1] + rgb[q + 2]) // 3
        out[q:q + 3] = lut[g]
    return bytes(out)


def save_jpeg(rgb, w, h, path):
    """PNG in a temp file, converted to JPEG with System.Drawing (Windows)."""
    png = path[:-4] + '.tmp.png'
    raw = b''.join(b'\x00' + rgb[y * w * 3:(y + 1) * w * 3] for y in range(h))

    def chunk(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t + b) & 0xffffffff)
    with open(png, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))
    ps = ("Add-Type -AssemblyName System.Drawing; $i=[System.Drawing.Image]::FromFile('%s'); "
          "$c=[System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders()|?{$_.MimeType -eq 'image/jpeg'}; "
          "$p=New-Object System.Drawing.Imaging.EncoderParameters 1; "
          "$p.Param[0]=New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality,[long]%d); "
          "$i.Save('%s',$c,$p); $i.Dispose()") % (png, JPEG_QUALITY, path)
    subprocess.run(['powershell', '-NoProfile', '-NonInteractive', '-Command', ps], check=True)
    os.remove(png)


def key_items(root):
    """Key gameplay tag -> {id, name} from the key item definitions (ID_*.uasset in Items/Keys)."""
    import re
    keys = {}
    for path in glob.glob(os.path.join(root, KEYS_DIR.replace('/', os.sep), '**', 'ID_*.uasset'), recursive=True):
        names, _, _ = package_full(path)
        tags = [n for n in names if n.startswith(KEY_TAG)]
        if not tags:
            continue
        data = open(path[:-6] + 'uexp', 'rb').read()
        texts = [m.decode() for m in re.findall(rb'[\x20-\x7e]{6,}', data) if b' ' in m]
        label = texts[0] if texts else tags[0][len(KEY_TAG):]
        if label.startswith('Key for the '):  # HAS keys: "Key for the HAS-0410-A building."
            label = label[len('Key for the '):].replace(' building.', '') + ' key'
        keys[tags[0]] = {'id': os.path.basename(path)[3:-7], 'name': label}
    return keys


def actor_keys(L, index, key_names):
    """Key tags referenced by an actor or its components."""
    found = set()
    for e in [L.exports[index - 1]] + L.children(index):
        if e['size'] > 65536:
            continue
        b = L.blob(e)
        for o in range(len(b) - 7):
            ni, nn = struct.unpack_from('<ii', b, o)
            if nn == 0 and ni in key_names:
                found.add(key_names[ni])
    return sorted(found)


def class_keys(tmp):
    """Door class (BP_Door_X_C) -> key tags of its class defaults (Blueprints/InteractionSystem/Actors/Door)."""
    out = {}
    for path in glob.glob(os.path.join(tmp, DOORS_DIR.replace('/', os.sep), '**', 'BP_Door_*.uasset'), recursive=True):
        names = package_full(path)[0]
        ks = sorted(n for n in names if n.startswith(KEY_TAG))
        if ks:
            out[os.path.basename(path)[:-7] + '_C'] = ks
    return out


def floor_of(z, floors):
    inside = [f for f in floors if abs(z - f['centerZ']) <= f['extentZ'] + 50]
    pool = inside or floors
    return min(pool, key=lambda f: abs(z - f['centerZ']))['index']


def main():
    paks = sys.argv[1]
    repak = sys.argv[2] if len(sys.argv) > 2 else (shutil.which('repak') or r'C:\Program Files\repak_cli\bin\repak.exe')
    print('listing the paks...')
    files = repak_list(repak, paks)
    tmp = tempfile.mkdtemp(prefix='irr_maps_')
    problems = []
    try:
        wanted = [RESOURCE_BP + 'BP_ResourceActor_%s.%s' % (t, ext) for t in RESOURCES for ext in ('uasset', 'uexp')]
        wanted += [f for f in files if f.startswith(MINIMAPS) and '_Mask' not in f]
        wanted += [f for f in files if f.startswith(KEYS_DIR) and '/ID_' in f]
        wanted += [f for f in files if f.startswith(DOORS_DIR) and '/BP_Door_' in f]
        wanted += [MAP_SETUPS + 'MD_%s.%s' % (n, ext) for n, _ in MAPS for ext in ('uasset', 'uexp')]
        for _, lvl in MAPS:
            wanted += [lvl + '.umap', lvl + '.uexp']
        print('extracting %d files (the levels are big, this takes a while)...' % len(wanted))
        extract(repak, files, wanted, tmp)
        defaults = resource_defaults(tmp)
        print('resource defaults:', defaults)
        keys = key_items(tmp)
        door_keys = class_keys(tmp)
        print('door classes with a key:', door_keys)
        print('%d key items' % len(keys))
        os.makedirs(OUT_IMG, exist_ok=True)
        old_images = {f for f in os.listdir(OUT_IMG) if f.endswith('.jpg')}
        images_done = {}
        maps_out = []
        for name, lvl in MAPS:
            umap = os.path.join(tmp, (lvl + '.umap').replace('/', os.sep))
            if not os.path.exists(umap):
                problems.append('%s: level %s not found' % (name, lvl))
                continue
            L = Level(umap)
            # floors (minimap volumes)
            floors = []
            for i, e in enumerate(L.exports, 1):
                if e['class'] != 'BP_IRRMiniMapVolume_C':
                    continue
                comps = {c['class']: c for c in L.children(i)}
                cx, cy, cz = L.first_vector(comps['SceneComponent'], LOCATION_INDEX['SceneComponent'])
                ex, ey, ez = L.first_vector(comps['BoxComponent'], BOX_EXTENT_INDEX)
                b = L.blob(e)
                tex = None
                for o in range(len(b) - 3):
                    v = struct.unpack_from('<i', b, o)[0]
                    if -len(L.imports) <= v < 0:
                        n = L.imports[-v - 1]
                        if n.startswith('T_LVL_') and '_Mask' not in n:
                            tex = n
                            break
                if not tex:
                    problems.append('%s: minimap volume without texture' % name)
                    continue
                # The PDA map (W_GCPS_Map / MPC_Minimap) uses MapCenter = volume location and MapSize. MapSize is a
                # float of the volume; when it is not set, the box is MapSize / 2 - MapMargins (200).
                box = max(ex, ey)
                half = box + MAP_MARGIN
                for o in range(len(b) - 3):
                    fv = struct.unpack_from('<f', b, o)[0]
                    if 2 * box < fv <= 4 * box + 2000 and abs(fv - round(fv)) < 1e-3:
                        half = fv / 2
                        break
                floors.append({'texture': tex, 'centerX': cx, 'centerY': cy, 'centerZ': cz, 'extent': half, 'extentZ': ez})
            floors.sort(key=lambda f: -f['centerZ'])
            for k, f in enumerate(floors):
                f['index'] = k
                f['name'] = name if len(floors) == 1 else 'Floor %d' % (k + 1)
            # images
            for f in floors:
                if f['texture'] not in images_done:
                    uexp = os.path.join(tmp, (MINIMAPS + f['texture'] + '.uexp').replace('/', os.sep))
                    pf, w, h, data = read_texture(uexp)
                    W, H, rgb = blocks_rgb(pf, w, h, data, IMAGE_SIZE)
                    if name in PLAN_MAPS:
                        rgb = plan_palette(rgb)
                    fname = '%s_%d.jpg' % (name.lower(), len([t for t in images_done if t.startswith('T_LVL_' + name)]) + 1)
                    save_jpeg(rgb, W, H, os.path.join(OUT_IMG, fname))
                    images_done[f['texture']] = fname
                    print('  %s: %s -> %s (%s %dx%d)' % (name, f['texture'][:40], fname, pf, W, H))
                f['image'] = images_done[f['texture']]
            # points
            points, extractions, locks, spawns = [], [], [], []
            key_names = {i: n for i, n in enumerate(L.names) if n.startswith(KEY_TAG)}
            for i, e in enumerate(L.exports, 1):
                cls = e['class']
                kind = LOCKS.get(cls) or (LOCKED_DOOR if cls.startswith('BP_Door_') and 'Locked' in cls else None)
                if kind:
                    ks = actor_keys(L, i, key_names)
                    if not ks and kind == LOCKED_DOOR:
                        ks = door_keys.get(cls, [])  # the key of the door class
                    if not ks:
                        continue  # a door that never opens (no key)
                    if len(ks) > 1:
                        problems.append('%s: %s references several keys %s' % (name, cls, ks))
                    unknown = [k for k in ks if k not in keys]
                    if unknown:
                        problems.append('%s: key without item definition %s' % (name, unknown))
                    root = [c for c in L.children(i) if c['class'] == 'SceneComponent' and c['name'] == 'DefaultSceneRoot']
                    try:
                        x, y, z = L.first_vector(root[0], LOCATION_INDEX['SceneComponent'])
                    except Exception as ex:  # noqa: BLE001
                        problems.append('%s: %s skipped (%s)' % (name, cls, ex))
                        continue
                    locks.append({'kind': kind, 'key': ks[0], 'x': round(x), 'y': round(y), 'z': round(z), 'floor': floor_of(z, floors)})
                    continue
                if cls.startswith('BP_ResourceActor_') and cls.endswith('_C'):
                    t = cls[len('BP_ResourceActor_'):-2]
                    if t not in defaults:
                        problems.append('%s: unknown resource type %s' % (name, t))
                        continue
                    # placed actors must not override the spawn chance / amount (properties of the resource blueprint
                    # come after the ~90 engine actor properties)
                    idx, _ = unversioned(L.blob(e))
                    if any(k >= 100 for k in idx):
                        problems.append('%s: a %s point overrides a blueprint property %s: check its chance' % (name, t, idx))
                    sm = [c for c in L.children(i) if c['class'] == 'StaticMeshComponent']
                    try:
                        x, y, z = L.first_vector(sm[0], LOCATION_INDEX['StaticMeshComponent'])
                    except Exception as ex:  # noqa: BLE001
                        problems.append('%s: %s point skipped (%s)' % (name, t, ex))
                        continue
                    points.append({'type': t, 'x': round(x), 'y': round(y), 'z': round(z), 'floor': floor_of(z, floors)})
                elif cls == 'BP_IRRSpawnZone_C':
                    root = [c for c in L.children(i) if c['class'] == 'SceneComponent' and c['name'] == 'DefaultSceneRoot']
                    try:
                        x, y, z = L.first_vector(root[0], LOCATION_INDEX['SceneComponent'])
                    except Exception as ex:  # noqa: BLE001
                        problems.append('%s: spawn zone skipped (%s)' % (name, ex))
                        continue
                    tags = actor_tags(L, L.blob(e))
                    if not tags:
                        problems.append('%s: spawn zone without a name tag at %d, %d' % (name, x, y))
                    spawns.append({'tag': tags[0] if tags else '', 'x': round(x), 'y': round(y), 'z': round(z), 'floor': floor_of(z, floors)})
                elif cls == 'BP_IRRExtractionZone_C':
                    sc = [c for c in L.children(i) if c['class'] == 'SceneComponent']
                    try:
                        x, y, z = L.first_vector(sc[0], LOCATION_INDEX['SceneComponent'])
                    except Exception as ex:  # noqa: BLE001
                        problems.append('%s: extraction skipped (%s)' % (name, ex))
                        continue
                    extractions.append({'x': round(x), 'y': round(y), 'z': round(z), 'floor': floor_of(z, floors)})
            L.f.close()
            name_spawns(name, spawns, infill_names(tmp, name), problems)
            counts = {t: sum(1 for p in points if p['type'] == t) for t in RESOURCES}
            print('%-10s %d floor(s), %d resource points %s, %d extraction(s)' % (name, len(floors), len(points), counts, len(extractions)))
            print('           spawns %s' % ', '.join('%s%s (tag %s, %d, %d, floor %d)' % (p['name'], ' [guess]' if p.get('guessed') else '', p['tag'], p['x'], p['y'], p['floor']) for p in spawns))
            lk = {}
            for l in locks:
                lk[l['kind']] = lk.get(l['kind'], 0) + 1
            print('           locks %s' % lk)
            maps_out.append({
                'name': name,
                'floors': [{'name': f['name'], 'image': f['image'], 'centerX': round(f['centerX']), 'centerY': round(f['centerY']),
                            'extent': round(f['extent']), 'centerZ': round(f['centerZ']), 'extentZ': round(f['extentZ'])} for f in floors],
                'resources': points,
                'extractions': extractions,
                'spawns': spawns,
                'locks': locks,
            })
        for f in sorted(old_images - set(images_done.values())):
            os.remove(os.path.join(OUT_IMG, f))
            print('removed', f)
        doc = {'resources': [{'type': t, 'chance': defaults[t]['chance'], 'amount': defaults[t]['amount']} for t in RESOURCES],
               'keys': [{'tag': t, 'id': k['id'], 'name': k['name']} for t, k in sorted(keys.items())],
               'maps': maps_out}
        with open(OUT_JSON, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(doc, f, indent=1)
        size = sum(os.path.getsize(os.path.join(OUT_IMG, f)) for f in os.listdir(OUT_IMG))
        print('wrote %s and %d images (%d KB)' % (OUT_JSON, len(images_done), size // 1024))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    for p in problems:
        print('WARNING:', p)


if __name__ == '__main__':
    main()
