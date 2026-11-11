r"""Extracts the game icons the app shows and writes them as small PNGs to src/Resources/icons/.

Usage:
  python -I tools/build_icons.py "<game>\Test_C\Content\Paks" [path\to\repak.exe]

Which icons: every item of src/Resources/hideout.json (run tools/build_hideout.py first), the keys of
src/Resources/maps.json (run tools/build_maps.py first), every weapon, attachment and ammo of
src/Resources/weapons.json (run tools/build_weapons.py first; weapons in BIG x BIG), every item of
src/Resources/traders.json (run tools/build_traders.py first), cash, the four
materials, plus the item names listed in EXTRA_ITEMS below. Icons are added little by little: add an item
definition name (as in tools/item_paths.txt, without ID_) to EXTRA_ITEMS to ship its icon.

How: an item's icon is the `*_Icon` texture found in the folder of its item definition (ID_*.uasset); the
material icons are the textures of Blueprints/ResourceSystem/Textures. The textures are cooked as DXT1/DXT5
(or B8G8R8A8); the script decodes the largest mip, scales it down to SIZE x SIZE and writes a PNG.
Nothing else than the listed icons is extracted.

The icons are artwork of the game's developers: see the notice in README.md / LICENSE.
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
ROOT = os.path.normpath(os.path.join(HERE, '..'))
OUT = os.path.join(ROOT, 'src', 'Resources', 'icons')
HIDEOUT = os.path.join(ROOT, 'src', 'Resources', 'hideout.json')
SIZE = 64
BIG = 128  # weapon icons (WEAPONS tab)

# Extra item icons to ship (item definition names, e.g. 'Bandage'), beyond the hideout items.
EXTRA_ITEMS = []
# Material -> texture name in Blueprints/ResourceSystem/Textures
MATERIALS = {'Concrete': 'concrete__1_', 'Food': 'food', 'Metal': 'metal__1_', 'Oil': 'oil'}
RESOURCE_TEX = 'Test_C/Content/Blueprints/ResourceSystem/Textures/'
# Generic silhouettes of the game (inventory slots), used by the WEAPONS tab for items without an icon of their
# own and for empty slots: cat_<weapon class or part folder>.png
CAT_TEX = 'Test_C/Content/Blueprints/InventorySystem/Textures/'
CATEGORIES = {
    'Rifle': 'ItemCategories/Weapon_Types/AR-Icon', 'Lmg': 'ItemCategories/Weapon_Types/AR-Icon',
    'DMR': 'ItemCategories/Weapon_Types/DMR-Icon', 'SniperRifle': 'ItemCategories/Weapon_Types/Sniper-Icon',
    'SMG': 'ItemCategories/Weapon_Types/SMG-Icon', 'Shotgun': 'ItemCategories/Weapon_Types/Shotgun-Icon',
    'Pistol': 'ItemCategories/Weapon_Types/Pistol-Icon',
    'Barrel': 'Attachments/T_Barrel', 'GasTube': 'Attachments/T_Gastube', 'Foregrip': 'Attachments/T_GripIcon',
    'Handguard': 'Attachments/T_Handguard', 'Tactical': 'Attachments/T_LightAttachment', 'Mag': 'Attachments/T_MagIcon',
    'Muzzle': 'Attachments/T_Muzzle', 'PistolGrip': 'Attachments/T_PistolGrip', 'Rail': 'Attachments/T_Rail',
    'Sight': 'Attachments/T_Scope', 'Stock': 'Attachments/T_Stock', 'DustCover': 'Attachments/DustCover-Icon',
    'Receiver': 'Attachments/T_Body',
}


# ------------------------------------------------------------------ texture decoding
def read_texture(uexp):
    """(pixel format, width, height, bytes of the largest mip) of a cooked Texture2D."""
    d = open(uexp, 'rb').read()
    ub = uexp[:-4] + 'ubulk'
    bulk = open(ub, 'rb').read() if os.path.exists(ub) else b''
    i = d.find(b'PF_')
    if i < 16:
        raise ValueError('no pixel format in ' + uexp)
    n = struct.unpack_from('<i', d, i - 4)[0]
    pf = d[i:i + n - 1].decode()
    w, h = struct.unpack_from('<ii', d, i - 16)
    bpp = {'PF_DXT1': 0.5, 'PF_DXT5': 1, 'PF_BC7': 1, 'PF_B8G8R8A8': 4, 'PF_FloatRGBA': 8}.get(pf)
    if bpp is None:
        raise ValueError('pixel format %s not handled (%s)' % (pf, uexp))
    size = int(w * h * bpp)
    if len(bulk) >= size:  # big mips stored in the .ubulk, largest first
        return pf, w, h, bulk[:size]
    end = d.rfind(struct.pack('<iii', w, h, 1))  # inline mip: data is followed by its size
    if end < size:
        raise ValueError('mip data not found in ' + uexp)
    return pf, w, h, d[end - size:end]


def _c565(c):
    return ((c >> 11 & 31) * 255 // 31, (c >> 5 & 63) * 255 // 63, (c & 31) * 255 // 31)


# BC7 modes handled: (subsets, partition bits, rotation bits, index selection bit, colour bits, alpha bits,
# endpoint p-bits, shared p-bits, index bits, second index bits). Modes 0 and 2 (3 subsets) are not handled:
# the script says so (the game's silhouettes use modes 4-7 today).
BC7_MODES = {1: (2, 6, 0, 0, 6, 0, 0, 1, 3, 0), 3: (2, 6, 0, 0, 7, 0, 1, 0, 2, 0), 4: (1, 0, 2, 1, 5, 6, 0, 0, 2, 3),
             5: (1, 0, 2, 0, 7, 8, 0, 0, 2, 2), 6: (1, 0, 0, 0, 7, 7, 1, 0, 4, 0), 7: (2, 6, 0, 0, 5, 5, 1, 0, 2, 0)}
BC7_WEIGHTS = {2: [0, 21, 43, 64], 3: [0, 9, 18, 27, 37, 46, 55, 64],
               4: [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64]}
# 2-subset partitions (bit i = subset of pixel i) and the anchor pixel of subset 1 (BC6H / BC7 specification)
BC7_P2 = [0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8,
          0xFF00, 0xFFF0, 0xF000, 0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110,
          0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C, 0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696,
          0xA55A, 0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660, 0x0272, 0x04E4, 0x4E40, 0x2720,
          0xC936, 0x936C, 0x39C6, 0x639C, 0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22]
BC7_A2 = [15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8,
          2, 2, 15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6, 6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2,
          2, 15]


def _bc7_block(block):
    """16 RGBA pixels of one BC7 block."""
    v = int.from_bytes(block, 'little')
    mode = (v & -v).bit_length() - 1 if v else 8
    if mode not in BC7_MODES:
        raise ValueError('BC7 mode %d not handled' % mode)
    ns, pb, rb, isb, cb, ab, epb, spb, ib, ib2 = BC7_MODES[mode]
    pos = mode + 1

    def bits(n):
        nonlocal pos
        r = (v >> pos) & ((1 << n) - 1)
        pos += n
        return r

    part, rot, sel = bits(pb), bits(rb), bits(isb)
    ne = 2 * ns  # endpoints: subset s uses 2s and 2s+1
    ends = [[bits(cb) for _ in range(ne)] for _ in range(3)]
    ends.append([bits(ab) for _ in range(ne)] if ab else [255] * ne)
    nb = [cb, cb, cb, ab]
    pbits = [bits(1) for _ in range(ne)] if epb else [p for p in (bits(1) for _ in range(ns)) for _ in range(2)] if spb else None
    if pbits:
        for c in range(4):
            if c < 3 or ab:
                ends[c] = [(ends[c][e] << 1) | pbits[e] for e in range(ne)]
        nb = [n + 1 for n in nb]
    for c in range(4):
        if c < 3 or ab:
            n = nb[c]
            ends[c] = [(x << (8 - n)) | (x >> (2 * n - 8)) for x in ends[c]]
    subset = [(BC7_P2[part] >> i) & 1 for i in range(16)] if ns == 2 else [0] * 16
    anchors = {0, BC7_A2[part]} if ns == 2 else {0}
    idx = [bits(ib - 1 if i in anchors else ib) for i in range(16)]
    idx2 = [bits(ib2 - 1 if i == 0 else ib2) for i in range(16)] if ib2 else None
    out = bytearray(64)
    for i in range(16):
        ci, cbits, ai, abits = idx[i], ib, idx[i], ib
        if idx2 is not None:
            if sel:
                ci, cbits = idx2[i], ib2
            else:
                ai, abits = idx2[i], ib2
        e0, e1 = 2 * subset[i], 2 * subset[i] + 1
        wc, wa = BC7_WEIGHTS[cbits][ci], BC7_WEIGHTS[abits][ai]
        px = [((64 - wc) * ends[c][e0] + wc * ends[c][e1] + 32) >> 6 for c in range(3)]
        px.append(((64 - wa) * ends[3][e0] + wa * ends[3][e1] + 32) >> 6)
        if rot:
            px[rot - 1], px[3] = px[3], px[rot - 1]
        out[i * 4:i * 4 + 4] = bytes(px)
    return out


def decode(pf, w, h, data):
    """RGBA bytes."""
    if pf == 'PF_BC7':
        out = bytearray(w * h * 4)
        k = 0
        for by in range(0, h, 4):
            for bx in range(0, w, 4):
                px = _bc7_block(data[k:k + 16])
                k += 16
                for i in range(16):
                    x, y = bx + i % 4, by + i // 4
                    if x < w and y < h:
                        q = (y * w + x) * 4
                        out[q:q + 4] = px[i * 4:i * 4 + 4]
        return bytes(out)
    if pf == 'PF_B8G8R8A8':
        out = bytearray(data)
        out[0::4], out[2::4] = data[2::4], data[0::4]
        return bytes(out)
    if pf == 'PF_FloatRGBA':  # half floats, linear colour -> sRGB
        vals = struct.unpack('<%de' % (w * h * 4), data)

        def srgb(v):
            v = min(max(v, 0.0), 1.0)
            return int(round(255 * (12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055)))
        out = bytearray(w * h * 4)
        for k in range(w * h):
            r, g, b, a = vals[4 * k:4 * k + 4]
            out[4 * k:4 * k + 4] = bytes((srgb(r), srgb(g), srgb(b), int(round(255 * min(max(a, 0.0), 1.0)))))
        return bytes(out)
    alpha = pf == 'PF_DXT5'
    bs = 16 if alpha else 8
    out = bytearray(w * h * 4)
    p = 0
    for by in range(0, h, 4):
        for bx in range(0, w, 4):
            blk = data[p:p + bs]
            p += bs
            a = [255] * 16
            if alpha:
                a0, a1 = blk[0], blk[1]
                bits = int.from_bytes(blk[2:8], 'little')
                if a0 > a1:
                    pal = [a0, a1] + [((7 - k) * a0 + k * a1) // 7 for k in range(1, 7)]
                else:
                    pal = [a0, a1] + [((5 - k) * a0 + k * a1) // 5 for k in range(1, 5)] + [0, 255]
                a = [pal[(bits >> (3 * k)) & 7] for k in range(16)]
                cb = blk[8:]
            else:
                cb = blk
            c0, c1, bits = struct.unpack('<HHI', cb)
            p0, p1 = _c565(c0), _c565(c1)
            if c0 > c1 or alpha:
                cols = [p0, p1, tuple((2 * x + y) // 3 for x, y in zip(p0, p1)), tuple((x + 2 * y) // 3 for x, y in zip(p0, p1))]
            else:
                cols = [p0, p1, tuple((x + y) // 2 for x, y in zip(p0, p1)), (0, 0, 0)]
            for k in range(16):
                x, y = bx + k % 4, by + k // 4
                if x < w and y < h:
                    idx = (bits >> (2 * k)) & 3
                    r, g, b = cols[idx]
                    aa = 0 if (not alpha and c0 <= c1 and idx == 3) else a[k]
                    q = (y * w + x) * 4
                    out[q:q + 4] = bytes((r, g, b, aa))
    return bytes(out)


def downscale(rgba, w, h, size):
    """Box filter with premultiplied alpha (keeps edges clean)."""
    fx, fy = w / size, h / size
    out = bytearray(size * size * 4)
    for y in range(size):
        y0, y1 = int(y * fy), max(int(y * fy) + 1, int((y + 1) * fy))
        for x in range(size):
            x0, x1 = int(x * fx), max(int(x * fx) + 1, int((x + 1) * fx))
            r = g = b = a = n = 0
            for yy in range(y0, y1):
                row = yy * w
                for xx in range(x0, x1):
                    q = (row + xx) * 4
                    al = rgba[q + 3]
                    r += rgba[q] * al
                    g += rgba[q + 1] * al
                    b += rgba[q + 2] * al
                    a += al
                    n += 1
            q = (y * size + x) * 4
            if a:
                out[q:q + 4] = bytes((r // a, g // a, b // a, a // n))
    return bytes(out)


def write_png(path, w, h, rgba):
    raw = b''.join(b'\x00' + rgba[y * w * 4:(y + 1) * w * 4] for y in range(h))

    def chunk(t, b):
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', zlib.crc32(t + b) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


# ------------------------------------------------------------------ pak access
def repak_list(repak, paks):
    files = {}
    for pak in sorted(glob.glob(os.path.join(paks, '*.pak'))):
        res = subprocess.run([repak, 'list', pak], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if res.returncode != 0:
            print('repak list failed on', os.path.basename(pak))
            continue
        for line in res.stdout.splitlines():
            files.setdefault(line.strip(), pak)
    return files


def extract(repak, files, wanted, out):
    by_pak = {}
    for f in wanted:
        if f in files:
            by_pak.setdefault(files[f], []).append(f)
    for pak, lst in by_pak.items():
        for k in range(0, len(lst), 40):  # keep the command line short
            args = [repak, 'unpack', '-o', out, '-f']
            for f in lst[k:k + 40]:
                args += ['-i', f]
            subprocess.run(args + [pak], capture_output=True, text=True)


def main():
    paks = sys.argv[1]
    repak = sys.argv[2] if len(sys.argv) > 2 else (shutil.which('repak') or r'C:\Program Files\repak_cli\bin\repak.exe')
    hideout = json.load(open(HIDEOUT, encoding='utf-8'))
    ids = {it['id'] for u in hideout['units'] for lv in u['levels'] for it in lv['items'] if it.get('id')} | {'Cash'} | set(EXTRA_ITEMS)
    maps_json = os.path.join(ROOT, 'src', 'Resources', 'maps.json')
    if os.path.exists(maps_json):  # keys of the safes / locked doors of the MAPS tab
        ids |= {k['id'] for k in json.load(open(maps_json, encoding='utf-8')).get('keys', [])}
    big, icon_of = set(), {}
    weapons_json = os.path.join(ROOT, 'src', 'Resources', 'weapons.json')
    if os.path.exists(weapons_json):  # WEAPONS tab
        w = json.load(open(weapons_json, encoding='utf-8'))
        ids |= set(w['items'])
        big = set(w['weapons'])
        icon_of = {k: 'Test_C/Content/' + v['icon'][len('/Game/'):] for k, v in w['items'].items() if v.get('icon')}
    traders_json = os.path.join(ROOT, 'src', 'Resources', 'traders.json')
    if os.path.exists(traders_json):  # TRADERS tab
        ids |= set(json.load(open(traders_json, encoding='utf-8'))['items'])
    ids = sorted(ids)

    print('listing the paks...')
    files = repak_list(repak, paks)
    id_paths = {p.rsplit('/', 1)[-1][3:-7]: p for p in files if '/ID_' in p and p.endswith('.uasset')}
    icons = [p for p in files if p.endswith('_Icon.uasset')]

    jobs, problems = [], []
    for i in ids:
        if i in icon_of and icon_of[i] + '.uasset' in files:  # icon named in the item definition
            jobs.append(('item_' + i, icon_of[i]))
            continue
        idp = id_paths.get(i)
        if not idp:
            problems.append('no item definition ID_%s' % i)
            continue
        folder = idp.rsplit('/', 1)[0] + '/'
        cands = sorted(p for p in icons if p.startswith(folder))
        if not cands:
            problems.append('no *_Icon texture in %s' % folder)
            continue
        if len(cands) > 1:
            print('  %s: several icons, using %s' % (i, cands[0].rsplit('/', 1)[-1]))
        jobs.append(('item_' + i, cands[0][:-7]))
    for m, tex in MATERIALS.items():
        jobs.append(('res_' + m, RESOURCE_TEX + tex))
    if big:
        for c, tex in CATEGORIES.items():
            jobs.append(('cat_' + c, CAT_TEX + tex))

    tmp = tempfile.mkdtemp(prefix='irr_icons_')
    try:
        wanted = [base + ext for _, base in jobs for ext in ('.uasset', '.uexp', '.ubulk')]
        extract(repak, files, wanted, tmp)
        os.makedirs(OUT, exist_ok=True)
        old = {f for f in os.listdir(OUT) if f.endswith('.png')}
        done = set()
        for name, base in jobs:
            uexp = os.path.join(tmp, base.replace('/', os.sep) + '.uexp')
            try:
                pf, w, h, data = read_texture(uexp)
                size = BIG if (name.startswith('item_') and name[5:] in big) or name.startswith('cat_') else SIZE
                small = downscale(decode(pf, w, h, data), w, h, size)
                write_png(os.path.join(OUT, name + '.png'), size, size, small)
                done.add(name + '.png')
            except Exception as ex:  # noqa: BLE001 - report every icon that failed
                problems.append('%s: %s' % (name, ex))
        for f in sorted(old - done):
            os.remove(os.path.join(OUT, f))
            print('removed', f)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    total = sum(os.path.getsize(os.path.join(OUT, f)) for f in done)
    print('%d icons (%d KB) -> %s' % (len(done), total // 1024, OUT))
    for p in problems:
        print('WARNING:', p)


if __name__ == '__main__':
    main()
