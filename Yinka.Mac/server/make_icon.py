#!/usr/bin/env python3
"""Generate a 1024x1024 RGBA PNG icon for Yinka using only the stdlib.

The icon is a dark rounded-square (Pewbeam-ish) with a gold "Y" glyph drawn
from three rounded line segments. The build script feeds the result to
`sips` + `iconutil` to produce the iconset and final AppIcon.icns.

Usage:  python3 make_icon.py <out.png>
"""

from __future__ import annotations

import math
import struct
import sys
import zlib

SIZE = 1024
CORNER_RADIUS = 220

# Palette (matches the dashboard).
BG_TOP    = (0x14, 0x18, 0x1F)
BG_BOTTOM = (0x07, 0x09, 0x0C)
GOLD_HI   = (0xF1, 0xD06A & 0xFF if False else 0xC8, 0x86)
GOLD_LO   = (0xC9, 0xA1, 0x40)
SHADOW    = (0, 0, 0, 90)


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_color(c1, c2, t):
    return (
        int(round(lerp(c1[0], c2[0], t))),
        int(round(lerp(c1[1], c2[1], t))),
        int(round(lerp(c1[2], c2[2], t))),
    )


def saturate(x):
    return 0.0 if x < 0 else (1.0 if x > 1 else x)


def rounded_square_alpha(x, y, size, radius):
    """Anti-aliased coverage [0..1] for a rounded square mask."""
    dx = max(radius - x, x - (size - 1 - radius), 0.0)
    dy = max(radius - y, y - (size - 1 - radius), 0.0)
    if dx == 0 and dy == 0:
        return 1.0
    d = math.hypot(dx, dy)
    return saturate(radius + 0.5 - d)


def line_distance(px, py, x1, y1, x2, y2):
    vx, vy = x2 - x1, y2 - y1
    wx, wy = px - x1, py - y1
    seg_sq = vx * vx + vy * vy
    if seg_sq <= 1e-6:
        return math.hypot(wx, wy)
    t = (wx * vx + wy * vy) / seg_sq
    t = 0.0 if t < 0 else (1.0 if t > 1 else t)
    cx, cy = x1 + t * vx, y1 + t * vy
    return math.hypot(px - cx, py - cy)


def write_png(path, width, height, pixels):
    """Write an 8-bit RGBA PNG using only struct + zlib."""
    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter byte: None
        raw.extend(pixels[y * stride:(y + 1) * stride])
    compressed = zlib.compress(bytes(raw), 9)

    def chunk(tag, data):
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", compressed))
        f.write(chunk(b"IEND", b""))


def main(argv):
    out = argv[1] if len(argv) > 1 else "AppIcon.png"

    # Y geometry (three rounded segments). Numbers chosen for SIZE=1024.
    cx = SIZE / 2
    top_y = 268
    apex_x, apex_y = cx, 588
    foot_x, foot_y = cx, 880
    left_x, left_y = 252, top_y
    right_x, right_y = 772, top_y
    stroke_half = 78  # ~156 px stroke

    # Pre-compute background gradient (vertical).
    bg_rows = []
    for y in range(SIZE):
        t = y / (SIZE - 1)
        bg_rows.append(lerp_color(BG_TOP, BG_BOTTOM, t))

    pixels = bytearray(SIZE * SIZE * 4)

    for y in range(SIZE):
        bg_r, bg_g, bg_b = bg_rows[y]
        row = y * SIZE * 4
        for x in range(SIZE):
            mask = rounded_square_alpha(x + 0.5, y + 0.5, SIZE, CORNER_RADIUS)
            if mask <= 0.0:
                idx = row + x * 4
                pixels[idx]     = 0
                pixels[idx + 1] = 0
                pixels[idx + 2] = 0
                pixels[idx + 3] = 0
                continue

            # Subtle radial highlight from upper-left.
            dxh = (x - 280) / 800.0
            dyh = (y - 220) / 800.0
            highlight = max(0.0, 1.0 - (dxh * dxh + dyh * dyh)) * 0.18
            r = bg_r + int(highlight * 60)
            g = bg_g + int(highlight * 60)
            b = bg_b + int(highlight * 70)
            r = min(255, r); g = min(255, g); b = min(255, b)

            # Y strokes — keep the smallest distance across segments.
            d1 = line_distance(x + 0.5, y + 0.5, left_x, left_y, apex_x, apex_y)
            d2 = line_distance(x + 0.5, y + 0.5, right_x, right_y, apex_x, apex_y)
            d3 = line_distance(x + 0.5, y + 0.5, apex_x, apex_y, foot_x, foot_y)
            d = min(d1, d2, d3)

            # Soft drop shadow under the Y.
            shadow_d = min(
                line_distance(x + 0.5, y + 0.5 - 14, left_x, left_y, apex_x, apex_y),
                line_distance(x + 0.5, y + 0.5 - 14, right_x, right_y, apex_x, apex_y),
                line_distance(x + 0.5, y + 0.5 - 14, apex_x, apex_y, foot_x, foot_y),
            )
            sh_alpha = saturate((stroke_half + 24 - shadow_d) / 30.0) * 0.45
            r = int(lerp(r, 0, sh_alpha))
            g = int(lerp(g, 0, sh_alpha))
            b = int(lerp(b, 0, sh_alpha))

            stroke_alpha = saturate(stroke_half + 0.5 - d)
            if stroke_alpha > 0.0:
                # Vertical gold gradient on the stroke.
                t = (y - 220) / 700.0
                t = 0.0 if t < 0 else (1.0 if t > 1 else t)
                gr, gg, gb = lerp_color(GOLD_HI, GOLD_LO, t)
                r = int(lerp(r, gr, stroke_alpha))
                g = int(lerp(g, gg, stroke_alpha))
                b = int(lerp(b, gb, stroke_alpha))

            a = int(round(mask * 255))
            idx = row + x * 4
            pixels[idx]     = r
            pixels[idx + 1] = g
            pixels[idx + 2] = b
            pixels[idx + 3] = a

    write_png(out, SIZE, SIZE, pixels)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
