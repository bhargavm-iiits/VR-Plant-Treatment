"""Reduce the supplied spray-bottle OBJ to a Quest-ready FBX for the Treatment lab's Training spray prop.

Keeps every source part as its own mesh (so Unity can give the body, head and trigger their own materials),
decimates each part to a share of the triangle budget, scales the whole bottle to a real-world height with the
pivot at the bottom centre, and exports FBX (Y up, metres).

Usage:
  blender -b --factory-startup -P Tools/blender_spray_bottle.py -- \
      --src Assets/Lab_Treatment_Assets/spray-bottle/model/SprayBottle.obj \
      --out Assets/Lab_Treatment_Assets/spray-bottle/model/SprayBottle_Quest.fbx \
      --height 0.27 --tris 9000 [--report]
"""
import argparse
import sys

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--src", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--height", type=float, default=0.27, help="Bottle height in metres")
    p.add_argument("--tris", type=int, default=9000, help="Triangle budget for the whole bottle")
    p.add_argument("--report", action="store_true", help="Only print the parts; export nothing")
    return p.parse_args(argv)


def tri_count(obj):
    return sum(len(poly.vertices) - 2 for poly in obj.data.polygons)


def world_bounds(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            w = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=args.src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("No meshes in " + args.src)

    total = sum(tri_count(o) for o in meshes)
    for obj in meshes:
        lo, hi = world_bounds([obj])
        mats = [s.material.name if s.material else "-" for s in obj.material_slots]
        print(f"PART {obj.name}: tris={tri_count(obj)} min={tuple(round(v, 3) for v in lo)} max={tuple(round(v, 3) for v in hi)} mats={mats}")
    print(f"TOTAL tris={total}")
    if args.report:
        return

    # Each part keeps its share of the budget, with a floor so small parts (trigger, nozzle) keep their shape.
    for obj in meshes:
        share = max(args.tris * tri_count(obj) / total, 300)
        ratio = min(1.0, share / max(tri_count(obj), 1))
        if ratio < 0.999:
            mod = obj.modifiers.new("Decimate", "DECIMATE")
            mod.ratio = ratio
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.modifier_apply(modifier=mod.name)

    # Scale to the real height (OBJ import is Z up in Blender) and put the pivot at the bottom centre.
    lo, hi = world_bounds(meshes)
    scale = args.height / max(hi.z - lo.z, 1e-6)
    offset = Vector(((lo.x + hi.x) * 0.5, (lo.y + hi.y) * 0.5, lo.z))
    for obj in meshes:
        obj.data.transform(obj.matrix_world)
        obj.matrix_world.identity()
        for v in obj.data.vertices:
            v.co = (v.co - offset) * scale
        obj.data.update()

    print(f"EXPORT tris={sum(tri_count(o) for o in meshes)} height={args.height} scale={scale:.5f}")
    bpy.ops.export_scene.fbx(
        filepath=args.out, use_selection=False, object_types={"MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_space_transform=True,
        path_mode="STRIP", embed_textures=False)


main()
