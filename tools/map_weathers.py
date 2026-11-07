r"""Reads the weather list of every map from the cooked map setup assets and writes tools/map_weathers.json.

Usage:
  python -I tools/map_weathers.py <extracted dir>\Test_C\Content\Blueprints\MissionSystem\DataAssets\Maps

Extract that folder first (pakchunk0):
  repak unpack -o <out> -i "Test_C/Content/Blueprints/MissionSystem/DataAssets/Maps" pakchunk0-Windows.pak

Each MD_<Map>.uasset (IRRMapSetup) holds WeatherTypeProbabilities, a map of UDS weather preset ->
relative weight. The assets are cooked with unversioned properties, so the map is found by scanning
the .uexp for "count, then count x (import index of a UDS_Weather_Settings_C, float weight)".
irr.World.SetWeather takes the position in this list (see WEATHER_INDEX_BASE in build_commands.py).
"""
import glob
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'map_weathers.json')


def rd(fmt, d, o):
    return struct.unpack_from(fmt, d, o)[0], o + struct.calcsize(fmt)


def fstr(d, o):
    n, o = rd('<i', d, o)
    if n == 0:
        return '', o
    if n < 0:
        return d[o:o - 2 * n].decode('utf-16le')[:-1], o - 2 * n
    return d[o:o + n - 1].decode('latin1'), o + n


def imports(path):
    """Name map + import table of a UE5 package summary (cooked, unversioned)."""
    d = open(path, 'rb').read()
    o = 4
    leg, o = rd('<i', d, o)
    if leg != -4:
        _, o = rd('<i', d, o)
    ue4, o = rd('<i', d, o)
    ue5, o = rd('<i', d, o)
    _, o = rd('<i', d, o)
    if ue4 == 0:
        ue5 = 1012  # unversioned package: assume a recent UE5 layout
    if leg <= -9:
        o += 20
        _, o = rd('<i', d, o)
    n, o = rd('<i', d, o)
    o += n * 20
    if leg > -9:
        _, o = rd('<i', d, o)
    _, o = fstr(d, o)
    flags, o = rd('<I', d, o)
    nc, o = rd('<i', d, o)
    no, o = rd('<i', d, o)
    if ue5 >= 1008:
        o += 8
    if not flags & 0x80000000:
        _, o = fstr(d, o)
    o += 8
    _, o = rd('<i', d, o)
    _, o = rd('<i', d, o)
    ic, o = rd('<i', d, o)
    io, o = rd('<i', d, o)
    names = []
    q = no
    for _ in range(nc):
        s, q = fstr(d, q)
        q += 4
        names.append(s)
    imps = []
    q = io
    for _ in range(ic):
        _, _, cn, _, outer, on, _ = struct.unpack_from('<iiiiiii', d, q)
        q += 28
        if not flags & 0x80000000:
            q += 8
        if ue5 >= 1003:
            q += 4
        imps.append((names[cn], names[on]))
    return imps


def weathers(uasset):
    imps = imports(uasset)
    x = open(uasset[:-6] + 'uexp', 'rb').read()
    best = None
    for o in range(len(x) - 4):
        n = struct.unpack_from('<i', x, o)[0]
        if not (1 <= n <= 30) or o + 4 + 8 * n > len(x):
            continue
        ent = []
        for i in range(n):
            idx, w = struct.unpack_from('<if', x, o + 4 + 8 * i)
            if not (-len(imps) <= idx < 0 and imps[-idx - 1][0] == 'UDS_Weather_Settings_C' and 0 < w < 1000):
                ent = None
                break
            ent.append([imps[-idx - 1][1], round(w, 3)])
        if ent and (best is None or len(ent) > len(best)):
            best = ent
    return best


def main():
    folder = sys.argv[1]
    result = {}
    for p in sorted(glob.glob(os.path.join(folder, 'MD_*.uasset'))):
        m = os.path.basename(p)[3:-7]
        w = weathers(p)
        print('%-12s %s' % (m, ', '.join('%s (%g)' % (a, b) for a, b in w) if w else 'NO WEATHER LIST FOUND'))
        if w:
            result[m] = w
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(result, f, indent=1)
    print('wrote', os.path.normpath(OUT))


if __name__ == '__main__':
    main()
