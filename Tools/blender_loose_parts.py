"""Report connected-component (loose part) statistics for a model.

Usage: blender -b --factory-startup -P Tools/blender_loose_parts.py -- <model.fbx>
"""
import sys

import bmesh
import bpy


def main():
    path = sys.argv[sys.argv.index("--") + 1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)
    for obj in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.verts.ensure_lookup_table()
        seen = set()
        sizes = []
        for start in bm.verts:
            if start.index in seen:
                continue
            stack = [start]
            seen.add(start.index)
            faces = set()
            while stack:
                v = stack.pop()
                for f in v.link_faces:
                    faces.add(f.index)
                for e in v.link_edges:
                    o = e.other_vert(v)
                    if o.index not in seen:
                        seen.add(o.index)
                        stack.append(o)
            sizes.append(len(faces))
        sizes.sort()
        n = len(sizes)
        small = sum(1 for s in sizes if s <= 20)
        print(f"PARTS {obj.name} parts={n} faces={len(bm.faces)} small(<=20 faces)={small} "
              f"median={sizes[n // 2] if n else 0} largest={sizes[-5:] if n else []}")
        bm.free()


main()
