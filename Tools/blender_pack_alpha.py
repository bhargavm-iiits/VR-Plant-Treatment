"""Pack a separate opacity map into a colour texture's alpha channel (URP reads cutout alpha
from the base map). Reads any format Blender supports (TGA, JPG, PNG).

Usage:
  blender -b --factory-startup -P Tools/blender_pack_alpha.py -- \
      --color <albedo> --alpha <opacity> --out <out.png> [--size 2048]
"""
import argparse
import sys

import bpy
import numpy as np


def load(path, size):
    img = bpy.data.images.load(path)
    if size and (img.size[0] != size or img.size[1] != size):
        img.scale(size, size)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4), img


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--color", required=True)
    p.add_argument("--alpha", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--size", type=int, default=2048)
    args = p.parse_args(argv)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    color, color_img = load(args.color, args.size)
    opacity, _ = load(args.alpha, args.size)
    out = color.copy()
    out[..., 3] = opacity[..., :3].mean(axis=-1)

    img = bpy.data.images.new("packed", out.shape[1], out.shape[0], alpha=True)
    img.colorspace_settings.name = color_img.colorspace_settings.name
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = args.out
    img.file_format = "PNG"
    img.save()
    print(f"PACKED {args.out} alpha coverage {np.mean(out[..., 3] > 0.5):.1%}")


main()
