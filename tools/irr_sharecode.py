"""Encode / decode Incursion Red River AI preset share codes (IRR1-...).

Format (reverse-engineered from Test_C-Win64-Shipping.exe, build of 2026-11-06):
    "IRR" + <version> + "-" + <payload> + "-" + <checksum>
    payload  = base64 of the compact UTF-8 JSON document, '=' padding stripped
    checksum = djb2-xor (h = 5381; h = (h*33) ^ char, 32-bit) over the payload
               string's characters, written as 4 base-36 digits (0-9A-Z),
               least significant digit first.

The game also accepts IRR2- codes (probably a compressed payload); this
script only handles IRR1.

Usage:
    python irr_sharecode.py decode CODE_OR_FILE  [-o out.json]
    python irr_sharecode.py encode JSON_FILE     [-o out.txt]
    python irr_sharecode.py verify CODE_OR_FILE
"""
import argparse
import base64
import json
import os
import sys

ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"


def checksum(payload: str) -> str:
    h = 5381
    for ch in payload:
        h = ((h * 33) ^ ord(ch)) & 0xFFFFFFFF
    out = ""
    for _ in range(4):
        out += ALPHABET[h % 36]
        h //= 36
    return out


def split_code(code: str):
    code = code.strip()
    if not code.startswith("IRR"):
        raise ValueError("not a share code (must start with IRR1-)")
    head, rest = code.split("-", 1)
    payload, chk = rest.rsplit("-", 1)
    return head, payload, chk


def decode(code: str) -> dict:
    head, payload, chk = split_code(code)
    if head != "IRR1":
        raise ValueError(f"unsupported version {head!r} (only IRR1)")
    if checksum(payload) != chk:
        raise ValueError(f"checksum mismatch: code says {chk}, computed {checksum(payload)} (code damaged?)")
    padded = payload + "=" * (-len(payload) % 4)
    raw = base64.b64decode(padded.replace("-", "+").replace("_", "/"))
    return json.loads(raw.decode("utf-8"))


def encode_raw(json_text: str) -> str:
    payload = base64.b64encode(json_text.encode("utf-8")).decode("ascii").rstrip("=")
    return f"IRR1-{payload}-{checksum(payload)}"


def encode(doc: dict) -> str:
    return encode_raw(json.dumps(doc, separators=(",", ":"), ensure_ascii=False))


def _read_arg(s: str) -> str:
    if os.path.isfile(s):
        with open(s, encoding="utf-8-sig") as f:
            return f.read().strip()
    return s.strip()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["decode", "encode", "verify"])
    ap.add_argument("src", help="share code, or path to a file containing it / the JSON")
    ap.add_argument("-o", "--out")
    a = ap.parse_args()

    if a.cmd == "decode":
        result = json.dumps(decode(_read_arg(a.src)), indent=2, ensure_ascii=False)
    elif a.cmd == "encode":
        result = encode(json.loads(_read_arg(a.src)))
    else:
        code = _read_arg(a.src)
        doc = decode(code)
        _, payload, _ = split_code(code)
        raw = base64.b64decode(payload + "=" * (-len(payload) % 4)).decode("utf-8")
        same = encode_raw(raw) == code
        result = f"OK: checksum valid, {len(doc.get('vars', []))} variant(s), round-trip identical: {same}"

    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(result)
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        print(result)


if __name__ == "__main__":
    main()
