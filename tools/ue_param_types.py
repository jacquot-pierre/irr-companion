r"""Checks the argument types of the game's cheat functions (the irr.World/Item/Player... commands).

Usage:
  python -I tools/ue_param_types.py "<game>\Test_C\Binaries\Win64\Test_C-Win64-Shipping.exe"

The cheat functions (SetWeatherType, SetTimeOfDay, GiveItem...) are UFunctions; their parameters are
described by UE reflection data compiled into the exe. For each parameter name, the script finds the
pointers to the name string, reads the EPropertyGenFlags byte of the FPropertyParams struct (+24) and
keeps the ones close to the anchor parameter (the cheat class). It prints the type and flags any
difference with EXPECTED, the types build_commands.py relies on.
"""
import re
import struct
import sys

GEN = ['Byte', 'Int8', 'Int16', 'Int', 'Int64', 'UInt16', 'UInt32', 'UInt64', 'UnsizedInt', 'UnsizedUInt',
       'Float', 'Double', 'Bool', 'SoftClass', 'WeakObject', 'LazyObject', 'SoftObject', 'Class', 'Object',
       'Interface', 'Name', 'Str', 'Array', 'Map', 'Set', 'Struct', 'Delegate', 'InlineMCDelegate',
       'SparseMCDelegate', 'Text', 'Enum', 'FieldPath', 'LWCReal']

ANCHOR = 'Hour24_Format'      # parameter of SetTimeOfDay, sits in the cheat class
WINDOW = 0x10000              # parameters farther than this from the anchor belong to other classes

# parameter -> type(s) expected by tools/build_commands.py (several values = several functions)
EXPECTED = {
    'WeatherType': {'Int'},            # irr.World.SetWeather <index>
    'Hour24_Format': {'Int'},          # irr.World.SetTimeOfDay <hour>
    'MissionName': {'Str'},            # irr.Mission.Start
    'NewSuccessRate': {'Float'},       # irr.World.SetSuccessRate
    'NewDurabilityPercentage': {'Float'},
    'NewReputation': {'Float'},
    'FactionName': {'Str'},
    'UnitName': {'Name'},
    'Level': {'Int'},
    'XP': {'Float'},
    'Count': {'Int'},
    'Name': {'Name'},
    'Amount': {'Int', 'Float'},
    'Category': {'Name'},
    'Page': {'Int'},
    'Min': {'Int'},
    'Max': {'Int', 'Float'},          # another function of the class also has a float Max
    'bWound': {'Bool'},
    'bLow': {'Bool'},
    'bInRestock': {'Bool'},
}


def main():
    d = open(sys.argv[1], 'rb').read()
    pe = struct.unpack_from('<I', d, 0x3c)[0]
    nsec = struct.unpack_from('<H', d, pe + 6)[0]
    optsz = struct.unpack_from('<H', d, pe + 20)[0]
    base = struct.unpack_from('<Q', d, pe + 24 + 24)[0]
    secs = []
    o = pe + 24 + optsz
    for _ in range(nsec):
        vsz, va, rsz, raw = struct.unpack_from('<IIII', d, o + 8)
        secs.append((va, raw, rsz))
        o += 40

    def off2va(off):
        for va, raw, rs in secs:
            if raw <= off < raw + rs:
                return base + va + off - raw

    def find(name):
        res = []
        for m in re.finditer(re.escape(name.encode()) + b'\0', d):
            if m.start() > 0 and d[m.start() - 1] != 0:
                continue
            va = off2va(m.start())
            if not va:
                continue
            for p in re.finditer(re.escape(struct.pack('<Q', va)), d):
                po = p.start()
                if po % 8:
                    continue
                gen = d[po + 24] & 0x3f
                res.append((off2va(po), GEN[gen] if gen < len(GEN) else hex(gen)))
        return res

    anchors = [va for va, t in find(ANCHOR) if t == 'Int']
    if not anchors:
        print('Anchor parameter %s not found: the cheat functions changed, check them by hand.' % ANCHOR)
        sys.exit(1)
    anchor = anchors[0]
    bad = 0
    for name, want in EXPECTED.items():
        got = sorted({t for va, t in find(name) if abs(va - anchor) < WINDOW})
        ok = bool(got) and set(got) <= want
        bad += not ok
        print('%-4s %-24s %-14s expected %s' % ('ok' if ok else '!!', name, '/'.join(got) or 'NOT FOUND', '/'.join(sorted(want))))
    print('\n%s' % ('All parameter types as expected.' if not bad else '%d parameter(s) to check (see !!).' % bad))


if __name__ == '__main__':
    main()
