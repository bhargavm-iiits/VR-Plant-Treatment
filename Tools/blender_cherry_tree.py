"""Exports the Blender cherry tree (Assets/Plant_Asstes/Cherry_Tree/Cherry_Tree.blend) as a VR-ready FBX
plus a per-leaf wind table, so the 8-second leaf wind loop survives into Unity.

The blend animates each of its 1,225 leaf blades as a separate object (quaternion keys every 4 frames,
seamless loop). Unity cannot afford 1,225 animated transforms per tree, so the leaves are joined into one
mesh and the motion is replayed in the vertex shader instead (BTP/Farm Recovery, _LEAF_WIND):
  * UV channel 1 ("Wind") tags every leaf vertex with its leaf index (u = i + 1, 0 = not a leaf) and marks
    the leaf's base vertex (v = 1), so Unity can find each rotation pivot in its own mesh space;
  * CherryTree_Wind.bytes holds, per leaf, the Blender world pivot and 48 rotation keys relative to frame 1
    (the pose the mesh is exported in), little-endian float32: pivot xyz, then 48 x (w, x, y, z).
    Assets/_Project/Scripts/Editor/CherryWindBaker.cs converts it to the shader's float texture.
Wood is decimated, the fruit drops its subdivision and is decimated hard (it was 875k triangles). Two LODs.

Run:  blender -b Assets/Plant_Asstes/Cherry_Tree/Cherry_Tree.blend -P Tools/blender_cherry_tree.py -- <out_dir>
The source .blend is never saved.
"""
import json
import math
import os
import struct
import sys

import bpy
from mathutils import Quaternion, Vector

OUT = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
KEYS = 48          # keys at frames 1, 5, ..., 189; frame 193 == frame 1 (seamless loop)
STEP = 4
FIRST = 1
SECONDS = 8.0
report = {}

TRUNK = "01 · Trunk & living branches"
LEAVES = "02 · Individual asset leaves"
FRUIT = "03 · Cherries & stems"

# Triangle targets. Fruit and stems are shared meshes, so they are decimated once per source mesh.
TRUNK_LOD0, TRUNK_LOD1 = 0.5, 0.15
FRUIT_TRIS = (80, 25)
STEM_TRIS = (30, 10)


