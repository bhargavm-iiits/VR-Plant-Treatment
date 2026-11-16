"""Exports the Blender bell pepper plant
(Assets/Plant_Asstes/Pepper/Bell Pepper/Bell_Pepper_Plant/Bell_Pepper_Plant_3D_Wind_8s.blend) as a VR-ready FBX plus a
per-leaf wind table, so the blend's 8-second wind loop survives into Unity (as Tools/blender_tomato_plants.py does).

The blend is a ~1.5 m plant in metres: 31 instances of the supplied Meshy bacterial-spot leaf (~265k triangles each),
six supplied peppers (two red GLB, four multicolour OBJ recoloured green / yellow by a colour ramp, ~824k triangles
each), a curve-built main stem, eight branches, petioles and fruit stalks, two flowers and three buds. Every leaf
hangs from an animated "Wind leaf NN" empty (under an animated "Wind branch NN" empty) and also flutters about its
own origin, which is the leaf's base, where the petiole ends.

For Quest the plant has to fit ~12k triangles (LOD0) and ~4k (LOD1), so:
  * leaves become curved cards: the leaf is rendered once, top down, to a cut-out sprite (its exact disease
    texture), and a grid card that follows the leaf's surface replaces each instance; empty grid cells are dropped;
  * peppers are decimated; their textures (red GLB, green and yellow ramps of the multicolour map) share one 1024
    atlas with two flat swatches (petal ivory, anther pollen) so fruit and flowers are one material;
  * curves are rebuilt at a lower resolution, then the wood gets whatever the triangle budget leaves;
  * materials get stable names: BellPepperLeaf, BellPepperFruit, BellPepperStem.
The whole plant is scaled uniformly to HEIGHT metres (the Farm beds space peppers 0.66 m apart); the stem base stays
at the origin.

Wind (see Assets/_Project/Shaders/LeafWind.hlsl): only the leaf blades move in Unity; wood, petioles, fruit and
flowers stay still. Each leaf rotates about its base (the petiole's end), so the blade never leaves its petiole.
UV channel 1 ("Wind") holds u = leaf index + 1 (0 = not a leaf) and v = 1 on the card vertex nearest the leaf's
pivot. BellPepper_Wind.bytes is the version-2 format read by Assets/_Project/Scripts/Editor/LeafWindBaker.cs:
    header  b"BTPW", uint32 version = 2, uint32 leaves, uint32 keys, float32 loop seconds, uint32 anchor sets (2)
    per leaf: pivot xyz, anchor xyz for LOD0 and LOD1, then keys x (w, x, y, z): world rotation relative to frame 1
The key count is the smallest that keeps linear (nlerp) blending within MAX_BLEND_ERROR_DEG of Blender's curves.

Run:  blender -b "<the .blend>" --factory-startup -P Tools/blender_bell_pepper_plant.py -- [<out_dir>] [--height H]
      (default out_dir Assets/_Project/Models/BellPepperPlant, default height 0.9 m)
The source .blend is only read; nothing is saved back.
"""
import json
import math
import os
import struct
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DEFAULT = os.path.join(ROOT, "Assets", "_Project", "Models", "BellPepperPlant")
NAME = "BellPepper"
HEIGHT_DEFAULT = 0.9

TRIS_LOD0, TRIS_LOD1 = 12000, 4000      # whole-plant budgets; the wood gets what the other parts leave
LEAF_GRID = ((10, 6), (4, 2))           # card cells along the leaf's length and width, per LOD
SPRITE = 1024                           # leaf sprite, longest side
ATLAS = 1024                            # 2 x 2: red GLB body, green body, yellow body, flower swatches
BODY_TRIS = (300, 80)
FLOWER_TRIS = {"petals": (24, 0), "anther": (12, 0)}
WOOD_MIN_TRIS = (12, 6)
# Curve rebuild per LOD: (resolution_u cap, bevel_resolution). A round bevel has 4 + 2 x bevel_resolution sides.
CURVE_RES = ((6, 1), (2, 0))
KEY_CANDIDATES = (8, 12, 16, 24, 32, 48, 64, 96)
MAX_BLEND_ERROR_DEG = 1.0
MIN_KEYS = 16                           # a floor, so a very smooth curve still loops without visible kinks

