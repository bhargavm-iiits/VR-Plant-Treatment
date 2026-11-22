"""Exports the Blender apple tree (Assets/Plant_Asstes/Apple_Tree/Apple_Tree.blend) as a VR-ready FBX.

Each supplied diseased leaf (black rot, scab, cedar apple rust) is rendered once to a cut-out sprite
(flat albedo, transparent background). Every one of the 4,500 leaf instances is then replaced by a
two-triangle card in exactly that leaf's place, so the canopy keeps its layout and disease markings
at a VR budget. Trunk, branches and the 236 apples are joined and decimated. Two LODs are exported.

Run:  blender -b Assets/Plant_Asstes/Apple_Tree/Apple_Tree.blend -P Tools/blender_apple_tree.py -- <out_dir>
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
LEAVES = {"SOURCE | Apple Black Rot": "BlackRot", "SOURCE | Apple Scab": "Scab", "SOURCE | Cedar Apple Rust": "Rust"}

scene = bpy.context.scene
scene.frame_set(1)
report = {}


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def override(objs, active):
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


# Rest pose: no wind lattice.
for o in bpy.data.objects:
    for m in list(o.modifiers):
        if m.type == "LATTICE":
            o.modifiers.remove(m)


def albedo_node(material):
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED":
            link = node.inputs["Base Color"].links
            if link and link[0].from_node.type == "TEX_IMAGE":
                return link[0].from_node
    images = [n for n in material.node_tree.nodes if n.type == "TEX_IMAGE"]
    return images[0] if images else None


# ---------------------------------------------------------------- leaf sprites and cards
bake = bpy.data.scenes.new("LeafBake")
bake.render.engine = "BLENDER_WORKBENCH"
bake.display.shading.light = "FLAT"
bake.display.shading.color_type = "TEXTURE"
bake.render.film_transparent = True
bake.render.resolution_x = bake.render.resolution_y = SPRITE
bake.render.resolution_percentage = 100
bake.render.image_settings.file_format = "PNG"
bake.render.image_settings.color_mode = "RGBA"
bake.view_settings.view_transform = "Standard"
cam_data = bpy.data.cameras.new("LeafCam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("LeafCam", cam_data)
bake.collection.objects.link(cam)
bake.camera = cam

for source_name, short in LEAVES.items():
    src = bpy.data.objects[source_name]
    saved = src.matrix_world.copy()
    src.matrix_world = Matrix.Identity(4)
    src.hide_render = False
    bake.collection.objects.link(src)
    material = src.material_slots[0].material
    node = albedo_node(material)
    if node is not None:
        material.node_tree.nodes.active = node

    corners = [Vector(c) for c in src.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    size = hi - lo
    center = (lo + hi) / 2
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

    sprite_path = os.path.join(OUT, f"AppleLeaf_{short}.png")
    bake.render.filepath = sprite_path
    with bpy.context.temp_override(scene=bake):
        bpy.ops.render.render(write_still=True, scene=bake.name)
    bake.collection.objects.unlink(src)
    src.matrix_world = saved

    # Two-triangle card in the leaf plane; UVs match the sprite framing.
    h = span / 2
    verts = [center - X * h - Y * h, center + X * h - Y * h, center + X * h + Y * h, center - X * h + Y * h]
    card = bpy.data.meshes.new(f"LeafCard_{short}")
    card.from_pydata([tuple(v) for v in verts], [], [(0, 1, 2, 3)])
    uv = card.uv_layers.new(name="UVMap")
    for loop, (u, v) in zip(card.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
        uv.data[loop.index].uv = (u, v)
    leaf_material = bpy.data.materials.new(f"AppleLeaf_{short}")
    card.materials.append(leaf_material)
    src.data = card
    report[f"sprite_{short}"] = os.path.basename(sprite_path)

bpy.data.scenes.remove(bake)

# ---------------------------------------------------------------- realise the canopy
source_canopy = bpy.data.objects["Canopy | 4,500 exact leaves | wind animated"]
for mod in source_canopy.modifiers:
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
evaluated = source_canopy.evaluated_get(depsgraph)
canopy_mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
canopy = bpy.data.objects.new("Canopy", canopy_mesh)
canopy.matrix_world = source_canopy.matrix_world.copy()
scene.collection.objects.link(canopy)
report["canopy_tris"] = tris(canopy)
report["canopy_materials"] = [m.name for m in canopy_mesh.materials if m]


def collection_meshes(name):
    return [o for o in bpy.data.collections[name].all_objects if o.type == "MESH"]


def join(objs, name):
    objs = [o for o in objs if o.type == "MESH"]
    with override(objs, objs[0]):
        bpy.ops.object.convert(target="MESH")
    with override(objs, objs[0]):
        bpy.ops.object.join()
    result = objs[0]
    result.name = name
    with override([result], result):
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return result


def decimate(obj, ratio):
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    with override([obj], obj):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def rename_materials(obj):
    names = {"Bark": "AppleBark", "Red apple": "AppleFruit", "Apple stem": "AppleStem", "Material.004": "AppleStem",
             "Material.005": "AppleFruit"}
    for slot in obj.material_slots:
        if slot.material is None:
            continue
        for prefix, new in names.items():
            if slot.material.name.startswith(prefix):
                slot.material.name = new


bark = join(collection_meshes("01.1 | Trunk and branches"), "Bark")
apples = join(collection_meshes("01.3 | Red apples"), "Apples")
with override([canopy], canopy):
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
report["bark_tris_source"] = tris(bark)
report["apple_tris_source"] = tris(apples)


# ---------------------------------------------------------------- collision relaxation (fruit, leaves vs branches and each other)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blender_tree_relax  # noqa: E402
report["collisions"] = blender_tree_relax.relax(bark, apples, canopy, bpy.context.evaluated_depsgraph_get())
print("RELAX", report["collisions"])

# LOD1 copies before the LOD0 decimation.
bark1, apples1, canopy1 = (o.copy() for o in (bark, apples, canopy))
for o in (bark1, apples1, canopy1):
    o.data = o.data.copy()
    scene.collection.objects.link(o)

decimate(bark, 0.3)
decimate(apples, 0.06)
decimate(bark1, 0.1)
decimate(apples1, 0.02)

lod0 = join([bark, apples, canopy], "AppleTree_LOD0")
lod1 = join([bark1, apples1, canopy1], "AppleTree_LOD1")
for obj in (lod0, lod1):
    rename_materials(obj)
    for poly in obj.data.polygons:
        poly.use_smooth = True
report["lod0_tris"] = tris(lod0)
report["lod1_tris"] = tris(lod1)
report["height"] = lod0.dimensions.z
report["materials"] = [s.material.name for s in lod0.material_slots if s.material]

for o in bpy.data.objects:
    o.select_set(o in (lod0, lod1))
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "AppleTree.fbx"), use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
    axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
with open(os.path.join(OUT, "AppleTree_export.json"), "w") as f:
    json.dump(report, f, indent=2)
print("APPLE_EXPORT", json.dumps(report))