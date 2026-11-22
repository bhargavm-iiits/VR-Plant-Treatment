"""Exports the Blender grape vine (Assets/Plant_Asstes/Grape_Tree/Grape_Tree.blend, scene
"Grape Tree - Full 3D Plant") as a VR-ready pergola (bower) vine, following the reference photo:
a straight trunk to about 1.9 m and a flat overhead canopy of canes and leaves at 2.0-2.3 m,
with the original grape bunches hanging below it.

- Each supplied diseased leaf (black rot, esca, leaf blight) is rendered to a cut-out sprite; every
  leaf instance becomes a two-triangle card, and the canopy is filled out (each leaf gets two
  companions on the same shoot) for the dense ceiling in the photo.
- Wood and canes are deformed into the pergola shape; leaves and bunches move rigidly with them.
- The original berry meshes are retained at LOD0; LOD1 keeps at least 80 triangles per berry.
  Dense stems are simplified separately; collisions are relaxed with
  blender_tree_relax (bunches are rigid groups). Two LODs.

Run:  blender -b Assets/Plant_Asstes/Grape_Tree/Grape_Tree.blend -P Tools/blender_grape_vine.py -- <out_dir>
The source .blend is not saved or modified.

After exporting, run Tools/attach_plant_parts.py (then BTP/Setup/28 in Unity): the supplied plant places many
leaves, fruit and stems near the wood rather than on it, and that pass joins every part to the plant.
"""
import json
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import blender_tree_relax  # noqa: E402

OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(OUT, exist_ok=True)
SPRITE = 512
TRUNK_TOP_SOURCE = 1.7   # where the source vine's limbs start to spread
TRUNK_TOP = 1.9          # pergola: trunk height
CANOPY_SQUASH = 0.25     # canopy thickness factor (flat ceiling)
CANOPY_MAX = 0.32        # canopy never rises more than this above the trunk top
TRUNK_THIN = 0.38        # slim trunks, as in the reference vineyard
LEAF_SIZE = 0.22         # leaf card side in metres (the leaf itself is about 15-18 cm)
CANOPY_SPREAD = 0.92     # canopy spread: neighbouring vines (3.2 m apart) meet without crossing
LEAF_COMPANIONS = 3
rng = random.Random(11)

