"""Optimise the Greenhouse Park asset for the Farm scene's surroundings.

Source (left untouched):
  Assets/Plant_Asstes/Assets_Lab/greenhouse-park-fbx-free/source/Greenhouse.fbx   (135 objects, ~233k triangles)
  Assets/Plant_Asstes/Assets_Lab/greenhouse-park-fbx-free/textures/*.png

Outputs:
  <out-models>/Greenhouse.fbx
      Greenhouse_Frame        white frame (GH_WhiteMetal) with the 21 benches joined in
      Greenhouse_Glass        the glazing (GH_Glass), separate so it can skip shadow casting
      Greenhouse_Floor        the wet-concrete slab, re-UVed planar (one texture repeat per --floor-tile metres)
      Greenhouse_Plants_LOD0  the ~72 potted plants joined and decimated to --plants-tris (seen from 8 m or more)
      Greenhouse_Plants_LOD1  the same decimated further to --plants-lod1-tris (seen from 30 m or more)
  <out-models>/GreenhouseBush.fbx
      Bush_LOD0               the asset's 774-triangle bush clump (all 39 bushes share this geometry)
      Bush_LOD1               only the leaf cards that cover most canopy (by opacity-weighted area), enlarged to
                              keep the silhouette; about half the triangles
  <out-models>/GreenhouseBushLayout.json
      The asset's 39 bush placements around the greenhouse (position, yaw, uniform scale) in Unity space,
      relative to the greenhouse root, so the Unity builder can reproduce the reference belts.
  <out-textures>/GH_*.png    see process_textures(): opacity packed into albedo alpha, DirectX normals converted
                             to OpenGL (green flipped), roughness converted to URP metallic/smoothness masks, a
                             seamless grass tile and a yard texture whose border fades into that tile.

The asset's own 45 m grass plane is dropped: the Farm's 600 m ground covers it, and the Unity builder lays a
yard patch with the reference's dirt-and-grass texture under the greenhouse instead.

Coordinates: the asset's grass sits at z = -0.10, so everything is lifted 0.10 m (grass at z = 0), then turned
180 degrees about Z. After the FBX axis conversion (forward -Z, up Y, as in blender_decimate.py) Unity sees
(x, y, z)_unity = (x, z, y)_source: the greenhouse runs along +Z from its gable end at the origin.

Usage:
  blender -b --factory-startup -P Tools/blender_greenhouse.py -- \
      [--src <Greenhouse.fbx>] [--textures <dir>] [--out-models <dir>] [--out-textures <dir>] \
      [--plants-tris 24000] [--plants-lod1-tris 8000] [--bush-lod1-tris 380] [--skip-models] [--skip-textures]
Defaults are the project paths, so a plain run from the project root rebuilds everything.
Log: Tools/logs/greenhouse.log (appended). Previews: Tools/out/previews/greenhouse_*.png.
"""
import argparse
import datetime
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSET = os.path.join(ROOT, "Assets", "Plant_Asstes", "Assets_Lab", "greenhouse-park-fbx-free")
LOG = os.path.join(ROOT, "Tools", "logs", "greenhouse.log")
LIFT = 0.10  # the asset's grass plane is at z = -0.10


def log(message):
    print(message)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} {message}\n")


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--src", default=os.path.join(ASSET, "source", "Greenhouse.fbx"))
    p.add_argument("--textures", default=os.path.join(ASSET, "textures"))
    p.add_argument("--out-models", default=os.path.join(ROOT, "Assets", "_Project", "Models", "Environment"))
    p.add_argument("--out-textures", default=os.path.join(ROOT, "Assets", "_Project", "Textures", "Environment", "Greenhouse"))
    p.add_argument("--preview-dir", default=os.path.join(ROOT, "Tools", "out", "previews"))
    p.add_argument("--plants-tris", type=int, default=24000)
    p.add_argument("--plants-lod1-tris", type=int, default=8000)
    p.add_argument("--bush-lod1-tris", type=int, default=380)
    p.add_argument("--floor-tile", type=float, default=3.0, help="Metres per concrete texture repeat")
    p.add_argument("--glass-tile", type=float, default=4.0, help="Metres per rough-glass texture repeat")
    # Grass-only square in Ground_BaseColor (pixels from the top-left), the source of the tileable grass.
    p.add_argument("--grass-crop", type=int, nargs=3, default=[1648, 0, 400], metavar=("X", "Y", "SIZE"))
    p.add_argument("--skip-models", action="store_true")
    p.add_argument("--skip-textures", action="store_true")
    return p.parse_args(argv)


