#!/usr/bin/env python3
"""Pack the approved artwork at full Windows icon size. Requires Pillow."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent
image = Image.open(root / 'AppIcon-source.png').convert('RGBA')
# Ignore near-transparent antialias residue when determining the artwork bounds.
bounds = image.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
if bounds is None:
    raise ValueError('Icon source is empty')
art = image.crop(bounds)
side = max(art.size)
# Normalize the almost-square artwork to an exact square without adding padding.
canvas = art.resize((side, side), Image.Resampling.LANCZOS)
canvas.save(root / 'AppIcon.png')
canvas.save(root / 'AppIcon.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print('Artwork bounds:', bounds, 'Packed canvas:', canvas.size)
for size in sorted(Image.open(root / 'AppIcon.ico').ico.sizes()):
    frame = Image.open(root / 'AppIcon.ico').ico.getimage(size)
    box = frame.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
    print(size, 'visible bounds:', box)