scene = bpy.data.scenes["Grape Tree - Full 3D Plant"]
scene.frame_set(1)
report = {}


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def override(objs, active):
    return bpy.context.temp_override(scene=scene, active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


def decimate(obj, ratio):
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    with override([obj], obj):
        bpy.ops.object.modifier_apply(modifier=mod.name)


# ---------------------------------------------------------------- leaf sprites and cards
LEAVES = {"SOURCE exact leaf - Black Rot": "BlackRot", "SOURCE exact leaf - Esca - Black Measles": "Esca",
          "SOURCE exact leaf - Leaf Blight": "LeafBlight"}
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
for collection_name, short in LEAVES.items():
    src = next(o for o in bpy.data.collections[collection_name].objects if o.type == "MESH")
    for m in list(src.modifiers):
        src.modifiers.remove(m)
    saved = src.matrix_world.copy()
    src.matrix_world = Matrix.Identity(4)
    hidden = src.hide_render
    src.hide_render = False
    bake.collection.objects.link(src)
    material = src.material_slots[0].material
    for node in material.node_tree.nodes:
        if node.type == "BSDF_PRINCIPLED" and node.inputs["Base Color"].links:
            material.node_tree.nodes.active = node.inputs["Base Color"].links[0].from_node
    corners = [Vector(c) for c in src.bound_box]
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
    mat = Matrix((X, Y, Z)).transposed().to_4x4()
    mat.translation = center + Z * (size[k] + span)
    cam.matrix_world = mat
    cam_data.clip_end = (size[k] + span) * 3
    bake.render.filepath = os.path.join(OUT, f"GrapeLeaf_{short}.png")
    with bpy.context.temp_override(scene=bake):
        bpy.ops.render.render(write_still=True, scene=bake.name)
    bake.collection.objects.unlink(src)
    src.matrix_world = saved
    src.hide_render = hidden
    h = span / 2
    card = bpy.data.meshes.new(f"GrapeLeafCard_{short}")
    card.from_pydata([tuple(center + X * a * h + Y * b * h) for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))], [], [(0, 1, 2, 3)])
    uv = card.uv_layers.new(name="UVMap")
    for loop, value in zip(card.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
        uv.data[loop.index].uv = value
    card.materials.append(bpy.data.materials.new(f"GrapeLeaf_{short}"))
    src.data = card
bpy.data.scenes.remove(bake)

# ---------------------------------------------------------------- keep the already-low-poly original berries
bunch_meshes = [o for o in bpy.data.collections["SOURCE - imported whole grape bunch"].objects if o.type == "MESH"]
report["bunch_source_tris"] = sum(tris(o) for o in bunch_meshes)
berry_sources = [o for o in bunch_meshes if o.name.startswith("Fruit_grape_")]
assert len(berry_sources) == 78, "Expected the 78 original berry meshes"
report["berry_source_tris"] = [tris(o) for o in berry_sources]
done = set()
for o in bunch_meshes:
    if o.data.name in done:
        continue
    done.add(o.data.name)
    # Each supplied berry has only 224 triangles. The former 0.02 ratio reduced
    # it to a tetrahedron; smoothing normals cannot restore the missing shape.
    if o not in berry_sources and len(o.data.polygons) > 60:
        decimate(o, 0.02)
report["bunch_tris"] = sum(tris(o) for o in bunch_meshes)


def berry_geometry(obj):
    """Audit connected berry surfaces independently of the stem and leaf materials."""
    mesh = obj.data
    berry_slots = {i for i, mat in enumerate(mesh.materials)
                   if mat and (mat.name.startswith("Grapes") or mat.name == "GrapeBerry")}
    assert berry_slots, "Original berry material not found"
    components = islands_of(mesh)
    component_for_vertex = [0] * len(mesh.vertices)
    for index, verts in enumerate(components):
        for vertex in verts:
            component_for_vertex[vertex] = index
    triangle_counts = {}
    for face in mesh.polygons:
        if face.material_index in berry_slots:
            index = component_for_vertex[face.vertices[0]]
            triangle_counts[index] = triangle_counts.get(index, 0) + len(face.vertices) - 2
    counts = list(triangle_counts.values())
    assert counts, "No berry surfaces found"
    return {"count": len(counts), "min_tris": min(counts), "max_tris": max(counts),
            "total_tris": sum(counts)}


# ---------------------------------------------------------------- realise instances
def realise_collection(name):
    empties = [o for o in bpy.data.collections[name].objects if o.instance_type == "COLLECTION"]
    before = set(bpy.data.objects)
    for e in empties:
        with override([e], e):
            bpy.ops.object.duplicates_make_real(use_base_parent=False, use_hierarchy=False)
    made = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    for o in made:
        o.data = o.data.copy()
        o.matrix_world = o.matrix_world.copy()
    return made


def unify_uv(objs):
    """One UV layer named UVMap on every mesh, so joining never drops a part's UVs (the leaf cards')."""
    for o in objs:
        layers = o.data.uv_layers
        if len(layers) == 0:
            layers.new(name="UVMap")
            continue
        keep = layers.active or layers[0]
        for layer in [l for l in layers if l != keep]:
            layers.remove(layer)
        keep.name = "UVMap"


def join(objs, name):
    objs = [o for o in objs if o.type == "MESH"]
    for o in objs:
        o.parent = None if o.parent is None else o.parent
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    with override(objs, objs[0]):
        bpy.ops.object.make_single_user(object=True, obdata=True)
    with override(objs, objs[0]):
        bpy.ops.object.convert(target="MESH")
    unify_uv(objs)
    with override(objs, objs[0]):
        bpy.ops.object.join()
    result = objs[0]
    result.name = name
    with override([result], result):
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return result


leaves = join(realise_collection("3D | 03 Exact leaf assets - 360 degree canopy"), "Leaves")
bunches = join(realise_collection("3D | 04 Original grape FBX bunches"), "Bunches")
wood_objs = [o for o in bpy.data.collections["3D | 01 Trunk and radial structural branches"].objects if o.type == "MESH"]
wood_objs += [o for o in bpy.data.collections["3D | 02 Branching fruiting canes and tendrils"].objects if o.type == "MESH"]
for o in wood_objs:
    for m in list(o.modifiers):
        if m.type in ("LATTICE", "CLOTH", "SOFT_BODY"):
            o.modifiers.remove(m)
wood = join(wood_objs, "Wood")
report.update(leaf_cards=len(leaves.data.polygons), wood_source_tris=tris(wood))


# ---------------------------------------------------------------- fuller canopy: two companions per leaf
def islands_of(mesh):
    return blender_tree_relax.islands(mesh)


import bmesh  # noqa: E402

bm = bmesh.new()
bm.from_mesh(leaves.data)
bm.faces.ensure_lookup_table()
originals = list(bm.faces)
for face in originals:
    centre = face.calc_center_median()
    side = (face.verts[1].co - face.verts[0].co).length
    for v in face.verts:
        v.co = centre + (v.co - centre) * (LEAF_SIZE / max(side, 1e-4))
    normal = face.normal.copy()
    span = max((v.co - centre).length for v in face.verts)
    for _ in range(LEAF_COMPANIONS):
        result = bmesh.ops.duplicate(bm, geom=[face] + list(face.verts) + list(face.edges))
        verts = [g for g in result["geom"] if isinstance(g, bmesh.types.BMVert)]
        tangent = normal.orthogonal().normalized()
        tangent.rotate(Quaternion(normal, rng.uniform(0, math.tau)))
        offset = tangent * span * rng.uniform(0.7, 1.4) + normal * span * rng.uniform(-0.6, 0.6)
        rot = Quaternion(normal, rng.uniform(-0.8, 0.8)) @ Quaternion(tangent, rng.uniform(-0.35, 0.35))
        scale = rng.uniform(0.8, 1.0)
        for v in verts:
            v.co = centre + offset + rot @ ((v.co - centre) * scale)
bm.to_mesh(leaves.data)
bm.free()
leaves.data.update()
report["leaf_cards_filled"] = len(leaves.data.polygons)


# ---------------------------------------------------------------- pergola reshape
def pergola(co):
    t = min(max((co.z - (TRUNK_TOP_SOURCE - 0.3)) / 0.55, 0.0), 1.0)
    t = t * t * (3 - 2 * t)
    r_scale = TRUNK_THIN + (CANOPY_SPREAD - TRUNK_THIN) * t
    if co.z < TRUNK_TOP_SOURCE:
        z = co.z * TRUNK_TOP / TRUNK_TOP_SOURCE
    else:
        z = TRUNK_TOP + min((co.z - TRUNK_TOP_SOURCE) * CANOPY_SQUASH, CANOPY_MAX)
    return Vector((co.x * r_scale, co.y * r_scale, z))


for v in wood.data.vertices:
    v.co = pergola(v.co)
wood.data.update()

up = Vector((0, 0, 1))
for verts in islands_of(leaves.data):
    centre = sum((leaves.data.vertices[i].co for i in verts), Vector()) / len(verts)
    target = pergola(centre)
    a, b, c = (leaves.data.vertices[i].co for i in verts[:3])
    normal = (b - a).cross(c - a).normalized()
    if normal.length < 0.5:
        normal = up.copy()
    facing = up if normal.z >= 0 else -up
    # Pergola leaves lie mostly flat, facing the sky (or the ground for the undersides).
    tilt = normal.rotation_difference((normal + facing * 1.6).normalized())
    for i in verts:
        leaves.data.vertices[i].co = target + tilt @ (leaves.data.vertices[i].co - centre)
leaves.data.update()

bunch_groups = blender_tree_relax.fruit_bodies(bunches.data, cluster=0.06)
for verts, centre, radius in bunch_groups:
    top = max((bunches.data.vertices[i].co for i in verts), key=lambda p: p.z).copy()
    delta = pergola(top) - top
    for i in verts:
        bunches.data.vertices[i].co += delta
bunches.data.update()
report["bunches"] = len(bunch_groups)

report["top_z"] = {o.name: round(max(v.co.z for v in o.data.vertices), 2) for o in (wood, leaves, bunches)}
print("TOPZ", report["top_z"])

# ---------------------------------------------------------------- collisions
report["collisions"] = blender_tree_relax.relax(wood, bunches, leaves, bpy.context.evaluated_depsgraph_get(), fruit_cluster=0.06,
                                                leaf_limit=3.0, leaf_zmax=TRUNK_TOP + 0.5, iterations=70)
print("RELAX", report["collisions"])

# ---------------------------------------------------------------- LODs and export
copies = []
for o in (wood, bunches, leaves):
    c = o.copy()
    c.data = o.data.copy()
    scene.collection.objects.link(c)
    copies.append(c)
wood1, bunches1, leaves1 = copies
decimate(wood, 0.07)
decimate(wood1, 0.025)
report["berries_lod0"] = berry_geometry(bunches)
assert report["berries_lod0"]["min_tris"] == min(report["berry_source_tris"])
assert report["berries_lod0"]["count"] == 21 * len(berry_sources)
# Check actual connected surfaces after simplification instead of trusting a
# global ratio. Every berry must survive and keep a round silhouette.
for ratio in (0.5, 0.65, 0.8, 1.0):
    if ratio < 1.0:
        decimate(bunches1, ratio)
    audit = berry_geometry(bunches1)
    if audit["count"] == report["berries_lod0"]["count"] and audit["min_tris"] >= 80:
        report["berries_lod1"] = dict(audit, ratio=ratio)
        break
    previous = bunches1.data
    bunches1.data = bunches.data.copy()
    bpy.data.meshes.remove(previous)
else:
    raise RuntimeError("Could not preserve round berries in LOD1")


def join_plain(objs, name):
    unify_uv(objs)
    with override(objs, objs[0]):
        bpy.ops.object.join()
    objs[0].name = name
    return objs[0]


lod0 = join_plain([wood, bunches, leaves], "GrapeVine_LOD0")
lod1 = join_plain([wood1, bunches1, leaves1], "GrapeVine_LOD1")
renames = {"Old vine": "GrapeWood", "3D Bark": "GrapeWood", "3D Torn": "GrapeWood", "Mature reddish": "GrapeCane", "Fresh green": "GrapeStem", "Grapes": "GrapeBerry",
           "3D Fine living grass": "GrapeStem", "GrapeLeaf": "GrapeLeaf"}
shared = {}
for obj in (lod0, lod1):
    for slot in obj.material_slots:
        if slot.material is None or slot.material.name.startswith("GrapeLeaf_"):
            continue
        for prefix, new in renames.items():
            if slot.material.name.startswith(prefix):
                if new not in shared:
                    slot.material.name = new
                    shared[new] = slot.material
                else:
                    slot.material = shared[new]
                break
    # Merge slots that now share a material.
    with override([obj], obj):
        bpy.ops.object.material_slot_remove_unused()
    for poly in obj.data.polygons:
        poly.use_smooth = True
report.update(lod0_tris=tris(lod0), lod1_tris=tris(lod1), height=lod0.dimensions.z, width=(lod0.dimensions.x, lod0.dimensions.y),
              materials=[s.material.name for s in lod0.material_slots if s.material])
for o in bpy.data.objects:
    o.select_set(o in (lod0, lod1))
with bpy.context.temp_override(scene=scene, selected_objects=[lod0, lod1]):
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, "GrapeVine.fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
with open(os.path.join(OUT, "GrapeVine_export.json"), "w") as f:
    json.dump(report, f, indent=2, default=str)
print("GRAPE_EXPORT", json.dumps(report, default=str))