# Plant collections of the blend (prefix match; the names carry decorative bullets).
PLANT_COLLECTIONS = ("01", "02", "03", "04")
# Source material name prefixes -> part kind.
KINDS = [("Spotted leaf", "leaf"), ("Bell_pepper_multicolor", "body"), ("Green pepper", "body"), ("Yellow pepper", "body"),
         ("Red pepper", "body"), ("Stem", "wood"), ("Calyx", "wood"), ("Petals", "petals"), ("Anthers", "anther")]
# Linear base colours of the blend's solid materials; the swatches are written to the (sRGB) atlas.
PETAL_RGB, ANTHER_RGB = (0.86, 0.89, 0.75), (0.61, 0.37, 0.025)
STEM_RGB = (0.13, 0.235, 0.025)


# ---------------------------------------------------------------------------------------------------------- helpers

def tris(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def override(objs, active):
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


def source_material(obj):
    return next((s.material for s in obj.material_slots if s.material), None)


def kind_of(obj):
    mat = source_material(obj)
    name = mat.name if mat else ""
    for prefix, kind in KINDS:
        if name.startswith(prefix):
            return kind
    raise RuntimeError(f"unclassified object {obj.name!r} with material {name!r}")


def weld(mesh):
    """Merges coincident vertices, so the decimator does not tear the mesh open along UV and normal seams."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(mesh)
    size = max((max(v.co[a] for v in bm.verts) - min(v.co[a] for v in bm.verts)) for a in range(3)) if bm.verts else 0.0
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(size, 1e-9) * 1e-5)
    bm.to_mesh(mesh)
    bm.free()


def decimate_copy(mesh, target):
    """Collapse-decimated copy of mesh with about target triangles (a plain copy when it is already below)."""
    copy = mesh.copy()
    weld(copy)
    ratio = target / max(tris(copy), 1)
    if ratio >= 1.0:
        return copy
    tmp = bpy.data.objects.new("DecimateTmp", copy)
    bpy.context.scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    with override([tmp], tmp):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(tmp)
    return copy


def texture_uv(mesh):
    layer = next((l for l in mesh.uv_layers if l.active_render), mesh.uv_layers[0] if mesh.uv_layers else None)
    uv = np.zeros(len(mesh.loops) * 2, dtype=np.float32)
    if layer is not None:
        layer.data.foreach_get("uv", uv)
    return uv


def set_uvs(mesh, uv=None, wind=None):
    """Exactly two UV layers, UVMap (texture) and Wind, so joined meshes merge by name."""
    loops = len(mesh.loops)
    uv = texture_uv(mesh) if uv is None else uv
    while mesh.uv_layers:
        mesh.uv_layers.remove(mesh.uv_layers[0])
    mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uv)
    mesh.uv_layers.new(name="Wind").data.foreach_set("uv", wind if wind is not None else np.zeros(loops * 2, dtype=np.float32))
    mesh.uv_layers.active_index = 0
    mesh.uv_layers["UVMap"].active_render = True


def place(matrix, mesh, name, material, uv=None, wind=None):
    """World-space (transform applied), single-user copy of mesh with one material."""
    data = mesh.copy()
    data.transform(matrix)
    if matrix.determinant() < 0:
        data.flip_normals()
    set_uvs(data, uv, wind)
    data.materials.clear()
    data.materials.append(material)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def join(objs, name):
    with override(objs, objs[0]):
        bpy.ops.object.join()
    obj = objs[0]
    obj.name = obj.data.name = name
    return obj


def material(name, color=None):
    m = bpy.data.materials.new(name)
    assert m.name == name, f"material name {name} is taken"
    if not m.node_tree:          # Blender 5 materials always have nodes (use_nodes is deprecated)
        m.use_nodes = True
    if color is not None:
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    return m


def base_color_node(mat):
    """The image (and colour ramp, if any) feeding the Principled base colour."""
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    stack = [l.from_node for l in bsdf.inputs["Base Color"].links]
    ramp = None
    seen = set()
    while stack:
        node = stack.pop()
        if node.name in seen:
            continue
        seen.add(node.name)
        if node.type == "VALTORGB":
            ramp = node.color_ramp
        if node.type == "TEX_IMAGE" and node.image:
            return node, ramp
        for socket in node.inputs:
            stack += [l.from_node for l in socket.links]
    raise RuntimeError(f"{mat.name}: no base colour image")


def save_png(pixels, width, height, path):
    """Writes float RGBA pixels (bottom row first, as Blender stores them) to a PNG, atomically."""
    img = bpy.data.images.new(os.path.basename(path), width, height, alpha=True)
    img.pixels.foreach_set(pixels.astype(np.float32).ravel())
    tmp = path + ".tmp.png"
    img.filepath_raw = tmp
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    os.replace(tmp, path)


def image_pixels(image, size=None):
    """RGBA float pixels (as stored, sRGB for colour maps) of a copy of image, optionally resized to size x size."""
    copy = image.copy()
    if size:
        copy.scale(size, size)
    w, h = copy.size
    px = np.empty(w * h * 4, dtype=np.float32)
    copy.pixels.foreach_get(px)
    bpy.data.images.remove(copy)
    return px.reshape(h, w, 4)


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def ramped(pixels, ramp):
    """The shader's colour ramp on an sRGB map: linear luminance (Rec. 709) as the factor, linear colour out."""
    lum = to_linear(pixels[..., :3]) @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    table = np.array([ramp.evaluate(float(x))[:3] for x in np.linspace(0.0, 1.0, 1024)], dtype=np.float32)
    idx = np.clip(np.round(lum * 1023).astype(int), 0, 1023)
    out = np.empty_like(pixels)
    out[..., :3] = to_srgb(table[idx])
    out[..., 3] = 1.0
    return out


def curve_mesh(obj, res_cap, bevel_res, depsgraph):
    """Mesh of a curve object (object space) rebuilt at a lower resolution; the source curve is restored after."""
    data = obj.data
    saved = (data.resolution_u, data.bevel_resolution, data.render_resolution_u)
    data.resolution_u = max(1, min(res_cap, data.resolution_u))
    data.render_resolution_u = 0
    data.bevel_resolution = min(bevel_res, data.bevel_resolution)
    depsgraph.update()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), depsgraph=depsgraph)
    data.resolution_u, data.bevel_resolution, data.render_resolution_u = saved
    depsgraph.update()
    return mesh


