"""Exports the nine disease tomato plants (SourceAssets/Tomato_Plant/.../models/NN_<Disease>.glb) as VR-ready FBXs
plus a per-leaf wind table each, so the GLBs' 8-second leaf breeze survives into Unity.

Each GLB is a ~1.75 m plant in metres: 31 instances (17 for mosaic virus) of one supplied Meshy disease leaf
(~175k triangles each), 20 instances of the supplied tomato fruit (body, calyx, stem; red, green and orange
textures), closed stem/branch tubes, flowers, a soil mound, loose soil crumbs and stem hairs. Every leaf hangs
from an animated "3D wind pivot" empty placed on the leaf's base (the leaf node's own origin is its centre).

For Quest the plant has to fit ~12k triangles (LOD0) and ~4k (LOD1), so:
  * leaves become curved cards: the leaf is rendered once, top down, to a cut-out sprite (its exact disease
    texture), and a grid card that follows the leaf's surface (ray cast heights) replaces each instance; grid
    cells the sprite leaves empty are dropped;
  * fruit, calyx, fruit stem, petals, stems and the soil mound are decimated; stem hairs and soil crumbs dropped;
  * the four fruit textures (red, green, orange body; calyx) share one 1024 atlas, so one fruit material;
  * materials get stable names: TomatoLeaf_<diseaseId>, TomatoStem, TomatoFruit, TomatoFlower, TomatoSoil.

Wind (as Tools/blender_cherry_tree.py, see Assets/_Project/Shaders/LeafWind.hlsl): UV channel 1 ("Wind") holds
u = leaf index + 1 (0 = not a leaf) and v = 1 on the card vertex nearest the leaf's pivot. Tomato_<id>_Wind.bytes
is the version-2 format read by Assets/_Project/Scripts/Editor/LeafWindBaker.cs, little-endian:
    header  b"BTPW", uint32 version = 2, uint32 leaves, uint32 keys, float32 loop seconds, uint32 anchor sets (2)
    per leaf: pivot xyz (the rotation centre), anchor xyz for LOD0 and LOD1 (the tagged vertex, which pairs Unity
              and Blender positions for the axis fit), then keys x (w, x, y, z): world rotation relative to frame 0
The key count is the smallest that keeps linear (nlerp) blending within MAX_BLEND_ERROR_DEG of Blender's curves.

Run one:  blender -b -P Tools/blender_tomato_plants.py -- <glb> <diseaseId> <out_dir>
Run all:  blender -b -P Tools/blender_tomato_plants.py -- --all [<models_dir>] [<out_dir>] [--jobs N]
          (one Blender process per plant; defaults: SourceAssets/Tomato_Plant/.../models, Assets/_Project/Models/TomatoPlant)
Sources are only read; nothing is saved back.
"""
import json
import math
import os
import struct
import subprocess
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "SourceAssets", "Tomato_Plant", "Tomato_3D_Assets", "Tomato_3D_Models_9_Diseases", "models")
OUT_DEFAULT = os.path.join(ROOT, "Assets", "_Project", "Models", "TomatoPlant")
# GLB file -> diseaseId of Assets/_Project/Scripts/Runtime/Plants/DiseaseClasses.cs.
PLANTS = [
    ("01_Bacterial_Spot.glb", "bacterial_spot"),
    ("02_Early_Blight.glb", "early_blight"),
    ("03_Late_Blight.glb", "late_blight"),
    ("04_Leaf_Mold.glb", "leaf_mold"),
    ("05_Mosaic_Virus.glb", "mosaic_virus"),
    ("06_Septoria_Leaf_Spot.glb", "septoria_leaf_spot"),
    ("07_Target_Spot.glb", "target_spot"),
    ("08_Two-Spotted_Spider_Mite.glb", "spider_mites"),
    ("09_Yellow_leaf_Curl_Virus.glb", "yellow_leaf_curl_virus"),
]

