r"""Checks share codes against the tool: decode -> tool import -> tool export must give the same document.

Usage:
  python -I tools/check_roundtrip.py [path\to\IRRCompanion.exe built in Debug] [codes...]

Default exe: src/bin/Debug/net462/IRRCompanion.exe (the --roundtrip helper only exists in Debug builds).
Default codes: every .txt file of tools/testdata (codes exported by the game; add a fresh one after each
game update). For each code it checks:
  - the checksum is valid and the code is IRR1
  - the tool re-exports the same JSON document (same values, same key order)
  - the AISettings / Simulation "hv" (schema hard version) matches src/Resources/defaults.json
  - the keys of the game's AISettings document match the tool's base document (new / removed keys)
"""
import base64
import glob
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..'))
ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ'


def checksum(payload):
    h = 5381
    for ch in payload:
        h = ((h * 33) ^ ord(ch)) & 0xFFFFFFFF
    out = ''
    for _ in range(4):
        out += ALPHABET[h % 36]
        h //= 36
    return out


def decode(code):
    code = ''.join(code.split())
    head, payload, chk = code.split('-')
    assert head == 'IRR1', 'not an IRR1 code: ' + head
    assert checksum(payload) == chk, 'bad checksum'
    raw = base64.b64decode(payload + '=' * (-len(payload) % 4)).decode('utf-8')
    return json.loads(raw)


def flat_keys(d, prefix=''):
    keys = []
    for k, v in d.items():
        keys.append(prefix + k)
        if isinstance(v, dict):
            keys += flat_keys(v, prefix + k + '.')
    return keys


def order(v):
    """Key order of every object, without the values (1.0 and 1 are equal for the game)."""
    if isinstance(v, dict):
        return [(k, order(x)) for k, x in v.items()]
    if isinstance(v, list):
        return [order(x) for x in v]
    return None


def main():
    exe = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'src', 'bin', 'Debug', 'net462', 'IRRCompanion.exe')
    codes = sys.argv[2:] or sorted(glob.glob(os.path.join(HERE, 'testdata', '*.txt')))
    defaults = json.load(open(os.path.join(ROOT, 'src', 'Resources', 'defaults.json'), encoding='utf-8'))
    base_keys = flat_keys(defaults['ai'][defaults['aiOrder'][1]])
    failures = 0
    for path in codes:
        name = os.path.basename(path)
        try:
            src = decode(open(path, encoding='utf-8-sig').read())
            with tempfile.TemporaryDirectory() as tmp:
                out = os.path.join(tmp, 'out.txt')
                subprocess.run([exe, '--roundtrip', os.path.abspath(path), out], check=True, timeout=60)
                again = decode(open(out, encoding='utf-8').read())
            problems, notes = [], []
            if src != again:
                problems.append('re-exported document differs')
            elif order(src) != order(again):
                problems.append('same values but different key order')
            elif json.dumps(src) != json.dumps(again):
                notes.append('note: number formatting differs (e.g. 60.0 -> 60), accepted by the game')
            for var in src.get('vars', []):
                want = defaults['aiHardVersion'] if var['c'] == 'AISettings' else defaults.get('simHardVersion')
                if var.get('hv') != want:
                    problems.append('%s hv=%s, tool has %s' % (var['c'], var.get('hv'), want))
                if var['c'] == 'AISettings':
                    keys = flat_keys(var['doc'])
                    new = [k for k in keys if k not in base_keys]
                    gone = [k for k in base_keys if k not in keys]
                    if new:
                        problems.append('keys not in the tool: ' + ', '.join(new))
                    if gone:
                        problems.append('tool keys missing from the game doc: ' + ', '.join(gone))
            print('%-4s %s%s' % ('ok' if not problems else '!!', name, ''.join('\n       ' + l for l in problems + notes)))
            failures += bool(problems)
        except Exception as ex:  # noqa: BLE001 - report and continue with the next code
            print('!!   %s: %s' % (name, ex))
            failures += 1
    print('\n%s' % ('All codes round-trip.' if not failures else '%d code(s) with problems.' % failures))
    sys.exit(1 if failures else 0)


if __name__ == '__main__':
    main()