# ---------------------------------------------------------------------------------------------- models

def import_model(path):
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)


def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def to_unity(v):
    """Blender (already lifted and turned) -> Unity, for the FBX settings in export_fbx()."""
    return (-v[0], v[2], -v[1])


def bake_join(objs, name, placement):
    """Apply the placement transform and every object transform into the mesh data, then join."""
    for o in objs:
        o.matrix_world = placement @ o.matrix_world
    select_only(objs)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(objs) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = name
    return obj


def strip_attributes(obj):
    """Keep only the first UV map; drop vertex colours and extra UV sets (smaller vertices in Unity)."""
    mesh = obj.data
    while len(mesh.uv_layers) > 1:
        mesh.uv_layers.remove(mesh.uv_layers[-1])
    for attr in list(getattr(mesh, "color_attributes", [])):
        mesh.color_attributes.remove(attr)


def single_material(obj, name):
    for poly in obj.data.polygons:
        poly.material_index = 0
    while len(obj.data.materials) > 1:
        obj.data.materials.pop()
    material = obj.data.materials[0] if obj.data.materials and obj.data.materials[0] else bpy.data.materials.new(name)
    material.name = name
    obj.data.materials.clear()
    obj.data.materials.append(material)


def decimated_copy(source, name, target_tris):
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = obj.data.name = name
    bpy.context.scene.collection.objects.link(obj)
    before = tri_count(obj)
    if target_tris < before:
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = target_tris / before
        mod.use_collapse_triangulate = True
        select_only([obj])
        bpy.ops.object.modifier_apply(modifier=mod.name)
    log(f"LOD {name} tris={tri_count(obj)} (from {before})")
    return obj


def split_by_material(obj, first_material, first_name, second_name):
    """Separate a two-material mesh into one object per material; returns (first, second)."""
    select_only([obj])
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = list(bpy.context.selected_objects)
    if len(parts) != 2:
        raise SystemExit(f"Expected two materials on {obj.name}, got {len(parts)} parts")
    for part in parts:
        used = {part.data.materials[p.material_index].name for p in part.data.polygons}
        name = first_name if used == {first_material} else second_name
        single_material(part, next(iter(used)))
        part.name = part.data.name = name
        log(f"SPLIT {name} tris={tri_count(part)} material={part.data.materials[0].name}")
    first = next(p for p in parts if p.name == first_name)
    second = next(p for p in parts if p.name == second_name)
    return first, second


def box_uv(obj, tile):
    """Box projection: each face takes the two axes across its dominant normal, one repeat per tile metres."""
    mesh = obj.data
    uv = mesh.uv_layers[0].data if mesh.uv_layers else mesh.uv_layers.new(name="UVMap").data
    for poly in mesh.polygons:
        axis = max(range(3), key=lambda i: abs(poly.normal[i]))
        a, b = [(1, 2), (0, 2), (0, 1)][axis]
        for index in poly.loop_indices:
            co = mesh.vertices[mesh.loops[index].vertex_index].co
            uv[index].uv = (co[a] / tile, co[b] / tile)


def planar_uv(obj, tile):
    mesh = obj.data
    uv = mesh.uv_layers[0].data if mesh.uv_layers else mesh.uv_layers.new(name="UVMap").data
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co
        uv[loop.index].uv = (co.x / tile, co.y / tile)


def load_opacity(textures, name, size=256):
    img = bpy.data.images.load(os.path.join(textures, name), check_existing=True)
    img.scale(size, size)
    px = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(size, size, 4)[..., :3].mean(axis=-1)


