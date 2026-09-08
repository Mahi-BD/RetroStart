#!/usr/bin/env python3
"""Generates the app icon (Assets/WinlyStart.ico/.png) and the built-in calendar tile icon.
Modern flat mark: rounded square with an accent gradient, four white rounded tiles, soft shadow.
Rendered at 4x and downsampled for clean anti-aliasing."""
from PIL import Image, ImageDraw, ImageFilter
import os
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "src", "WinlyStart", "Assets")

def rounded(draw, box, r, fill): draw.rounded_rectangle(box, radius=r, fill=fill)

def app_icon(size):
    S = size * 4
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # gradient plate
    plate = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grad = Image.new("RGBA", (S, S))
    top, bot = (0x1F, 0x8F, 0xFF), (0x00, 0x5F, 0xB8)
    px = grad.load()
    for y in range(S):
        t = y / (S - 1)
        c = tuple(int(top[i] * (1 - t) + bot[i] * t) for i in range(3)) + (255,)
        for x in range(S): px[x, y] = c
    mask = Image.new("L", (S, S), 0)
    pad = int(S * 0.06); r = int(S * 0.22)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=r, fill=255)
    plate.paste(grad, (0, 0), mask)
    # soft shadow under the plate
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([pad, pad + int(S*0.02), S - pad, S - pad + int(S*0.02)], radius=r, fill=(0, 0, 0, 90))
    sh = sh.filter(ImageFilter.GaussianBlur(S * 0.03))
    im.alpha_composite(sh); im.alpha_composite(plate)
    # four white tiles with a subtle "start" offset, plus a faint highlight
    d = ImageDraw.Draw(im)
    inner = S - 2 * pad
    g = int(S * 0.045); t = (inner - int(S * 0.34) - g) // 2
    ox = pad + (inner - (2 * t + g)) // 2; oy = pad + (inner - (2 * t + g)) // 2
    tr = int(S * 0.045)
    for row in range(2):
        for col in range(2):
            x = ox + col * (t + g); y = oy + row * (t + g)
            rounded(d, [x, y, x + t, y + t], tr, (255, 255, 255, 235 if (row + col) % 2 == 0 else 255))
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(hl).ellipse([pad, pad - S * 0.35, S - pad, pad + S * 0.45], fill=(255, 255, 255, 38))
    hl.putalpha(hl.split()[3].point(lambda a: a).filter(ImageFilter.GaussianBlur(S * 0.02)))
    plate_mask = mask
    hl2 = Image.new("RGBA", (S, S), (0, 0, 0, 0)); hl2.paste(hl, (0, 0), plate_mask)
    im.alpha_composite(hl2)
    return im.resize((size, size), Image.LANCZOS)

def calendar_icon(size):
    S = size * 4
    im = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    pad = int(S * 0.08); r = int(S * 0.14)
    d.rounded_rectangle([pad, pad + int(S*0.08), S - pad, S - pad], radius=r, fill=(255, 255, 255, 255))
    d.rounded_rectangle([pad, pad + int(S*0.08), S - pad, pad + int(S*0.34)], radius=r, fill=(0xD1, 0x34, 0x38, 255))
    d.rectangle([pad, pad + int(S*0.22), S - pad, pad + int(S*0.34)], fill=(0xD1, 0x34, 0x38, 255))
    for x in (0.32, 0.68):   # rings
        cx = int(S * x); d.rounded_rectangle([cx - S*0.035, pad, cx + S*0.035, pad + S*0.16], radius=int(S*0.03), fill=(0x55, 0x55, 0x55, 255))
    # day grid dots
    for row in range(3):
        for col in range(4):
            x = pad + int(S*0.16) + col * int(S*0.19); y = pad + int(S*0.46) + row * int(S*0.16)
            d.rounded_rectangle([x, y, x + int(S*0.11), y + int(S*0.09)], radius=int(S*0.02),
                                fill=(0x1F, 0x8F, 0xFF, 255) if (row, col) == (1, 2) else (0xBF, 0xBF, 0xBF, 255))
    return im.resize((size, size), Image.LANCZOS)

sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
imgs = [app_icon(s) for s in sizes]
imgs[-1].save(os.path.join(OUT, "WinlyStart.ico"), format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs[:-1])
imgs[-1].save(os.path.join(OUT, "WinlyStart.png"))
calendar_icon(256).save(os.path.join(OUT, "calendar.png"))
print("icons written:", os.listdir(OUT))