def tris(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def override(objs, active):
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


def mesh_objects(collection):
    return sorted((o for o in bpy.data.collections[collection].all_objects if o.type == "MESH"), key=lambda o: o.name)


trunk_src = mesh_objects(TRUNK)
leaf_all = mesh_objects(LEAVES)
leaf_src = [o for o in leaf_all if o.data.name.startswith("Exact asset leaf")]
petiole_src = [o for o in leaf_all if o.data.name.startswith("Leaf petiole")]
fruit_all = mesh_objects(FRUIT)
body_src = [o for o in fruit_all if o.data.name.startswith("Original cherry body")]
stem_src = [o for o in fruit_all if o.data.name.startswith("Original curved cherry stem")]
assert len(leaf_src) + len(petiole_src) == len(leaf_all), "unexpected object in the leaf collection"
assert len(body_src) + len(stem_src) == len(fruit_all), "unexpected object in the fruit collection"
report.update(source_objects=dict(trunk=len(trunk_src), leaves=len(leaf_src), petioles=len(petiole_src),
                                  fruit=len(body_src), stems=len(stem_src)))

# ---------------------------------------------------------------- wind keys (sampled before any geometry is built)
# D(k) = world rotation of leaf i at key k relative to frame 1. The leaf only rotates about its origin (its
# base vertex), so D fully describes the motion of the frame-1 mesh: x(k) = D(k) (x - pivot) + pivot.
n = len(leaf_src)
frames = list(range(FIRST, FIRST + KEYS * STEP + 1))           # 1..193, every frame (for the interpolation check)
mats = {}
for f in frames:
    scene.frame_set(f)
    mats[f] = [o.matrix_world.copy() for o in leaf_src]
scene.frame_set(FIRST)

base_inv = [m.inverted() for m in mats[FIRST]]
pivots = [m.translation.copy() for m in mats[FIRST]]


def relative(f, i):
    return (mats[f][i] @ base_inv[i]).to_3x3().to_quaternion().normalized()


keys = []
max_angle = mean_max = 0.0
loop_error = drift = interp_error = 0.0
wrap_flips = 0
for i in range(n):
    q = [relative(FIRST + k * STEP, i) for k in range(KEYS)]
    if q[0].w < 0:
        q[0].negate()
    for k in range(1, KEYS):
        if q[k].dot(q[k - 1]) < 0:
            q[k].negate()
    if q[KEYS - 1].dot(q[0]) < 0:
        wrap_flips += 1        # the shader also aligns hemispheres before blending, so this is harmless
    keys.append(q)
    leaf_max = max(2 * math.degrees(math.acos(min(1.0, abs(x.w)))) for x in q)
    max_angle = max(max_angle, leaf_max)
    mean_max += leaf_max / n
    # Loop: frame 193 must equal frame 1.
    end = relative(FIRST + KEYS * STEP, i)
    loop_error = max(loop_error, math.degrees(end.rotation_difference(Quaternion()).angle))
    # Translation of the pivot must stay put (rotation about the base only).
    for f in frames:
        drift = max(drift, (mats[f][i].translation - pivots[i]).length)
    # How far the shader's linear key blend strays from Blender's bezier curves between keys.
    for f in frames[:-1]:
        k0, r = divmod(f - FIRST, STEP)
        if r == 0:
            continue
        a, b = q[k0], q[(k0 + 1) % KEYS]
        if a.dot(b) < 0:
            b = -b
        t = r / STEP
        blend = Quaternion([a[j] * (1 - t) + b[j] * t for j in range(4)]).normalized()
        interp_error = max(interp_error, math.degrees(blend.rotation_difference(relative(f, i)).angle))

report["wind"] = dict(leafCount=n, keyCount=KEYS, seconds=SECONDS, fps=scene.render.fps, frameStep=STEP,
                      maxAngleDeg=round(max_angle, 3), meanLeafMaxAngleDeg=round(mean_max, 3),
                      loopErrorDeg=loop_error, pivotDriftM=drift, keyBlendErrorDeg=round(interp_error, 4),
                      wrapHemisphereFlips=wrap_flips)
with open(os.path.join(OUT, "CherryTree_Wind.bytes"), "wb") as f:
    for i in range(n):
        f.write(struct.pack("<3f", *pivots[i]))
        for q in keys[i]:
            f.write(struct.pack("<4f", q.w, q.x, q.y, q.z))
print("WIND", json.dumps(report["wind"]))

# ---------------------------------------------------------------- geometry
decimated = {}


def decimate_mesh(mesh, ratio):
    """A decimated copy of mesh (collapse), or a plain copy when ratio >= 1."""
    copy = mesh.copy()
    if ratio >= 1.0:
        return copy
    tmp = bpy.data.objects.new("DecimateTmp", copy)
    scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    with override([tmp], tmp):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(tmp)
    return copy


def shared(mesh, target_tris):
    key = (mesh.name, target_tris)
    if key not in decimated:
        decimated[key] = decimate_mesh(mesh, target_tris / max(tris(mesh), 1))
    return decimated[key]


def texture_uv_layer(mesh):
    """The UV layer the material renders with (the fruit keeps a preserved original layer too)."""
    return next((l for l in mesh.uv_layers if l.active_render), mesh.uv_layers[0] if mesh.uv_layers else None)


def set_uvs(mesh, wind=None):
    """Exactly two UV layers, UVMap (texture) and Wind, so joined meshes merge by name."""
    loops = len(mesh.loops)
    src = texture_uv_layer(mesh)
    uv = [0.0] * (loops * 2)
    if src is not None:
        src.data.foreach_get("uv", uv)
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uv)
    mesh.uv_layers.new(name="Wind").data.foreach_set("uv", wind if wind is not None else [0.0] * (loops * 2))
    mesh.uv_layers.active_index = 0
    mesh.uv_layers["UVMap"].active_render = True