def card_lod(source, name, textures, target_tris):
    """Distant bush: keep the cards that cover the most canopy, enlarged so the silhouette stays full.

    The clump is ~330 single-quad leaf cards plus ~20 branch strips. Each loose part is scored by its area
    times the share of its UV footprint that is opaque in Bush_Opacity, so mostly-empty branch cards rank low.
    """
    opacity = load_opacity(textures, "Bush_Opacity.png")
    size = opacity.shape[0]
    obj = source.copy()
    obj.data = source.data.copy()
    obj.name = obj.data.name = name
    bpy.context.scene.collection.objects.link(obj)

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    uv_layer = bm.loops.layers.uv.active
    bm.faces.ensure_lookup_table()
    seen, parts = set(), []
    for face in bm.faces:
        if face.index in seen:
            continue
        stack, part = [face], []
        seen.add(face.index)
        while stack:
            f = stack.pop()
            part.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        parts.append(part)

    def coverage(face):
        uvs = [l[uv_layer].uv for l in face.loops]
        centre = sum(uvs, Vector((0.0, 0.0))) / len(uvs)
        samples = [centre] + [(u + centre) * 0.5 for u in uvs]
        values = [opacity[min(size - 1, max(0, int((s.y % 1.0) * size))), min(size - 1, max(0, int((s.x % 1.0) * size)))]
                  for s in samples]
        return sum(values) / len(values)

    scored = []
    for part in parts:
        area = sum(f.calc_area() for f in part)
        visible = sum(f.calc_area() * coverage(f) for f in part)
        tris = sum(len(f.verts) - 2 for f in part)
        scored.append((visible, area, tris, part))
    scored.sort(key=lambda s: s[0], reverse=True)

    total_visible = sum(s[0] for s in scored)
    kept, kept_visible, kept_tris = [], 0.0, 0
    for visible, area, tris, part in scored:
        if kept_tris + tris > target_tris:
            continue
        kept.append(part)
        kept_visible += visible
        kept_tris += tris
    grow = min(1.45, math.sqrt(total_visible / max(kept_visible, 1e-6)))

    kept_faces = {f for part in kept for f in part}
    for part in kept:
        verts = {v for f in part for v in f.verts}
        centre = sum((v.co for v in verts), Vector()) / len(verts)
        for v in verts:
            v.co = centre + (v.co - centre) * grow
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in kept_faces], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    log(f"LOD {name} tris={tri_count(obj)} parts={len(kept)}/{len(parts)} visible-area kept="
        f"{kept_visible / max(total_visible, 1e-6):.0%} cards enlarged x{grow:.2f}")
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
    log(f"EXPORT {path} objects={[o.name for o in objs]} tris={sum(tri_count(o) for o in objs)}")


def render_preview(objs, path, eye, target, lens=35):
    """Workbench render of the given objects, for a quick silhouette check."""
    scene = bpy.context.scene
    for o in scene.objects:
        o.hide_render = o not in objs
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.lens = lens
    cam_data.clip_end = 500
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = eye
    cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x = 960
    scene.render.resolution_y = 540
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    log(f"PREVIEW {path}")


