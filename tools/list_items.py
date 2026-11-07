r"""Writes tools/item_paths.txt: the item definition assets (ID_*) found in the game paks.

Usage:
  python -I tools/list_items.py "<game>\Test_C\Content\Paks" [path\to\repak.exe]

repak defaults to "repak" on the PATH, then C:\Program Files\repak_cli\bin\repak.exe.
"""
import glob
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'item_paths.txt')


def main():
    paks = sys.argv[1]
    repak = sys.argv[2] if len(sys.argv) > 2 else (shutil.which('repak') or r'C:\Program Files\repak_cli\bin\repak.exe')
    found = []
    for pak in sorted(glob.glob(os.path.join(paks, '*.pak'))):
        out = subprocess.run([repak, 'list', pak], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if out.returncode != 0:
            print('repak failed on', os.path.basename(pak), out.stderr.strip()[:200])
            continue
        for line in out.stdout.splitlines():
            line = line.strip()
            if '/Items/' in line and '/ID_' in line and line.endswith('.uasset'):
                found.append(line)
    found = sorted(set(found))
    old = set()
    if os.path.exists(OUT):
        old = {l.strip() for l in open(OUT, encoding='utf-8-sig') if l.strip()}
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(found) + '\n')
    name = lambda p: p.rsplit('/', 1)[-1][3:-7]
    added = sorted(name(p) for p in set(found) - old)
    removed = sorted(name(p) for p in old - set(found))
    print('%d item definitions -> %s' % (len(found), OUT))
    print('added (%d): %s' % (len(added), ', '.join(added[:60]) + (' ...' if len(added) > 60 else '')))
    print('removed (%d): %s' % (len(removed), ', '.join(removed[:60]) + (' ...' if len(removed) > 60 else '')))


if __name__ == '__main__':
    main()