# ---------------------------------------------------------------------------------------------------------- export

def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    height = HEIGHT_DEFAULT
    if "--height" in argv:
        i = argv.index("--height")
        height = float(argv[i + 1])
        argv = argv[:i] + argv[i + 2:]
    out = os.path.abspath(argv[0]) if argv else OUT_DEFAULT
    export(out, height)


def export(out, height):
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    depsgraph = bpy.context.evaluated_depsgraph_get()
    report = {"source": os.path.basename(bpy.data.filepath)}

    objs = [o for o in bpy.data.objects
            if o.type in ("MESH", "CURVE") and not o.hide_render
            and any(c.name.startswith(PLANT_COLLECTIONS) for c in o.users_collection)]
    parts = {}
    for o in objs:
        parts.setdefault(kind_of(o), []).append(o)
    for k in parts:
        parts[k].sort(key=lambda o: o.name)
    leaves = parts["leaf"]
    assert all(o.type == "MESH" for o in leaves), "a leaf is not a mesh"
    report["source_parts"] = {k: dict(objects=len(v), meshes=sum(o.type == "MESH" for o in v),
                                      mesh_tris=sum(tris(o.data) for o in v if o.type == "MESH")) for k, v in sorted(parts.items())}

    # ------------------------------------------------------------ scale: the plant to `height`, stem base at the origin
    scene.frame_set(scene.frame_start)
    zs = []
    for o in objs:
        zs += [(o.matrix_world @ Vector(c)).z for c in o.bound_box]
    source_height = max(zs) - min(0.0, min(zs))
    scale = height / source_height
    S = Matrix.Scale(scale, 4)
    report["scale"] = dict(source_height_m=round(source_height, 4), height_m=height, factor=round(scale, 5))

    # ------------------------------------------------------------ wind: pivots, keys, key count from the blend error
    first = scene.frame_start
    span = scene.frame_end - scene.frame_start + 1          # 192 frames; frame 193 is the loop-closure pose
    last = first + span
    fps = scene.render.fps / scene.render.fps_base
    seconds = span / fps
    n = len(leaves)
    assert all(o.parent is not None and o.parent.animation_data and o.parent.animation_data.action for o in leaves), \
        "leaf without an animated wind pivot"

    def sample(frame, sub=0.0):
        scene.frame_set(frame, subframe=sub)
        return [S @ o.matrix_world for o in leaves]

    frames = {}
    for f in range(first, last + 1):
        frames[(f, 0.0)] = sample(f)
        if f < last:
            frames[(f, 0.5)] = sample(f, 0.5)
    mats0 = sample(first)
    inv0 = [m.inverted() for m in mats0]
    # Pivot: the leaf's own origin, its base, where the petiole ends (checked below).
    pivots = [m.translation.copy() for m in mats0]

    def rel(m, i):
        return (m[i] @ inv0[i]).to_3x3().normalized().to_quaternion().normalized()

    rotations = {t: [rel(m, i) for i in range(n)] for t, m in frames.items()}
    times = sorted(rotations)
    # How far the true base travels (branch sway, leaf rig); Unity keeps it on the still petiole instead.
    drift = max(((m[i] @ inv0[i]) @ pivots[i] - pivots[i]).length for m in frames.values() for i in range(n))
    loop_error = max(math.degrees(rotations[(last, 0.0)][i].angle) for i in range(n))
    max_angle = max(math.degrees(q[i].angle) for q in rotations.values() for i in range(n))

    def phase(t):
        return (t[0] + t[1] - first) / span          # 0..1 over the loop

    def keyed(i, keys):
        return [rotations[min(times, key=lambda t: abs(phase(t) - k / keys))][i].copy() for k in range(keys)]

    def blend_error(keys):
        worst = 0.0
        for i in range(n):
            q = keyed(i, keys)
            for t in times:
                x = phase(t) * keys
                k0 = min(int(math.floor(x)), keys - 1)
                a, b = q[k0], q[(k0 + 1) % keys]
                if a.dot(b) < 0:
                    b = -b
                f = x - k0
                blend = Quaternion([a[j] * (1 - f) + b[j] * f for j in range(4)]).normalized()
                worst = max(worst, math.degrees(blend.rotation_difference(rotations[t][i]).angle))
        return worst

    errors = {}
    keys = None
    for k in KEY_CANDIDATES:
        if (span * 2) % k:          # keys must land on sampled (half-)frames
            continue
        errors[k] = round(blend_error(k), 4)
        if k >= MIN_KEYS and errors[k] <= MAX_BLEND_ERROR_DEG:
            keys = k
            break
    assert keys is not None, f"no key count keeps the blend error within {MAX_BLEND_ERROR_DEG} deg: {errors}"
    key_rot = []
    for i in range(n):
        q = keyed(i, keys)
        if q[0].w < 0:
            q[0].negate()
        for k in range(1, keys):
            if q[k].dot(q[k - 1]) < 0:
                q[k].negate()
        key_rot.append(q)
    scene.frame_set(first)
    depsgraph.update()

    # ------------------------------------------------------------ leaf sprite and curved cards
    leaf_mesh = leaves[0].data
    assert all(o.data == leaf_mesh for o in leaves), "leaves use more than one mesh"
    leaf_mat = source_material(leaves[0])
    co = np.empty(len(leaf_mesh.vertices) * 3)
    leaf_mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    centre = co.mean(0)
    _, sv, axes = np.linalg.svd((co - centre)[:: max(1, len(co) // 20000)], full_matrices=False)
    ax_u, ax_v, ax_n = (Vector(a) for a in axes)
    # The sprite looks at the side that faces up on the plant (on average over the instances).
    up = sum((m.to_3x3() @ ax_n).normalized().z for m in mats0)
    if up < 0:
        ax_n = -ax_n
    ax_v = ax_n.cross(ax_u)
    local = np.stack([co @ np.array(ax_u), co @ np.array(ax_v), co @ np.array(ax_n)], 1)
    lo, hi = local.min(0), local.max(0)
    ext_u, ext_v = (hi[0] - lo[0]) * 1.04, (hi[1] - lo[1]) * 1.04
    width = SPRITE
    sprite_h = SPRITE // 2 if ext_v / ext_u <= 0.65 else SPRITE   # 1024 x 512 for the usual ~0.6 leaf aspect
    span_u = max(ext_u, ext_v * width / sprite_h)
    span_v = span_u * sprite_h / width
    mid = Vector((lo + hi) / 2)
    origin = ax_u * mid.x + ax_v * mid.y      # sprite centre on the leaf's mid-plane (n = 0)
    report["leaf"] = dict(source_tris=tris(leaf_mesh), instances=n, flatness=round(float(sv[2] / sv[0]), 5),
                          sprite=[width, sprite_h], card_span_local=[round(span_u, 4), round(span_v, 4)])

    bake = bpy.data.scenes.new("LeafBake")
    bake.render.engine = "BLENDER_WORKBENCH"
    bake.display.shading.light = "FLAT"
    bake.display.shading.color_type = "TEXTURE"
    bake.display.render_aa = "16"
    bake.render.film_transparent = True
    bake.render.resolution_x, bake.render.resolution_y = width, sprite_h
    bake.render.resolution_percentage = 100
    bake.render.image_settings.file_format = "PNG"
    bake.render.image_settings.color_mode = "RGBA"
    bake.view_settings.view_transform = "Standard"
    cam_data = bpy.data.cameras.new("LeafCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = span_u
    cam = bpy.data.objects.new("LeafCam", cam_data)
    bake.collection.objects.link(cam)
    bake.camera = cam
    depth = float(hi[2] - lo[2]) + span_u
    cm = Matrix((ax_u, ax_v, ax_n)).transposed().to_4x4()
    cm.translation = origin + ax_n * (float(hi[2]) + span_u)
    cam.matrix_world = cm
    cam_data.clip_end = depth * 3
    sprite_obj = bpy.data.objects.new("LeafSprite", leaf_mesh)
    bake.collection.objects.link(sprite_obj)
    leaf_mat.node_tree.nodes.active = base_color_node(leaf_mat)[0]
    sprite_path = os.path.join(out, f"{NAME}Leaf.png")
    bake.render.filepath = sprite_path + ".tmp.png"
    with bpy.context.temp_override(scene=bake):
        bpy.ops.render.render(write_still=True, scene=bake.name)
    os.replace(sprite_path + ".tmp.png", sprite_path)
    bpy.data.objects.remove(sprite_obj)
    bpy.data.scenes.remove(bake)
    sprite = bpy.data.images.load(sprite_path)
    alpha = image_pixels(sprite)[..., 3]          # [row from bottom, column]
    bpy.data.images.remove(sprite)
    report["leaf"]["sprite_coverage"] = round(float((alpha > 0.5).mean()), 3)

    bvh = BVHTree.FromPolygons([tuple(v) for v in local.tolist()], [tuple(p.vertices) for p in leaf_mesh.polygons])

    def card(cells_u, cells_v):
        """Grid card in leaf-local space over the sprite rectangle, following the leaf surface, empty cells dropped."""
        u0, v0 = mid.x - span_u / 2, mid.y - span_v / 2
        top = float(hi[2]) + 1.0
        verts, uvs = [], []
        for j in range(cells_v + 1):
            for i in range(cells_u + 1):
                s, t = i / cells_u, j / cells_v
                x, y = u0 + s * span_u, v0 + t * span_v
                hit = bvh.ray_cast(Vector((x, y, top)), Vector((0, 0, -1)))
                z = hit[0].z if hit[0] is not None else bvh.find_nearest(Vector((x, y, 0.0)))[0].z
                verts.append(ax_u * x + ax_v * y + ax_n * z)
                uvs.append((s, t))
        faces = []
        for j in range(cells_v):
            for i in range(cells_u):
                # Keep the cell if the sprite (or a neighbouring cell, for filtering margins) has leaf pixels in it.
                r0 = max(0, int((j - 0.25) / cells_v * sprite_h)); r1 = min(sprite_h, int(math.ceil((j + 1.25) / cells_v * sprite_h)))
                c0 = max(0, int((i - 0.25) / cells_u * width)); c1 = min(width, int(math.ceil((i + 1.25) / cells_u * width)))
                if (alpha[r0:r1, c0:c1] > 0.02).any():
                    a = j * (cells_u + 1) + i
                    faces.append((a, a + 1, a + cells_u + 2, a + cells_u + 1))
        mesh = bpy.data.meshes.new(f"LeafCard{cells_u}x{cells_v}")
        mesh.from_pydata([tuple(v) for v in verts], [], faces)
        mesh.validate()
        uv = np.array([uvs[mesh.loops[l].vertex_index] for l in range(len(mesh.loops))], dtype=np.float32).ravel()
        mesh.uv_layers.new(name="UVMap").data.foreach_set("uv", uv)
        unused = set(range(len(verts))) - {v for f in faces for v in f}
        if unused:
            import bmesh
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bm.verts.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[bm.verts[v] for v in unused], context="VERTS")
            bm.to_mesh(mesh)
            bm.free()
        return mesh

    cards = [card(*LEAF_GRID[0]), card(*LEAF_GRID[1])]

    # ------------------------------------------------------------ materials and the fruit / flower atlas
    leaf_material = material(f"{NAME}Leaf")
    fruit_material = material(f"{NAME}Fruit")
    stem_material = material(f"{NAME}Stem", STEM_RGB)

    # Quadrants (column, row from the bottom): (0, 0) red GLB body, (1, 0) green body, (0, 1) yellow body,
    # (1, 1) flower swatches: petal ivory on its left half, anther pollen on its right half.
    def body_quadrant(obj):
        mname = source_material(obj).name
        return (1, 0) if mname.startswith("Green") else (0, 1) if mname.startswith("Yellow") else (0, 0)

    half = ATLAS // 2
    atlas = np.zeros((ATLAS, ATLAS, 4), dtype=np.float32)
    atlas[..., 3] = 1.0
    filled = {}
    for o in parts.get("body", []):
        q = body_quadrant(o)
        if q in filled:
            continue
        node, ramp = base_color_node(source_material(o))
        px = image_pixels(node.image, half)
        px = ramped(px, ramp) if ramp is not None else px
        px[..., 3] = 1.0
        atlas[q[1] * half:(q[1] + 1) * half, q[0] * half:(q[0] + 1) * half] = px
        filled[q] = f"{node.image.name}{' + ramp' if ramp else ''}"
    swatch = atlas[half:, half:]
    swatch[:, : half // 2, :3] = to_srgb(np.array(PETAL_RGB, dtype=np.float32))
    swatch[:, half // 2:, :3] = to_srgb(np.array(ANTHER_RGB, dtype=np.float32))
    atlas_path = os.path.join(out, f"{NAME}Fruit.png")
    save_png(atlas, ATLAS, ATLAS, atlas_path)
    report["fruit_atlas_quadrants"] = {f"{q}": v for q, v in sorted(filled.items())}
    pad = 1.0 / ATLAS

    def atlas_uv(mesh, quadrant):
        uv = texture_uv(mesh).reshape(-1, 2)
        outside = float(((uv < -0.001) | (uv > 1.001)).any(axis=1).mean()) if len(uv) else 0.0
        uv = np.clip(uv, 0.0, 1.0)
        # Padding so bilinear filtering stays inside the quadrant.
        uv = pad + uv * (0.5 - 2 * pad) + np.array(quadrant, dtype=np.float32) * 0.5
        return uv.astype(np.float32).ravel(), outside

    def swatch_uv(mesh, kind):
        u = 0.625 if kind == "petals" else 0.875
        return np.tile(np.array([u, 0.75], dtype=np.float32), len(mesh.loops))

    # ------------------------------------------------------------ build the LODs
    shared = {}

    def lighter(mesh, target):
        key = (mesh.name, target)
        if key not in shared:
            shared[key] = decimate_copy(mesh, target)
        return shared[key]

    curve_cache = {}

    def source_mesh(o, lod):
        """Object-space mesh of a part: the mesh itself, or the curve rebuilt at this LOD's resolution."""
        if o.type == "MESH":
            return o.data
        key = (o.name, lod)
        if key not in curve_cache:
            curve_cache[key] = curve_mesh(o, *CURVE_RES[lod], depsgraph)
        return curve_cache[key]

    uv_outside = 0.0

    def build(lod):
        nonlocal uv_outside
        built = []
        anchors = []
        counts = {}

        def add(kind, obj):
            counts[kind] = counts.get(kind, 0) + tris(obj.data)
            built.append(obj)

        # Leaves: card per instance, tagged with its index and the vertex nearest its pivot.
        cardm = cards[lod]
        for i, o in enumerate(leaves):
            m = mats0[i]
            ccos = [m @ v.co for v in cardm.vertices]
            base = min(range(len(ccos)), key=lambda v: (ccos[v] - pivots[i]).length)
            anchors.append(ccos[base])
            wind = np.array([(i + 1.0, 1.0 if l.vertex_index == base else 0.0) for l in cardm.loops], dtype=np.float32).ravel()
            add("leaves", place(m, cardm, f"Leaf{lod}", leaf_material, None, wind))
        # Peppers: decimated shared meshes, UVs into their atlas quadrant.
        for o in parts.get("body", []):
            mesh = lighter(source_mesh(o, lod), BODY_TRIS[lod])
            uv, outside = atlas_uv(mesh, body_quadrant(o))
            uv_outside = max(uv_outside, outside)
            add("fruit", place(S @ o.matrix_world, mesh, f"Fruit{lod}", fruit_material, uv))
        # Flowers: petals and anthers, flat swatches of the same atlas.
        for kind in ("petals", "anther"):
            target = FLOWER_TRIS[kind][lod]
            if target <= 0:
                continue
            for o in parts.get(kind, []):
                mesh = lighter(source_mesh(o, lod), target)
                if len(mesh.polygons):
                    add("flowers", place(S @ o.matrix_world, mesh, f"Flower{lod}", fruit_material, swatch_uv(mesh, kind)))
        # Wood (stem, branches, petioles, stalks, calyces, sepals): whatever the budget leaves, in proportion.
        wood = parts.get("wood", [])
        budget = (TRIS_LOD0, TRIS_LOD1)[lod] - sum(counts.values())
        total = sum(tris(source_mesh(o, lod)) for o in wood)
        ratio = max(0.02, min(1.0, budget / max(total, 1)))
        for o in wood:
            src = source_mesh(o, lod)
            target = max(WOOD_MIN_TRIS[lod], int(tris(src) * ratio))
            mesh = decimate_copy(src, target)
            if len(mesh.polygons):
                add("wood", place(S @ o.matrix_world, mesh, f"Wood{lod}", stem_material))
            bpy.data.meshes.remove(mesh)
        report[f"lod{lod}_parts_tris"] = counts
        report[f"lod{lod}_wood_ratio"] = round(ratio, 4)
        report[f"lod{lod}_wood_source_tris"] = total
        return join(built, f"{NAME}_LOD{lod}"), anchors

    lod0, anchors0 = build(0)
    lod1, anchors1 = build(1)
    report["fruit_uv_outside_unit_square"] = uv_outside
    for obj in (lod0, lod1):
        obj.data.shade_smooth()
        report[obj.name] = dict(tris=tris(obj.data), verts=len(obj.data.vertices),
                                materials=[s.material.name for s in obj.material_slots if s.material],
                                uv_layers=[l.name for l in obj.data.uv_layers])
    corners = [lod0.matrix_world @ Vector(c) for c in lod0.bound_box]
    report["lod0_bbox_blender"] = dict(min=[round(min(c[a] for c in corners), 4) for a in range(3)],
                                       max=[round(max(c[a] for c in corners), 4) for a in range(3)])

    # ------------------------------------------------------------ wind data and the pivot report
    nearest_vertex = 0.0
    for m, p in zip(mats0, pivots):
        lp = m.inverted() @ p
        d = np.linalg.norm(co - np.array(lp), axis=1).min()
        nearest_vertex = max(nearest_vertex, float(d) * m.to_scale()[0])
    petiole_gap = 0.0
    for i, o in enumerate(leaves):
        number = o.name.split()[1]
        pet = bpy.data.objects.get(f"Petiole {number}")
        if pet is not None and pet.type == "CURVE":
            end = S @ pet.matrix_world @ pet.data.splines[0].bezier_points[-1].co
            petiole_gap = max(petiole_gap, (end - pivots[i]).length)
    report["pivot_check"] = dict(
        rotation_centre="leaf origin (its base, the petiole end)",
        petiole_end_to_pivot_max_m=round(petiole_gap, 6),
        source_leaf_vertex_nearest_pivot_max_m=round(nearest_vertex, 6),
        true_base_travel_max_m=round(drift, 5),
        card_anchor_to_pivot_max_m=[round(max((a - p).length for a, p in zip(anchors, pivots)), 5) for anchors in (anchors0, anchors1)])
    report["wind"] = dict(leafCount=n, keyCount=keys, seconds=seconds, fps=fps, frameRange=[first, last],
                          blendErrorDegByKeys=errors, keyBlendErrorDeg=errors[keys], maxAngleDeg=round(max_angle, 3),
                          loopErrorDeg=loop_error)
    wind_path = os.path.join(out, f"{NAME}_Wind.bytes")
    with open(wind_path, "wb") as f:
        f.write(b"BTPW")
        f.write(struct.pack("<IIIfI", 2, n, keys, seconds, 2))
        for i in range(n):
            f.write(struct.pack("<3f", *pivots[i]))
            f.write(struct.pack("<3f", *anchors0[i]))
            f.write(struct.pack("<3f", *anchors1[i]))
            for q in key_rot[i]:
                f.write(struct.pack("<4f", q.w, q.x, q.y, q.z))

    for o in bpy.data.objects:
        o.select_set(o in (lod0, lod1))
    fbx = os.path.join(out, f"{NAME}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx + ".tmp.fbx", use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    os.replace(fbx + ".tmp.fbx", fbx)
    report["files_bytes"] = {os.path.basename(p): os.path.getsize(p) for p in (fbx, sprite_path, atlas_path, wind_path)}
    with open(os.path.join(out, f"{NAME}_export.json"), "w") as f:
        json.dump(report, f, indent=2)
    print("BELL_PEPPER_EXPORT", json.dumps(report))


main()