def build_models(args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_model(args.src)
    os.makedirs(args.out_models, exist_ok=True)

    # Unparent with world transforms kept, so every object can be baked independently.
    for o in list(bpy.context.scene.objects):
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    log(f"SOURCE {args.src} objects={len(bpy.context.scene.objects)} meshes={len(meshes)} "
        f"tris={sum(tri_count(o) for o in meshes)}")

    placement = Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation((0.0, 0.0, LIFT))
    structure = [o for o in meshes if o.name == "GreenHouseStructure" or o.name.startswith("Table")]
    floor = [o for o in meshes if o.name == "Floor"]
    pots = [o for o in meshes if o.name.startswith("Pots_")]
    bushes = sorted((o for o in meshes if o.name.startswith("Bush")), key=lambda o: o.name)
    dropped = [o for o in meshes if o not in structure + floor + pots + bushes]
    if len(floor) != 1 or not structure or not pots or not bushes:
        raise SystemExit("Unexpected source layout: GreenHouseStructure, Floor, Pots_* and Bush* are required.")
    log(f"PARTS structure={len(structure)} floor={len(floor)} pots={len(pots)} bushes={len(bushes)} "
        f"dropped={[o.name for o in dropped]}")

    # Bush layout in Unity space. Unity rotation about Y by yaw maps local +Z to (sin yaw, 0, cos yaw).
    flip = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))  # to_unity() as a matrix (a reflection)
    layout = []
    for o in bushes:
        loc = (placement @ o.matrix_world).translation
        # The exported clump already carries the placement's 180-degree turn, so only the bush's own yaw remains.
        _, rot, scale = o.matrix_world.decompose()
        m = flip @ rot.to_matrix() @ flip.inverted()
        yaw = math.degrees(math.atan2(m[0][2], m[2][2]))
        position = to_unity(loc)
        layout.append({"name": o.name, "position": [round(c, 3) for c in position], "yaw": round(yaw, 2),
                       "scale": round(scale.x, 4)})
    with open(os.path.join(args.out_models, "GreenhouseBushLayout.json"), "w", encoding="utf-8") as f:
        json.dump({"source": "greenhouse-park-fbx-free/source/Greenhouse.fbx", "bushes": layout}, f, indent=1)
    log(f"LAYOUT {len(layout)} bushes written")

    # The bush clump: every Bush.* object shares the same 774-triangle geometry, so one template is exported
    # in its own local space (pivot 0.23 m below the grass, as placed in the asset).
    template = bushes[0]
    bush = bpy.data.objects.new("Bush_LOD0", template.data.copy())
    bpy.context.scene.collection.objects.link(bush)
    bush.data.name = "Bush_LOD0"
    bush.matrix_world = Matrix.Rotation(math.pi, 4, "Z")  # same turn as the greenhouse, so yaw values match
    select_only([bush])
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    strip_attributes(bush)
    single_material(bush, "GH_Bush")

    for o in dropped + bushes:
        bpy.data.objects.remove(o, do_unlink=True)

    house = bake_join(structure, "Greenhouse_Structure", placement)
    strip_attributes(house)
    for slot in house.material_slots:
        if slot.material and slot.material.name.startswith("WhiteMetal"):
            slot.material.name = "GH_WhiteMetal"
        elif slot.material and slot.material.name.startswith("Glass"):
            slot.material.name = "GH_Glass"
    log(f"STRUCTURE tris={tri_count(house)} materials={[s.material.name for s in house.material_slots]}")
    # Frame and glazing become separate meshes: the transparent glass must not cast shadows (URP Lit still
    # renders a shadow caster for transparent surfaces), while the white frame should.
    house, glass = split_by_material(house, "GH_WhiteMetal", "Greenhouse_Frame", "Greenhouse_Glass")
    # The glazing is 16 large panes whose UVs stretch one texture over each; re-project so the rough-glass
    # streaks repeat every few metres instead.
    box_uv(glass, args.glass_tile)

    slab = bake_join(floor, "Greenhouse_Floor", placement)
    strip_attributes(slab)
    single_material(slab, "GH_WetConcrete")
    planar_uv(slab, args.floor_tile)

    plants = bake_join(pots, "Greenhouse_Plants", placement)
    strip_attributes(plants)
    single_material(plants, "GH_Plants")
    lod0 = decimated_copy(plants, "Greenhouse_Plants_LOD0", args.plants_tris)
    lod1 = decimated_copy(lod0, "Greenhouse_Plants_LOD1", args.plants_lod1_tris)
    bpy.data.objects.remove(plants, do_unlink=True)

    bush_far = card_lod(bush, "Bush_LOD1", args.textures, args.bush_lod1_tris)
    for o in (lod0, lod1):
        if hasattr(o.data, "shade_smooth"):
            o.data.shade_smooth()

    export_fbx([house, glass, slab, lod0, lod1], os.path.join(args.out_models, "Greenhouse.fbx"))
    export_fbx([bush, bush_far], os.path.join(args.out_models, "GreenhouseBush.fbx"))

    if args.preview_dir:
        os.makedirs(args.preview_dir, exist_ok=True)
        render_preview([house, slab, lod0], os.path.join(args.preview_dir, "greenhouse_lod0.png"), (14, 18, 9), (0, -16, 1.5))
        render_preview([house, slab, lod1], os.path.join(args.preview_dir, "greenhouse_lod1.png"), (14, 18, 9), (0, -16, 1.5))
        bush_far.location.x = 12
        render_preview([bush, bush_far], os.path.join(args.preview_dir, "greenhouse_bush_lods.png"), (6, -24, 5), (6, 0, 4))