def place(src, mesh, name, wind=None):
    """World-space (transform applied), single-user copy of mesh at src's frame-1 placement."""
    data = mesh.copy()
    m = src.matrix_world
    data.transform(m)
    if m.determinant() < 0:
        data.flip_normals()
    set_uvs(data, wind)
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    return obj


base_vertex = {}
base_offset = 0.0


def leaf_base(mesh):
    """Index of the vertex at the object origin: the leaf's rotation pivot."""
    global base_offset
    if mesh.name not in base_vertex:
        best = min(range(len(mesh.vertices)), key=lambda v: mesh.vertices[v].co.length)
        base_offset = max(base_offset, mesh.vertices[best].co.length)
        base_vertex[mesh.name] = best
    return base_vertex[mesh.name]


def leaf_wind(mesh, index):
    base = leaf_base(mesh)
    wind = []
    for loop in mesh.loops:
        wind += (float(index + 1), 1.0 if loop.vertex_index == base else 0.0)
    return wind


def build(lod):
    parts = []
    trunk_ratio = TRUNK_LOD0 if lod == 0 else TRUNK_LOD1
    for o in trunk_src:
        mesh = decimate_mesh(o.data, trunk_ratio)
        if len(mesh.polygons):
            parts.append(place(o, mesh, f"Trunk{lod}"))
        bpy.data.meshes.remove(mesh)
    pivot_error = 0.0
    for i, o in enumerate(leaf_src):
        obj = place(o, o.data, f"Leaf{lod}", leaf_wind(o.data, i))
        pivot_error = max(pivot_error, (obj.data.vertices[leaf_base(o.data)].co - pivots[i]).length)
        parts.append(obj)
    if lod == 0:
        parts += [place(o, o.data, "Petiole0") for o in petiole_src]
    parts += [place(o, shared(o.data, FRUIT_TRIS[lod]), f"Fruit{lod}") for o in body_src]
    parts += [place(o, shared(o.data, STEM_TRIS[lod]), f"Stem{lod}") for o in stem_src]
    report[f"lod{lod}_pivot_vertex_error_m"] = pivot_error
    return parts


def join(objs, name):
    with override(objs, objs[0]):
        bpy.ops.object.join()
    obj = objs[0]
    obj.name = obj.data.name = name
    return obj


lod0 = join(build(0), "CherryTree_LOD0")
lod1 = join(build(1), "CherryTree_LOD1")
report["fruit_tris_each"] = {f"{name} -> {t}": tris(m) for (name, t), m in decimated.items()}
report["leaf_base_vertex_offset_m"] = base_offset

renames = {"Cherry · original asset bark": "CherryBark", "Leaves · exact Cherry1 atlas pixels": "CherryLeaf",
           "Cherry · green-brown pedicels": "CherryPedicel", "Cherries · supplied baked maps": "CherryFruit"}
for old, new in renames.items():
    material = bpy.data.materials[old]
    material.name = new
    assert material.name == new, f"material name {new} is already taken in the blend"
for obj in (lod0, lod1):
    obj.data.shade_smooth()   # wood, fruit and the gently curved blades all read better smooth
    report[obj.name] = dict(tris=tris(obj.data), verts=len(obj.data.vertices),
                            materials=[s.material.name for s in obj.material_slots if s.material],
                            uv_layers=[l.name for l in obj.data.uv_layers])
corners = [lod0.matrix_world @ Vector(c) for c in lod0.bound_box]
report["lod0_bbox_blender"] = dict(min=[min(c[a] for c in corners) for a in range(3)],
                                   max=[max(c[a] for c in corners) for a in range(3)])
report["wind"]["lod0_bbox_blender"] = report["lod0_bbox_blender"]

for o in bpy.data.objects:
    o.select_set(o in (lod0, lod1))
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "CherryTree.fbx"), use_selection=True, object_types={"MESH"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
    axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
    add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
with open(os.path.join(OUT, "CherryTree_export.json"), "w") as f:
    json.dump(report, f, indent=2)
print("CHERRY_EXPORT", json.dumps(report))
