r"""Lists the game's `irr.*` console commands and compares them with src/Resources/commands.json.

Usage:
  python -I tools/extract_console.py "<game>\Test_C\Binaries\Win64\Test_C-Win64-Shipping.exe" [out_dir]

Writes to out_dir (default: ./irr_console_out):
  console_commands.txt   every irr.* name with the UTF-16 strings around it (help text is usually
                         just before or just after the name)
  console_names.txt      the sorted list of names
and prints:
  NEW      names in the exe that are neither in commands.json nor in its "omitted" list
  REMOVED  commands of commands.json that are no longer in the exe
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
COMMANDS = os.path.join(HERE, '..', 'src', 'Resources', 'commands.json')

exe = sys.argv[1]
out_dir = sys.argv[2] if len(sys.argv) > 2 else 'irr_console_out'
os.makedirs(out_dir, exist_ok=True)

data = open(exe, 'rb').read()
u16 = re.compile(rb'(?:[\x20-\x7e\x09\x0a\x0d]\x00){3,}')
S = [(m.start(), m.group().decode('utf-16le')) for m in u16.finditer(data)]
name_re = re.compile(r'^irr\.[A-Za-z0-9_.]+$', re.I)
idx = [i for i, (a, s) in enumerate(S) if name_re.match(s.strip())]
names = sorted({S[i][1].strip() for i in idx}, key=str.lower)
# not console commands: config sections / log categories that look like names
NOT_COMMANDS = {'irr.mods', 'irr.framegeneration'}
names = [n for n in names if n.lower() not in NOT_COMMANDS]

with open(os.path.join(out_dir, 'console_commands.txt'), 'w', encoding='utf-8') as f:
    seen = set()
    for i in idx:
        if S[i][1].strip().lower() in NOT_COMMANDS:
            continue
        for j in range(max(0, i - 3), min(len(S), i + 3)):
            if j in seen:
                continue
            seen.add(j)
            a, s = S[j]
            mark = '>>' if name_re.match(s.strip()) else '  '
            f.write('%s %08x %s\n' % (mark, a, s.replace('\r', '').replace('\n', '\\n')))
        f.write('\n')
with open(os.path.join(out_dir, 'console_names.txt'), 'w', encoding='utf-8') as f:
    f.write('\n'.join(names) + '\n')

doc = json.load(open(COMMANDS, encoding='utf-8'))
known = {c['cmd'].lower() for c in doc['commands']}
omitted = {c.lower() for c in doc.get('omitted', [])}
exe_set = {n.lower() for n in names}
new = [n for n in names if n.lower() not in known and n.lower() not in omitted]
removed = [c['cmd'] for c in doc['commands'] if c['cmd'].lower() not in exe_set]
gone_omitted = [c for c in doc.get('omitted', []) if c.lower() not in exe_set]

print('%d irr.* names in the exe, %d in the tool, %d deliberately omitted' % (len(names), len(known), len(omitted)))
print('\nNEW (%d):' % len(new))
for n in new:
    print('  ' + n)
print('\nREMOVED (%d):' % len(removed))
for n in removed:
    print('  ' + n)
if gone_omitted:
    print('\nOMITTED entries no longer in the exe (remove them from OMITTED): ' + ', '.join(gone_omitted))
print('\nContext of every name: ' + os.path.abspath(os.path.join(out_dir, 'console_commands.txt')))