TRIS_LOD0, TRIS_LOD1 = 12000, 4000      # whole-plant budgets; the stems get what the other parts leave
LEAF_GRID = ((10, 6), (4, 2))           # card cells along the leaf's length and width, per LOD
SPRITE = 1024                           # leaf sprite, longest side
FRUIT_ATLAS = 1024                      # 2 x 2 atlas: red, green, orange body; calyx
FRUIT_TRIS = {"body": (72, 24), "calyx": (20, 0), "stem": (8, 0)}   # ~100 per fruit at LOD0
FLOWER_TRIS = {"petals": (40, 0), "anther": (12, 0)}
SOIL_TRIS = (420, 120)
STEM_MIN_TRIS = (16, 8)
KEY_CANDIDATES = (8, 12, 16, 24, 32, 48, 64, 96)
MAX_BLEND_ERROR_DEG = 1.0
MIN_KEYS = 16                           # a floor, so a very smooth curve still loops without visible kinks

# Source material name prefixes -> part kind.
KINDS = [("LEAF |", "leaf"), ("FRUIT | supplied calyx", "calyx"), ("FRUIT | supplied stem", "fruitstem"),
         ("FRUIT |", "body"), ("PBR EXPORT | Green living stem", "stem"), ("Flower | golden anther", "anther"),
         ("Flower |", "petals"), ("PBR EXPORT | Dark loam", "soil"), ("Soil crumbs", "drop"), ("Trichomes", "drop")]
# Linear colours from the GLBs (GLB_verification.json); the Unity materials take them as sRGB (.gamma there).
STEM_RGB, SOIL_RGB = (0.12, 0.24, 0.025), (0.035, 0.014, 0.004)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if argv and argv[0] == "--all":
        run_all(argv[1:])
    elif len(argv) == 3:
        export_plant(os.path.abspath(argv[0]), argv[1], os.path.abspath(argv[2]))
    else:
        raise SystemExit(__doc__)


def run_all(args):
    jobs = 1
    if "--jobs" in args:
        i = args.index("--jobs")
        jobs = int(args[i + 1])
        args = args[:i] + args[i + 2:]
    models = os.path.abspath(args[0]) if len(args) > 0 else MODELS
    out = os.path.abspath(args[1]) if len(args) > 1 else OUT_DEFAULT
    os.makedirs(out, exist_ok=True)
    pending = list(PLANTS)
    running = []
    failed = []
    while pending or running:
        while pending and len(running) < jobs:
            glb, disease = pending.pop(0)
            # Logs go to Tools/logs (git-ignored), not next to the exports where Unity would import them.
            logs = os.path.join(ROOT, "Tools", "logs")
            os.makedirs(logs, exist_ok=True)
            log = open(os.path.join(logs, f"Tomato_{disease}_export.log"), "w")
            cmd = [bpy.app.binary_path, "-b", "--factory-startup", "-P", os.path.abspath(__file__), "--",
                   os.path.join(models, glb), disease, out]
            running.append((disease, subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT), log))
            print("START", disease, flush=True)
        for item in list(running):
            disease, proc, log = item
            if proc.poll() is not None:
                log.close()
                running.remove(item)
                print("DONE" if proc.returncode == 0 else "FAILED", disease, proc.returncode, flush=True)
                if proc.returncode != 0:
                    failed.append(disease)
        time.sleep(1)
    if failed:
        raise SystemExit(f"tomato export failed for: {failed}")


# ---------------------------------------------------------------------------------------------------------- helpers

def tris(mesh):
    return sum(len(p.vertices) - 2 for p in mesh.polygons)


def override(objs, active):
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=objs, selected_editable_objects=objs)


def kind_of(obj):
    names = [s.material.name for s in obj.material_slots if s.material]
    for prefix, kind in KINDS:
        if any(n.startswith(prefix) for n in names):
            return kind
    raise RuntimeError(f"unclassified mesh {obj.name!r} with materials {names}")


def weld(mesh):
    """Merges coincident vertices. glTF splits vertices along UV and normal seams; left split, the decimator treats
    every seam as an open border and moves its two sides apart, which cracks the fruit open. UVs live on the loops,
    so they survive the weld."""
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
    ratio = target / max(tris(mesh), 1)
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