# ---------------------------------------------------------------------------------------------- textures

def load_pixels(path, size=None):
    """RGBA float pixels, rows bottom-to-top (Blender order), optionally resampled to size x size."""
    img = bpy.data.images.load(path, check_existing=False)
    if size and tuple(img.size) != (size, size):
        img.scale(size, size)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def save_png(path, px, non_color=False):
    h, w = px.shape[:2]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.clip(px, 0.0, 1.0).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    log(f"TEXTURE {os.path.relpath(path, ROOT)} {w}x{h}")


def opengl_normal(px):
    """DirectX (Y-down) normal map -> OpenGL (Y-up), which Unity expects: flip the green channel."""
    out = px.copy()
    out[..., 1] = 1.0 - out[..., 1]
    out[..., 3] = 1.0
    return out


def curl_score(px, green_sign):
    """Residual curl of the gradient field a normal map implies; the right Y convention gives the smaller value."""
    n = px[..., :3] * 2.0 - 1.0
    nz = np.clip(n[..., 2], 0.2, None)
    p = -n[..., 0] / nz
    q = -(green_sign * n[..., 1]) / nz
    dp_dv = np.diff(p, axis=0)[:, :-1]
    dq_du = np.diff(q, axis=1)[:-1, :]
    return float(np.mean(np.abs(dp_dv - dq_du)) / (np.mean(np.abs(dp_dv)) + np.mean(np.abs(dq_du))))


def check_convention(name, px):
    gl, dx = curl_score(px, 1.0), curl_score(px, -1.0)
    log(f"NORMAL {name}: curl residual OpenGL={gl:.3f} DirectX={dx:.3f} -> {'DirectX' if dx < gl else 'OpenGL'}")


def pack_alpha(color, opacity):
    out = color.copy()
    out[..., 3] = opacity[..., :3].mean(axis=-1)
    return out


def metallic_smoothness(roughness, metallic):
    """URP Lit _MetallicGlossMap: R = metallic, A = smoothness (1 - roughness)."""
    out = np.zeros_like(roughness)
    out[..., 0] = metallic
    out[..., 1] = metallic
    out[..., 2] = metallic
    out[..., 3] = 1.0 - roughness[..., 0]
    return out


