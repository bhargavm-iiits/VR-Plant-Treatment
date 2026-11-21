"""Blender side of Tools/attach_plant_parts.py (the plant attachment pass). Two modes:

  blender -b --factory-startup -P Tools/blender_attach_io.py -- dump  <model.fbx> <out.npz>
  blender -b --factory-startup -P Tools/blender_attach_io.py -- build <in.npz>    <model.fbx>

dump: every mesh object of the FBX, per triangle corner: position (object space), normal (the corner normal the
file carries), UV0 and, when present, UV1 (the wind channel: u = wind part + 1, v = 1 on the tagged vertex),
plus the triangle material index, the object's world matrix and its material names.
build: the same objects rebuilt from (possibly edited) arrays and exported with the settings every plant exporter
in Tools/ uses (metres, -Z forward, Y up, transforms baked, corner normals as given, no textures), so Unity's
importer sees the same object names, mesh names, materials and UV channels as before.
"""
import sys

import bpy
import numpy as np


def import_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)


def dump(model, out):
    import_fbx(model)
    data = {}
    names = []
    for index, obj in enumerate(sorted((o for o in bpy.context.scene.objects if o.type == "MESH"), key=lambda o: o.name)):
        me = obj.data
        me.calc_loop_triangles()
        count = len(me.loop_triangles)
        co = np.empty(len(me.vertices) * 3, np.float64)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        tv = np.empty(count * 3, np.int32)
        me.loop_triangles.foreach_get("vertices", tv)
        tl = np.empty(count * 3, np.int32)
        me.loop_triangles.foreach_get("loops", tl)
        tm = np.empty(count, np.int32)
        me.loop_triangles.foreach_get("material_index", tm)
        normals = np.empty(len(me.loops) * 3, np.float32)
        me.corner_normals.foreach_get("vector", normals)
        prefix = f"o{index}_"
        data[prefix + "P"] = co[tv].reshape(count, 3, 3)
        data[prefix + "N"] = normals.reshape(-1, 3)[tl].reshape(count, 3, 3)
        for channel, layer in enumerate(me.uv_layers[:2]):
            uv = np.empty(len(me.loops) * 2, np.float64)
            layer.data.foreach_get("uv", uv)
            data[prefix + f"UV{channel}"] = uv.reshape(-1, 2)[tl].reshape(count, 3, 2)
        data[prefix + "M"] = tm
        data[prefix + "W"] = np.array(obj.matrix_world, dtype=np.float64)
        data[prefix + "mats"] = np.array([s.material.name if s.material else "" for s in obj.material_slots] or [""])
        data[prefix + "uvnames"] = np.array([layer.name for layer in me.uv_layers[:2]] or ["UVMap"])
        names.append((obj.name, me.name))
    data["objects"] = np.array([n for n, _ in names])
    data["meshes"] = np.array([m for _, m in names])
    np.savez_compressed(out, **data)
    print("ATTACH_IO dumped", model, [n for n, _ in names], flush=True)


def build(npz, model):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    data = np.load(npz)
    objects = []
    materials = {}
    for index, (name, mesh_name) in enumerate(zip(data["objects"], data["meshes"])):
        prefix = f"o{index}_"
        # World space with an identity object, as the exporters build their meshes (FBX import puts the Y-up to
        # Z-up conversion on the object; the export bakes it back, so Unity sees the original mesh coordinates).
        W = data[prefix + "W"]
        P = data[prefix + "P"] @ W[:3, :3].T + W[:3, 3]
        N = data[prefix + "N"] @ np.linalg.inv(W[:3, :3])
        count = len(P)
        corners = P.reshape(-1, 3)
        # Corners that share a position become one vertex; normals and UVs stay per corner.
        _, first, inverse = np.unique(np.round(corners * 1e6).astype(np.int64), axis=0, return_index=True, return_inverse=True)
        inverse = inverse.reshape(-1)
        me = bpy.data.meshes.new(str(mesh_name))
        me.vertices.add(len(first))
        me.vertices.foreach_set("co", corners[first].astype(np.float32).ravel())
        me.loops.add(count * 3)
        me.loops.foreach_set("vertex_index", inverse.astype(np.int32))
        me.polygons.add(count)
        me.polygons.foreach_set("loop_start", np.arange(0, count * 3, 3, dtype=np.int32))
        me.polygons.foreach_set("loop_total", np.full(count, 3, dtype=np.int32))
        me.polygons.foreach_set("material_index", data[prefix + "M"].astype(np.int32))
        me.polygons.foreach_set("use_smooth", np.ones(count, dtype=bool))
        uvnames = list(data[prefix + "uvnames"])
        for channel in range(2):
            key = prefix + f"UV{channel}"
            if key in data:
                layer = me.uv_layers.new(name=str(uvnames[channel]) if channel < len(uvnames) else f"UV{channel}")
                layer.data.foreach_set("uv", data[key].reshape(-1, 2).astype(np.float32).ravel())
        if me.uv_layers:
            me.uv_layers.active_index = 0
            me.uv_layers[0].active_render = True
        for material_name in data[prefix + "mats"]:
            material_name = str(material_name)
            if material_name not in materials:
                materials[material_name] = bpy.data.materials.new(material_name or "Material")
            me.materials.append(materials[material_name])
        me.update(calc_edges=True)
        normals = N.reshape(-1, 3).astype(np.float64)
        lengths = np.linalg.norm(normals, axis=1, keepdims=True)
        normals = np.where(lengths > 1e-8, normals / np.maximum(lengths, 1e-8), (0.0, 0.0, 1.0))
        me.normals_split_custom_set(normals.tolist())
        obj = bpy.data.objects.new(str(name), me)
        bpy.context.scene.collection.objects.link(obj)
        objects.append(obj)
    for obj in bpy.data.objects:
        obj.select_set(obj in objects)
    tmp = model + ".tmp.fbx"
    bpy.ops.export_scene.fbx(
        filepath=tmp, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", use_mesh_modifiers=False,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    import os
    os.replace(tmp, model)
    print("ATTACH_IO built", model, [o.name for o in objects], flush=True)


argv = sys.argv[sys.argv.index("--") + 1:]
if argv[0] == "dump":
    dump(argv[1], argv[2])
elif argv[0] == "build":
    build(argv[1], argv[2])
else:
    raise SystemExit(f"unknown mode {argv[0]}")
