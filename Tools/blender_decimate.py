"""Decimate a plant model into Unity-ready Lab and Farm FBX files.

The model is re-pivoted to ground level (bounding-box bottom centre), scaled to a
real-world height in metres, and decimated to fixed triangle budgets:

  <out-dir>/<name>_Lab.fbx   one mesh "<name>_Lab"    (close-up view)
  <out-dir>/<name>_Farm.fbx  one mesh "<name>_Farm"   (near LOD; Unity adds a billboard LOD)

Usage:
  blender -b --factory-startup -P Tools/blender_decimate.py -- \
      --src <model.fbx|.glb> --out-dir <dir> --name Tomato --height 1.0 \
      --lab-tris 60000 --farm-tris 8000 \
      [--pick <mesh name>] [--cleanup 0.0005] [--rotate-x 90] [--preview-dir <dir>]

Farm meshes can be decimated from an existing <name>_Lab.fbx (--src it with --skip-lab),
which is much faster than starting again from the multi-million-triangle source.
"""
import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--src", required=True)
    p.add_argument("--out-dir", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--height", type=float, required=True, help="Target height in metres")
    p.add_argument("--lab-tris", type=int, default=60000)
    p.add_argument("--farm-tris", type=int, default=6000)
    p.add_argument("--farm-lod1-tris", type=int, default=0)
    p.add_argument("--pick", default=None, help="Keep only this mesh object (e.g. one plant of a collection)")
    p.add_argument("--rotate-x", type=float, default=0.0, help="Extra X rotation in degrees before pivoting")
    p.add_argument("--cleanup", type=float, default=0.0,
                   help="Merge vertices closer than this (metres, after scaling) and dissolve "
                        "degenerate geometry before decimating; unblocks meshes that stall")
    p.add_argument("--skip-lab", action="store_true")
    p.add_argument("--skip-farm", action="store_true")
    p.add_argument("--preview-dir", default=None)
    return p.parse_args(argv)


def import_model(path):
    lower = path.lower()
    if lower.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)


def tri_count(obj):
    mesh = obj.data
    mesh.calc_loop_triangles()
    return len(mesh.loop_triangles)


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def build_base(args):
    """Import, keep the wanted meshes, join, then pivot and scale to real size."""
    import_model(args.src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if args.pick:
        meshes = [o for o in meshes if o.name == args.pick]
        if not meshes:
            raise SystemExit(f"--pick {args.pick!r} matched no mesh")

    # Bake world transforms into the mesh data and drop parents / empties.
    for o in meshes:
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world
    for o in list(bpy.context.scene.objects):
        if o not in meshes:
            bpy.data.objects.remove(o, do_unlink=True)
    select_only(meshes)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    base = bpy.context.view_layer.objects.active

    if args.rotate_x:
        base.data.transform(Matrix.Rotation(math.radians(args.rotate_x), 4, "X"))

    # Pivot at the bottom centre of the bounds, then scale to the target height.
    coords = [v.co for v in base.data.vertices]
    lo = Vector((min(c.x for c in coords), min(c.y for c in coords), min(c.z for c in coords)))
    hi = Vector((max(c.x for c in coords), max(c.y for c in coords), max(c.z for c in coords)))
    pivot = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    scale = args.height / (hi.z - lo.z)
    base.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-pivot))
    base.location = (0, 0, 0)

    # One material slot so Unity creates a single, remappable material. The supplied
    # models use one texture atlas, so every face can share the first material; it is
    # kept (renamed) so preview renders still show the texture.
    for poly in base.data.polygons:
        poly.material_index = 0
    while len(base.data.materials) > 1:
        base.data.materials.pop()
    if base.data.materials and base.data.materials[0]:
        base.data.materials[0].name = f"{args.name}_Mat"
    else:
        base.data.materials.clear()
        base.data.materials.append(bpy.data.materials.new(f"{args.name}_Mat"))
    base.data.update()
    report_topology(base, "before")
    if args.cleanup > 0:
        cleanup_mesh(base, args.cleanup)
        report_topology(base, "after cleanup")
    print(f"BASE tris={tri_count(base)} scale={scale:.4f} size=({(hi.x-lo.x)*scale:.3f}, "
          f"{(hi.y-lo.y)*scale:.3f}, {(hi.z-lo.z)*scale:.3f})")
    return base


def report_topology(obj, label):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    non_manifold = sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary)
    boundary = sum(1 for e in bm.edges if e.is_boundary)
    wire = sum(1 for e in bm.edges if e.is_wire)
    print(f"TOPOLOGY {label}: verts={len(bm.verts)} edges={len(bm.edges)} faces={len(bm.faces)} "
          f"non_manifold={non_manifold} boundary={boundary} wire={wire}")
    bm.free()


def cleanup_mesh(obj, merge_distance):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=merge_distance)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=merge_distance)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def decimated_copy(base, name, target_tris):
    obj = base.copy()
    obj.data = base.data.copy()
    obj.name = obj.data.name = name
    bpy.context.scene.collection.objects.link(obj)
    source_tris = tri_count(obj)
    if target_tris < source_tris:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = target_tris / source_tris
        mod.use_collapse_triangulate = True
        select_only([obj])
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    print(f"LOD {name} tris={tri_count(obj)} (from {source_tris})")
    return obj


def export_fbx(objs, path):
    select_only(objs)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False,
    )
    print(f"EXPORT {path}")


def render_preview(objs, path, height):
    """Workbench render of the given objects, for a quick visual check."""
    scene = bpy.context.scene
    for o in scene.objects:
        o.hide_render = o not in objs
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    dist = height * 2.2
    cam.location = (dist * 0.7, -dist * 0.7, height * 0.8)
    direction = Vector((0, 0, height * 0.45)) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    print(f"PREVIEW {path}")


def main():
    args = parse_args()
    os.makedirs(args.out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    base = build_base(args)

    outputs = []
    if not args.skip_lab:
        lab = decimated_copy(base, f"{args.name}_Lab", args.lab_tris)
        export_fbx([lab], os.path.join(args.out_dir, f"{args.name}_Lab.fbx"))
        outputs.append(lab)
    if not args.skip_farm:
        # The distant LOD is a billboard generated in Unity from the finished prefab, so
        # the farm file normally holds one mesh; --farm-lod1-tris adds a second mesh LOD.
        if args.farm_lod1_tris > 0:
            farm = [decimated_copy(base, f"{args.name}_Farm_LOD0", args.farm_tris),
                    decimated_copy(base, f"{args.name}_Farm_LOD1", args.farm_lod1_tris)]
        else:
            farm = [decimated_copy(base, f"{args.name}_Farm", args.farm_tris)]
        export_fbx(farm, os.path.join(args.out_dir, f"{args.name}_Farm.fbx"))
        outputs.extend(farm)

    if args.preview_dir:
        os.makedirs(args.preview_dir, exist_ok=True)
        for obj in outputs:
            render_preview([obj], os.path.join(args.preview_dir, f"{obj.name}.png"), args.height)


main()
