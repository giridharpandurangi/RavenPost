"""Draws the Thunderstore icon (256x256): a raven on a wooden post with a sealed letter, at night.

Drawn at 1024 px and scaled down for smooth edges. Run: python thunderstore/make_icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Background: rounded square, night-sky gradient, thin gold frame.
bg = Image.new("RGBA", (S, S))
bd = ImageDraw.Draw(bg)
for y in range(S):
    t = y / S
    r = int(32 * (1 - t) + 12 * t)
    g = int(48 * (1 - t) + 18 * t)
    b = int(66 * (1 - t) + 26 * t)
    bd.line([(0, y), (S, y)], fill=(r, g, b, 255))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([16, 16, S - 16, S - 16], radius=150, fill=255)
img.paste(bg, (0, 0), mask)
d.rounded_rectangle([16, 16, S - 16, S - 16], radius=150, outline=(201, 162, 74, 255), width=14)

# Moon with a soft glow.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([640, 110, 900, 370], fill=(232, 226, 200, 90))
glow = glow.filter(ImageFilter.GaussianBlur(40))
img.alpha_composite(glow)
d.ellipse([690, 160, 850, 320], fill=(236, 230, 205, 255))

# A few stars.
for x, y, r in [(170, 170, 7), (300, 120, 5), (420, 210, 6), (230, 330, 4), (560, 140, 5), (900, 470, 5)]:
    d.ellipse([x - r, y - r, x + r, y + r], fill=(240, 236, 214, 230))

# Wooden post.
post = [(462, 600), (562, 600), (572, 990), (452, 990)]
d.polygon(post, fill=(112, 76, 44, 255))
d.polygon([(462, 600), (492, 600), (486, 990), (452, 990)], fill=(140, 98, 58, 255))  # lit side
for y in (690, 790, 890):
    d.line([(470, y), (556, y + 12)], fill=(78, 52, 30, 255), width=8)
d.rectangle([448, 584, 576, 612], fill=(92, 62, 36, 255))  # cap

# Raven (sitting, facing right).
BLACK = (16, 19, 24, 255)
SHEEN = (40, 52, 70, 255)
# tail
d.polygon([(400, 500), (250, 610), (270, 640), (300, 628), (420, 552)], fill=BLACK)
# body
d.ellipse([360, 380, 660, 590], fill=BLACK)
# wing sheen
d.chord([390, 410, 640, 570], start=180, end=330, fill=SHEEN)
d.ellipse([400, 430, 620, 560], fill=BLACK)
d.chord([410, 440, 620, 550], start=200, end=320, fill=SHEEN)
# chest/neck
d.ellipse([540, 330, 700, 520], fill=BLACK)
# head
d.ellipse([590, 270, 740, 410], fill=BLACK)
# beak
d.polygon([(720, 318), (835, 352), (722, 378)], fill=(30, 33, 38, 255))
d.line([(724, 350), (820, 352)], fill=(60, 64, 70, 255), width=5)
# eye
d.ellipse([668, 312, 694, 338], fill=(226, 178, 64, 255))
d.ellipse([677, 320, 688, 331], fill=(20, 20, 20, 255))
# legs gripping the post cap
for x in (500, 548):
    d.line([(x, 570), (x - 6, 592)], fill=(52, 46, 40, 255), width=12)
    d.line([(x - 6, 592), (x - 26, 600)], fill=(52, 46, 40, 255), width=9)
    d.line([(x - 6, 592), (x + 14, 600)], fill=(52, 46, 40, 255), width=9)

# Sealed letter, tilted, bottom left.
letter = Image.new("RGBA", (420, 300), (0, 0, 0, 0))
ld = ImageDraw.Draw(letter)
ld.rounded_rectangle([20, 40, 400, 280], radius=14, fill=(226, 204, 152, 255), outline=(150, 118, 70, 255), width=6)
ld.polygon([(20, 46), (210, 175), (400, 46)], fill=(208, 184, 132, 255), outline=(150, 118, 70, 255))
ld.line([(20, 46), (210, 175), (400, 46)], fill=(150, 118, 70, 255), width=6)
ld.ellipse([170, 135, 250, 215], fill=(170, 28, 22, 255))
ld.ellipse([188, 153, 232, 197], fill=(206, 62, 48, 255))
letter = letter.rotate(14, resample=Image.BICUBIC, expand=True)
img.alpha_composite(letter, (120, 590))

out = img.resize((256, 256), Image.LANCZOS)
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icon.png")
out.save(path)
print("saved", path, out.size)