def material(name, color=None, image=None):
    m = bpy.data.materials.new(name)
    assert m.name == name, f"material name {name} is taken"
    if not m.node_tree:          # Blender 5 materials always have nodes (use_nodes is deprecated)
        m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if color is not None:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def base_color_image(mat):
    """The image feeding the Principled base colour (followed upstream through any nodes)."""
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    stack = [l.from_node for l in bsdf.inputs["Base Color"].links]
    seen = set()
    while stack:
        node = stack.pop()
        if node.name in seen:
            continue
        seen.add(node.name)
        if node.type == "TEX_IMAGE" and node.image:
            return node
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
    """RGBA float pixels of a copy of image, optionally resized to size x size (box filtered by Blender)."""
    copy = image.copy()
    if size:
        copy.scale(size, size)
    w, h = copy.size
    px = np.empty(w * h * 4, dtype=np.float32)
    copy.pixels.foreach_get(px)
    bpy.data.images.remove(copy)
    return px.reshape(h, w, 4)


# ---------------------------------------------------------------------------------------------------------- export

def export_plant(glb, disease, out):
    os.makedirs(out, exist_ok=True)
    name = f"Tomato_{disease}"
    report = {"source": os.path.basename(glb), "diseaseId": disease}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    scene = bpy.context.scene

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    parts = {}
    for o in meshes:
        parts.setdefault(kind_of(o), []).append(o)
    for k in parts:
        parts[k].sort(key=lambda o: o.name)
    leaves = parts["leaf"]
    report["source_parts"] = {k: dict(objects=len(v), tris=sum(tris(o.data) for o in v)) for k, v in sorted(parts.items())}
    report["source_tris"] = sum(tris(o.data) for o in meshes)

    # ------------------------------------------------------------ wind: pivots, keys, key count from the blend error
    pivots_obj = [o.parent for o in leaves]
    assert all(p is not None and p.animation_data and p.animation_data.action for p in pivots_obj), "leaf without an animated pivot"
    action = bpy.data.actions[0]
    first, last = (int(round(x)) for x in action.frame_range)
    fps = scene.render.fps / scene.render.fps_base
    span = last - first
    seconds = span / fps
    n = len(leaves)

    def sample(frame, sub=0.0):
        scene.frame_set(frame, subframe=sub)
        return [o.matrix_world.copy() for o in leaves], [p.matrix_world.translation.copy() for p in pivots_obj]

    frames = {}
    for f in range(first, last + 1):
        frames[(f, 0.0)] = sample(f)[0]
        if f < last:
            frames[(f, 0.5)] = sample(f, 0.5)[0]
    mats0, pivots = sample(first)
    inv0 = [m.inverted() for m in mats0]

    def rel(m, i):
        return (m[i] @ inv0[i]).to_3x3().to_quaternion().normalized()

    rotations = {t: [rel(m, i) for i in range(n)] for t, m in frames.items()}
    times = sorted(rotations)
    # Pivot check: the leaf rotates rigidly about its pivot, which stays put and sits on the leaf's base.
    drift = 0.0
    for t, m in frames.items():
        for i in range(n):
            drift = max(drift, ((m[i] @ inv0[i]) @ pivots[i] - pivots[i]).length)
    loop_error = max(math.degrees(rotations[(last, 0.0)][i].angle) for i in range(n))
    max_angle = max(math.degrees(q[i].angle) for q in rotations.values() for i in range(n))

    def phase(t):
        return (t[0] + t[1] - first) / span          # 0..1 over the loop

    def blend_error(keys):
        worst = 0.0
        for i in range(n):
            q = [rotations[min(times, key=lambda t: abs(phase(t) - k / keys))][i] for k in range(keys)]
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
        if keys is None and k >= MIN_KEYS and errors[k] <= MAX_BLEND_ERROR_DEG:
            keys = k
            break
    assert keys is not None, f"no key count keeps the blend error within {MAX_BLEND_ERROR_DEG} deg: {errors}"
    key_rot = []
    for i in range(n):
        q = [rotations[min(times, key=lambda t: abs(phase(t) - k / keys))][i].copy() for k in range(keys)]
        if q[0].w < 0:
            q[0].negate()
        for k in range(1, keys):
            if q[k].dot(q[k - 1]) < 0:
                q[k].negate()
        key_rot.append(q)
    scene.frame_set(first)

    # ------------------------------------------------------------ leaf sprite and curved cards
    leaf_mesh = leaves[0].data
    assert all(o.data == leaf_mesh for o in leaves), "leaves use more than one mesh"
    leaf_mat = leaves[0].material_slots[0].material
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
    height = SPRITE // 2 if ext_v / ext_u <= 0.65 else SPRITE   # 1024 x 512 for the usual ~0.6 leaf aspect
    span_u = max(ext_u, ext_v * width / height)
    span_v = span_u * height / width
    mid = Vector((lo + hi) / 2)
    origin = ax_u * mid.x + ax_v * mid.y      # sprite centre on the leaf's mid-plane (n = 0)
    report["leaf"] = dict(source_tris=tris(leaf_mesh), instances=n, flatness=round(float(sv[2] / sv[0]), 5),
                          thickness_local=round(float(hi[2] - lo[2]), 5), sprite=[width, height],
                          card_span_local=[round(span_u, 4), round(span_v, 4)])

    bake = bpy.data.scenes.new("LeafBake")
    bake.render.engine = "BLENDER_WORKBENCH"
    bake.display.shading.light = "FLAT"
    bake.display.shading.color_type = "TEXTURE"
    bake.display.render_aa = "16"
    bake.render.film_transparent = True
    bake.render.resolution_x, bake.render.resolution_y = width, height
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
    leaf_mat.node_tree.nodes.active = base_color_image(leaf_mat)
    sprite_path = os.path.join(out, f"TomatoLeaf_{disease}.png")
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
                r0 = max(0, int((j - 0.25) / cells_v * height)); r1 = min(height, int(math.ceil((j + 1.25) / cells_v * height)))
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

    # ------------------------------------------------------------ materials and the fruit atlas
    leaf_material = material(f"TomatoLeaf_{disease}", image=None)
    stem_material = material("TomatoStem", STEM_RGB)
    soil_material = material("TomatoSoil", SOIL_RGB)
    flower_material = material("TomatoFlower", (0.9, 0.62, 0.05))
    fruit_material = material("TomatoFruit")

    # Quadrants (column, row from the bottom): red body, green body, orange body, calyx (the supplied leaf texture).
    def body_quadrant(obj):
        mname = obj.material_slots[0].material.name
        return (1, 1) if "green" in mname else (0, 0) if "orange" in mname else (0, 1)

    quadrant_images = {}
    for o in parts.get("body", []) + parts.get("calyx", []):
        mat = o.material_slots[0].material
        q = (1, 0) if kind_of(o) == "calyx" else body_quadrant(o)
        quadrant_images.setdefault(q, base_color_image(mat).image)
    half = FRUIT_ATLAS // 2
    atlas = np.zeros((FRUIT_ATLAS, FRUIT_ATLAS, 4), dtype=np.float32)
    atlas[..., 3] = 1.0
    for (cx, cy), img in quadrant_images.items():
        atlas[cy * half:(cy + 1) * half, cx * half:(cx + 1) * half] = image_pixels(img, half)
        atlas[cy * half:(cy + 1) * half, cx * half:(cx + 1) * half, 3] = 1.0
    save_png(atlas, FRUIT_ATLAS, FRUIT_ATLAS, os.path.join(out, "TomatoFruit.png"))
    report["fruit_atlas_quadrants"] = {f"{q}": img.name for q, img in sorted(quadrant_images.items())}

    def atlas_uv(mesh, quadrant, squeeze=1.0):
        uv = texture_uv(mesh).reshape(-1, 2)
        outside = float(((uv < -0.001) | (uv > 1.001)).any(axis=1).mean())
        uv = np.clip(uv, 0.0, 1.0)
        # Half a texel of padding at 512 per quadrant, so bilinear filtering stays inside the quadrant.
        pad = 1.0 / FRUIT_ATLAS
        uv = 0.5 + (uv - 0.5) * squeeze
        uv = pad + uv * (0.5 - 2 * pad) + np.array(quadrant, dtype=np.float32) * 0.5
        return uv.astype(np.float32).ravel(), outside

    # ------------------------------------------------------------ build the LODs
    shared = {}

    def lighter(mesh, target):
        key = (mesh.name, target)
        if key not in shared:
            shared[key] = decimate_copy(mesh, target)
        return shared[key]

    uv_outside = 0.0

    def build(lod):
        nonlocal uv_outside
        objs = []
        anchors = []
        counts = {}

        def add(kind, obj):
            counts[kind] = counts.get(kind, 0) + tris(obj.data)
            objs.append(obj)

        # Leaves: card per instance, tagged with its index and the vertex nearest its pivot.
        cardm = cards[lod]
        for i, o in enumerate(leaves):
            m = o.matrix_world
            ccos = [m @ v.co for v in cardm.vertices]
            base = min(range(len(ccos)), key=lambda v: (ccos[v] - pivots[i]).length)
            anchors.append(ccos[base])
            wind = np.array([(i + 1.0, 1.0 if l.vertex_index == base else 0.0) for l in cardm.loops], dtype=np.float32).ravel()
            add("leaves", place(m, cardm, f"Leaf{lod}", leaf_material, None, wind))
        # Fruit: decimated shared meshes; UVs into the atlas quadrant of their texture.
        for kind in ("body", "calyx", "fruitstem"):
            target = FRUIT_TRIS["stem" if kind == "fruitstem" else kind][lod]
            if target <= 0:
                continue
            for o in parts.get(kind, []):
                mesh = lighter(o.data, target)
                quadrant = body_quadrant(o) if kind == "body" else (1, 0)
                uv, outside = atlas_uv(mesh, quadrant, 1.0 if kind != "fruitstem" else 0.2)
                uv_outside = max(uv_outside, outside)
                add("fruit", place(o.matrix_world, mesh, f"Fruit{lod}", fruit_material, uv))
        for kind in ("petals", "anther"):
            target = FLOWER_TRIS[kind][lod]
            if target <= 0:
                continue
            for o in parts.get(kind, []):
                add("flowers", place(o.matrix_world, lighter(o.data, target), f"Flower{lod}", flower_material))
        for o in parts.get("soil", []):
            add("soil", place(o.matrix_world, lighter(o.data, SOIL_TRIS[lod]), f"Soil{lod}", soil_material))
        # Stems: whatever the budget leaves, spread in proportion to each tube's source triangles.
        stems = parts.get("stem", [])
        budget = (TRIS_LOD0, TRIS_LOD1)[lod] - sum(counts.values())
        total = sum(tris(o.data) for o in stems)
        ratio = max(0.02, min(1.0, budget / max(total, 1)))
        for o in stems:
            target = max(STEM_MIN_TRIS[lod], int(tris(o.data) * ratio))
            mesh = decimate_copy(o.data, target)
            if len(mesh.polygons):
                add("stems", place(o.matrix_world, mesh, f"Stem{lod}", stem_material))
            bpy.data.meshes.remove(mesh)
        report[f"lod{lod}_parts_tris"] = counts
        report[f"lod{lod}_stem_ratio"] = round(ratio, 4)
        return join(objs, f"{name}_LOD{lod}"), anchors

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
    leaf_origin_offset = max((m.translation - p).length for m, p in zip(mats0, pivots))
    nearest_vertex = 0.0
    for m, p in zip(mats0, pivots):
        inv = m.inverted()
        lp = inv @ p
        d = np.linalg.norm(co - np.array(lp), axis=1).min()
        nearest_vertex = max(nearest_vertex, float(d) * m.to_scale()[0])
    report["pivot_check"] = dict(
        rotation_centre="parent '3D wind pivot' empty",
        leaf_node_origin_to_pivot_max_m=round(leaf_origin_offset, 5),
        source_leaf_vertex_nearest_pivot_max_m=round(nearest_vertex, 6),
        pivot_drift_m=drift,
        card_anchor_to_pivot_max_m=[round(max((a - p).length for a, p in zip(anchors, pivots)), 5) for anchors in (anchors0, anchors1)])
    report["wind"] = dict(leafCount=n, keyCount=keys, seconds=seconds, fps=fps, frameRange=[first, last],
                          blendErrorDegByKeys=errors, keyBlendErrorDeg=errors[keys], maxAngleDeg=round(max_angle, 3),
                          loopErrorDeg=loop_error)
    with open(os.path.join(out, f"{name}_Wind.bytes"), "wb") as f:
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
    fbx = os.path.join(out, f"{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx + ".tmp.fbx", use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    os.replace(fbx + ".tmp.fbx", fbx)
    report["files_bytes"] = {os.path.basename(p): os.path.getsize(p) for p in
                             (fbx, sprite_path, os.path.join(out, "TomatoFruit.png"), os.path.join(out, f"{name}_Wind.bytes"))}
    with open(os.path.join(out, f"{name}_export.json"), "w") as f:
        json.dump(report, f, indent=2)
    print("TOMATO_EXPORT", json.dumps(report))


main()