def seamless(tile):
    """Blend a crop with its half-offset copy, so its edges wrap (the classic offset-and-blend tile)."""
    n = tile.shape[0]
    shifted = np.roll(np.roll(tile, n // 2, axis=0), n // 2, axis=1)
    t = np.sin(np.pi * (np.arange(n) + 0.5) / n)
    mask = np.sqrt(np.outer(t, t))[..., None]
    return tile * mask + shifted * (1.0 - mask)


def renormalise(px):
    n = px[..., :3] * 2.0 - 1.0
    n /= np.linalg.norm(n, axis=-1, keepdims=True) + 1e-6
    out = px.copy()
    out[..., :3] = n * 0.5 + 0.5
    out[..., 3] = 1.0
    return out


def resample(px, size):
    img = bpy.data.images.new("resample", px.shape[1], px.shape[0], alpha=True, float_buffer=True)
    img.pixels.foreach_set(px.astype(np.float32).ravel())
    img.scale(size, size)
    out = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(out)
    bpy.data.images.remove(img)
    return out.reshape(size, size, 4)


def process_textures(args):
    src = lambda name: os.path.join(args.textures, name)
    out = lambda name: os.path.join(args.out_textures, name)
    os.makedirs(args.out_textures, exist_ok=True)

    # Bush and potted plants: alpha-clipped, so the opacity maps go into the albedo alpha.
    save_png(out("GH_Bush_Albedo.png"), pack_alpha(load_pixels(src("Bush.png")), load_pixels(src("Bush_Opacity.png"))))
    save_png(out("GH_Plants_Albedo.png"),
             pack_alpha(load_pixels(src("Plants_Diffuse.png"), 2048), load_pixels(src("Plants_Opacity.png"), 2048)))

    # Normal maps. The glass and concrete maps are labelled DirectX; the curl test (logged) shows the ground,
    # plants and bush maps follow the same convention, so all of them are converted.
    normals = {
        "Bush_Normal.png": ("GH_Bush_Normal.png", 1024),
        "Plants_Normal.png": ("GH_Plants_Normal.png", 2048),  # source is 2080 x 2048; resampled to the albedo's size
        "Rough_Glass_Seamless_Normal_DirectX.png": ("GH_Glass_Normal.png", 1024),
        "WetConcrete_Normal_DirectX.png": ("GH_Concrete_Normal.png", 1024),
    }
    for name, (target, size) in normals.items():
        px = load_pixels(src(name), size)
        check_convention(name, px)
        save_png(out(target), opengl_normal(px), non_color=True)

    # Glass: base colour at 1024 is plenty (nearly uniform); the source marks it fully metallic, which reads as
    # a dark mirror in URP, so the mask keeps only its roughness streaks (as smoothness) with metallic 0.
    save_png(out("GH_Glass_Albedo.png"), load_pixels(src("Rough_Glass_Seamless_Base_Color.png"), 1024))
    save_png(out("GH_Glass_Mask.png"), metallic_smoothness(load_pixels(src("Rough_Glass_Seamless_Roughness.png"), 1024), 0.0),
             non_color=True)

    # Wet concrete: albedo, normal and roughness -> metallic/smoothness mask.
    save_png(out("GH_Concrete_Albedo.png"), load_pixels(src("WetConcrete_Base_color.png")))
    save_png(out("GH_Concrete_Mask.png"), metallic_smoothness(load_pixels(src("WetConcrete_Roughness.png")), 0.0), non_color=True)

    # Ground: Ground_BaseColor is one 45 m yard (a dirt patch ringed by grass), not a tiling texture.
    #  - GH_Grass_*: a grass-only crop made seamless, tiled over the whole Farm ground.
    #  - GH_Yard_*: the full yard under the greenhouse, its border faded into the grass tile so it has no seam.
    color = load_pixels(src("Ground_BaseColor.png"))
    normal = load_pixels(src("Ground_Normal.png"))
    check_convention("Ground_Normal.png", normal)
    normal = opengl_normal(normal)
    h = color.shape[0]
    x, y, size = args.grass_crop
    rows = slice(h - y - size, h - y)  # crop given from the top-left; Blender rows run bottom-up
    cols = slice(x, x + size)
    grass_color = seamless(color[rows, cols])
    grass_normal = renormalise(seamless(normal[rows, cols]))
    save_png(out("GH_Grass_Albedo.png"), resample(grass_color, 512))
    save_png(out("GH_Grass_Normal.png"), resample(grass_normal, 512), non_color=True)
    log(f"GRASS tile = {size}px of the 2048px / 44.8 m yard = {size * 44.8 / h:.2f} m per repeat")

    reps = int(math.ceil(h / size))
    tiled_color = np.tile(grass_color, (reps, reps, 1))[:h, :h]
    tiled_normal = np.tile(grass_normal, (reps, reps, 1))[:h, :h]
    edge = np.minimum(np.arange(h), np.arange(h)[::-1]) / (h * 0.14)  # fade over the outer 14% (about 6 m)
    edge = np.clip(edge, 0.0, 1.0)
    edge = edge * edge * (3.0 - 2.0 * edge)
    weight = np.minimum.outer(edge, edge)[..., None]
    save_png(out("GH_Yard_Albedo.png"), color * weight + tiled_color * (1.0 - weight))
    save_png(out("GH_Yard_Normal.png"), renormalise(normal * weight + tiled_normal * (1.0 - weight)), non_color=True)


def main():
    args = parse_args()
    log(f"=== blender_greenhouse.py src={args.src}")
    if not args.skip_models:
        build_models(args)
    if not args.skip_textures:
        process_textures(args)
    log("DONE")


main()
