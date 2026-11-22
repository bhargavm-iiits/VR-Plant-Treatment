"""Exports the Blender peach tree (Assets/Plant_Asstes/Peach/Peach_Tree.blend) as a VR-ready FBX.

The supplied spotted peach leaf is rendered once to a cut-out sprite and each of the 800 leaf
instances becomes a two-triangle card in that leaf's place. The supplied peach (body and stem) is
decimated before its 80 instances are realised; the trunk and branches are decimated. Two LODs.

Run:  blender -b Assets/Plant_Asstes/Peach/Peach_Tree.blend -P Tools/blender_peach_tree.py -- <out_dir>
The source .blend is not saved or modified.

After exporting, run Tools/attach_plant_parts.py (then BTP/Setup/28 in Unity): the supplied plant places many
leaves, fruit and stems near the wood rather than on it, and that pass joins every part to the plant.
"""
import json
import os
import sys

import bpy
from mathutils import Matrix, Vector

OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(OUT, exist_ok=True)
SPRITE = 512
scene = bpy.context.scene
scene.frame_set(1)
report = {}


def find(prefix):
    return next(o for o in bpy.data.objects if o.name.startswith(prefix))


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def override(objs, active):
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


def decimate(obj, ratio):
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    with override([obj], obj):
        bpy.ops.object.modifier_apply(modifier=mod.name)


for o in bpy.data.objects:
    for m in list(o.modifiers):
        if m.type in ("LATTICE", "DECIMATE"):
            o.modifiers.remove(m)

# ---------------------------------------------------------------- leaf sprite and card
leaf_src = find("RENDER SOURCE | Exact supplied spotted peach leaf")
bake = bpy.data.scenes.new("LeafBake")
bake.render.engine = "BLENDER_WORKBENCH"
bake.display.shading.light = "FLAT"
bake.display.shading.color_type = "TEXTURE"
bake.render.film_transparent = True
bake.render.resolution_x = bake.render.resolution_y = SPRITE
bake.render.image_settings.file_format = "PNG"
bake.render.image_settings.color_mode = "RGBA"
bake.view_settings.view_transform = "Standard"
cam_data = bpy.data.cameras.new("LeafCam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("LeafCam", cam_data)
bake.collection.objects.link(cam)
bake.camera = cam

saved = leaf_src.matrix_world.copy()
leaf_src.matrix_world = Matrix.Identity(4)
leaf_src.hide_render = False
bake.collection.objects.link(leaf_src)
material = leaf_src.material_slots[0].material
for node in material.node_tree.nodes:
    if node.type == "BSDF_PRINCIPLED" and node.inputs["Base Color"].links:
        material.node_tree.nodes.active = node.inputs["Base Color"].links[0].from_node
corners = [Vector(c) for c in leaf_src.bound_box]
lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
size, center = hi - lo, (lo + hi) / 2
k = min(range(3), key=lambda a: size[a])
i, j = [a for a in range(3) if a != k]
axes = [Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))]
X, Z = axes[i], axes[k]
Y = Z.cross(X)
span = max(size[i], size[j]) * 1.04
cam_data.ortho_scale = span
m = Matrix((X, Y, Z)).transposed().to_4x4()
m.translation = center + Z * (size[k] + span)
cam.matrix_world = m
cam_data.clip_end = (size[k] + span) * 3
bake.render.filepath = os.path.join(OUT, "PeachLeaf.png")
with bpy.context.temp_override(scene=bake):
    bpy.ops.render.render(write_still=True, scene=bake.name)
bake.collection.objects.unlink(leaf_src)
leaf_src.matrix_world = saved
h = span / 2
card = bpy.data.meshes.new("PeachLeafCard")
card.from_pydata([tuple(center + X * a * h + Y * b * h) for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))], [], [(0, 1, 2, 3)])
uv = card.uv_layers.new(name="UVMap")
for loop, value in zip(card.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
    uv.data[loop.index].uv = value
card.materials.append(bpy.data.materials.new("PeachLeaf"))
leaf_src.data = card
bpy.data.scenes.remove(bake)

# ---------------------------------------------------------------- lighter fruit before realising
fruit_src = find("RENDER SOURCE | supplied peach OBJ body and stem")
report["fruit_source_tris"] = tris(fruit_src)
decimate(fruit_src, 0.005)
report["fruit_tris_each"] = tris(fruit_src)


def realise(prefix, name):
    src = find(prefix)
    for mod in src.modifiers:
        if mod.type != "NODES":
            continue
        tree = mod.node_group
        output = next(n for n in tree.nodes if n.type == "GROUP_OUTPUT")
        socket = output.inputs[0]
        if socket.links:
            realize = tree.nodes.new("GeometryNodeRealizeInstances")
            tree.links.new(socket.links[0].from_socket, realize.inputs[0])
            tree.links.new(realize.outputs[0], socket)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    depsgraph.update()
    mesh = bpy.data.meshes.new_from_object(src.evaluated_get(depsgraph), preserve_all_data_layers=True, depsgraph=depsgraph)
    obj = bpy.data.objects.new(name, mesh)
    obj.matrix_world = src.matrix_world.copy()
    scene.collection.objects.link(obj)
    with override([obj], obj):
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


canopy = realise("Canopy | ", "Canopy")
fruit = realise("Harvest | ", "Fruit")
trunk_src = find("Peach tree | connected trunk")
trunk = trunk_src.copy()
trunk.data = trunk_src.data.copy()
scene.collection.objects.link(trunk)
with override([trunk], trunk):
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
report.update(canopy_tris=tris(canopy), fruit_tris=tris(fruit), trunk_source_tris=tris(trunk))


# ---------------------------------------------------------------- collision relaxation (fruit, leaves vs branches and each other)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blender_tree_relax  # noqa: E402
report["collisions"] = blender_tree_relax.relax(trunk, fruit, canopy, bpy.context.evaluated_depsgraph_get())
print("RELAX", report["collisions"])

copies = []
for o in (trunk, fruit, canopy):
    c = o.copy()
    c.data = o.data.copy()
    scene.collection.objects.link(c)
    copies.append(c)
trunk1, fruit1, canopy1 = copies
decimate(trunk, 0.06)
decimate(trunk1, 0.02)
decimate(fruit1, 0.35)


def join(objs, name):
    with override(objs, objs[0]):
        bpy.ops.object.join()
    objs[0].name = name
    return objs[0]


lod0 = join([trunk, fruit, canopy], "PeachTree_LOD0")
lod1 = join([trunk1, fruit1, canopy1], "PeachTree_LOD1")
renames = {"Peach bark": "PeachBark", "Peach skin": "PeachSkin", "Peach stems": "PeachStem", "PeachLeaf": "PeachLeaf"}
for obj in (lod0, lod1):
    for slot in obj.material_slots:
        if slot.material:
            for prefix, new in renames.items():
                if slot.material.name.startswith(prefix):
                    slot.material.name = new
    for poly in obj.data.polygons:
        poly.use_smooth = True
report.update(lod0_tris=tris(lod0), lod1_tris=tris(lod1), height=lod0.dimensions.z,
              materials=[s.material.name for s in lod0.material_slots if s.material])

for o in bpy.data.objects:
    o.select_set(o in (lod0, lod1))
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "PeachTree.fbx"), use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
    axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
with open(os.path.join(OUT, "PeachTree_export.json"), "w") as f:
    json.dump(report, f, indent=2)
print("PEACH_EXPORT", json.dumps(report))