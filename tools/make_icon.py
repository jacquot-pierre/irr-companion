"""Generates src/Resources/app.ico (crosshair on dark background) without external libraries."""
import math
import os
import struct
import zlib

BG = (33, 29, 27)
FG = (214, 208, 176)
RED = (150, 60, 50)


def render(n):
    px = []
    c = (n - 1) / 2
    for y in range(n):
        row = []
        for x in range(n):
            # supersample 4x4 for antialiasing
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(4):
                for sx in range(4):
                    fx = (x + (sx + 0.5) / 4) / n * 2 - 1
                    fy = (y + (sy + 0.5) / 4) / n * 2 - 1
                    col, a = None, 0
                    # rounded square background
                    rx, ry = abs(fx), abs(fy)
                    k = 0.78
                    d = math.hypot(max(rx - k, 0), max(ry - k, 0))
                    if d <= 0.22:
                        col, a = BG, 255
                        r = math.hypot(fx, fy)
                        if abs(r - 0.56) < 0.075:
                            col = FG
                        if (abs(fx) < 0.05 and 0.22 < abs(fy) < 0.86) or (abs(fy) < 0.05 and 0.22 < abs(fx) < 0.86):
                            col = FG
                        if r < 0.11:
                            col = RED
                    if a:
                        acc[0] += col[0]; acc[1] += col[1]; acc[2] += col[2]; acc[3] += 255
            if acc[3]:
                cnt = acc[3] / 255
                row.append((int(acc[0] / cnt), int(acc[1] / cnt), int(acc[2] / cnt), int(acc[3] / 16)))
            else:
                row.append((0, 0, 0, 0))
        px.append(row)
    return px


def png(n):
    px = render(n)
    raw = b"".join(b"\0" + bytes(v for p in row for v in p) for row in px)

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


sizes = [16, 24, 32, 48, 64, 256]
images = [png(s) for s in sizes]
out = os.path.join(os.path.dirname(__file__), "..", "src", "Resources", "app.ico")
with open(out, "wb") as f:
    f.write(struct.pack("<HHH", 0, 1, len(sizes)))
    off = 6 + 16 * len(sizes)
    for s, im in zip(sizes, images):
        f.write(struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(im), off))
        off += len(im)
    for im in images:
        f.write(im)
print("wrote", os.path.abspath(out))
