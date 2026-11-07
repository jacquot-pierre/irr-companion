r"""Extracts the game icons the app shows and writes them as small PNGs to src/Resources/icons/.

Usage:
  python -I tools/build_icons.py "<game>\Test_C\Content\Paks" [path\to\repak.exe]

Which icons: every item of src/Resources/hideout.json (run tools/build_hideout.py first), cash, the four
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

# Extra item icons to ship (item definition names, e.g. 'Bandage'), beyond the hideout items.
EXTRA_ITEMS = []
# Material -> texture name in Blueprints/ResourceSystem/Textures
MATERIALS = {'Concrete': 'concrete__1_', 'Food': 'food', 'Metal': 'metal__1_', 'Oil': 'oil'}
RESOURCE_TEX = 'Test_C/Content/Blueprints/ResourceSystem/Textures/'


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
    bpp = {'PF_DXT1': 0.5, 'PF_DXT5': 1, 'PF_B8G8R8A8': 4, 'PF_FloatRGBA': 8}.get(pf)
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


def decode(pf, w, h, data):
    """RGBA bytes."""
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
    ids = sorted({it['id'] for u in hideout['units'] for lv in u['levels'] for it in lv['items'] if it.get('id')} | {'Cash'} | set(EXTRA_ITEMS))

    print('listing the paks...')
    files = repak_list(repak, paks)
    id_paths = {p.rsplit('/', 1)[-1][3:-7]: p for p in files if '/ID_' in p and p.endswith('.uasset')}
    icons = [p for p in files if p.endswith('_Icon.uasset')]

    jobs, problems = [], []
    for i in ids:
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
                small = downscale(decode(pf, w, h, data), w, h, SIZE)
                write_png(os.path.join(OUT, name + '.png'), SIZE, SIZE, small)
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
