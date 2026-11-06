"""Prepare the lab tomato for the illustrative leaf-disease demo.

1. Geometry mask -> vertex colour R (1 = leaf, 0 = stem or fruit), from how the surface
   normals spread within --radius: leaf blades face one way, stems fan around their axis
   (one strong direction of spread) and fruit caps tilt evenly in every direction.
2. Texture mask -> RGBA PNG used by the BTP/Leaf Disease shader:
     R  leaf texels (green hues; excludes red fruit, brown soil and yellow flowers)
     G  lesion field: a texel shows a lesion when severity > G, so spots appear one by one
        and grow as severity rises
     B  chlorosis field: a texel yellows when chlorosis > B (patchy, heavier near lesions)

Usage:
  blender -b --factory-startup -P Tools/blender_tomato_disease.py -- \
      --mesh Tools/out/models/TomatoSingleStem_Lab.fbx --albedo <albedo.png> \
      --out-mesh <out.fbx> --out-mask <mask.png> [--preview-dir <dir>]
"""
import argparse
import os
import sys

import bpy
import numpy as np
from mathutils import kdtree


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:]
    p = argparse.ArgumentParser()
    p.add_argument("--mesh", required=True)
    p.add_argument("--albedo", required=True)
    p.add_argument("--out-mesh", required=True)
    p.add_argument("--out-mask", required=True)
    p.add_argument("--mask-size", type=int, default=1024)
    p.add_argument("--radius", type=float, default=0.015, help="Neighbourhood radius in metres")
    p.add_argument("--normal-spread", type=float, default=0.22,
                   help="Middle eigenvalue of the normal covariance separating leaves (below) "
                        "from stems (above)")
    p.add_argument("--fruit-roundness", type=float, default=0.0,
                   help="Smallest eigenvalue of the normal covariance above which a vertex "
                        "counts as fruit; 0 disables the test. On the Meshy tomato crumpled "
                        "leaves overlap fruit here, so red fruit is excluded by colour instead.")
    p.add_argument("--seed", type=int, default=7)
    p.add_argument("--preview-dir", default=None)
    return p.parse_args(argv)


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# ---------------------------------------------------------------- geometry mask

def geometry_leaf_mask(obj, radius, spread_threshold, fruit_roundness):
    """Per-vertex leafness from how widely surface normals spread around the vertex.

    Area-weighted covariance of the face normals within `radius`: a leaf blade faces one way
    (second eigenvalue near 0), a stem's normals fan around its axis (~0.5) and a fruit's
    point everywhere (~0.33). Face samples keep this stable on sparse, decimated geometry.
    """
    mesh = obj.data
    tri_count = len(mesh.polygons)
    centres = np.empty(tri_count * 3)
    normals = np.empty(tri_count * 3)
    areas = np.empty(tri_count)
    mesh.polygons.foreach_get("center", centres)
    mesh.polygons.foreach_get("normal", normals)
    mesh.polygons.foreach_get("area", areas)
    centres = centres.reshape(tri_count, 3)
    normals = normals.reshape(tri_count, 3)
    # Outer products n n^T, weighted by area, so each neighbourhood sum is one matrix add.
    weighted = normals[:, :, None] * normals[:, None, :] * areas[:, None, None]

    tree = kdtree.KDTree(tri_count)
    for i, c in enumerate(centres):
        tree.insert(c, i)
    tree.balance()

    count = len(mesh.vertices)
    positions = np.empty(count * 3)
    mesh.vertices.foreach_get("co", positions)
    positions = positions.reshape(count, 3)

    def normal_eigenvalues(r):
        eig = np.zeros((count, 3))
        for v in range(count):
            idx = [hit[1] for hit in tree.find_range(positions[v], r)]
            if idx:
                cov = weighted[idx].sum(axis=0) / max(areas[idx].sum(), 1e-12)
                eig[v] = np.linalg.eigvalsh(cov)  # ascending
        return eig

    eig = normal_eigenvalues(radius)
    # Stems: normals fan around the axis (middle eigenvalue high, smallest near zero).
    spread = eig[:, 1]
    # Fruit: a round cap tilts its normals evenly in both directions, so the smallest
    # eigenvalue is high too; a curled leaf or a stem bends one way only.
    roundness = eig[:, 0]

    stem = smoothstep(spread_threshold * 0.7, spread_threshold * 1.3, spread)
    if fruit_roundness > 0:
        fruit = smoothstep(fruit_roundness * 0.85, fruit_roundness * 1.15, roundness)
    else:
        fruit = np.zeros(count)
    leaf = 1.0 - np.maximum(stem, fruit)
    print(f"GEOMETRY verts={count} leaf>0.5: {np.mean(leaf > 0.5):.1%} "
          f"stem>0.5: {np.mean(stem > 0.5):.1%} fruit>0.5: {np.mean(fruit > 0.5):.1%} "
          f"spread p10/50/90: {np.percentile(spread, [10, 50, 90]).round(3).tolist()} "
          f"roundness p10/50/90: {np.percentile(roundness, [10, 50, 90]).round(3).tolist()}")
    return leaf


def write_vertex_colours(obj, per_vertex):
    mesh = obj.data
    for attr in list(mesh.color_attributes):
        mesh.color_attributes.remove(attr)
    colours = mesh.color_attributes.new(name="Col", type="BYTE_COLOR", domain="CORNER")
    loop_verts = np.zeros(len(mesh.loops), dtype=np.int64)
    mesh.loops.foreach_get("vertex_index", loop_verts)
    values = per_vertex[loop_verts]
    rgba = np.stack([values, values, values, np.ones_like(values)], axis=1).astype(np.float32)
    colours.data.foreach_set("color", rgba.ravel())
    mesh.color_attributes.active_color = colours
    mesh.update()


# ---------------------------------------------------------------- texture mask

def load_image_rgb(path, size):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[:, :, :3]
    fy, fx = h // size, w // size
    px = px[: size * fy, : size * fx].reshape(size, fy, size, fx, 3).mean(axis=(1, 3))
    return px  # rows bottom-to-top, as Blender stores them (matches UV v)


def leaf_texel_mask(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(axis=-1)
    mn = rgb.min(axis=-1)
    delta = mx - mn + 1e-6
    hue = np.where(mx == r, ((g - b) / delta) % 6.0,
                   np.where(mx == g, (b - r) / delta + 2.0, (r - g) / delta + 4.0)) * 60.0
    sat = delta / (mx + 1e-6)
    val = mx
    green_hue = smoothstep(62.0, 75.0, hue) * (1.0 - smoothstep(160.0, 175.0, hue))
    return green_hue * smoothstep(0.12, 0.22, sat) * smoothstep(0.06, 0.12, val)


def value_noise(size, period, rng):
    cells = size // period + 2
    grid = rng.random((cells, cells))
    coords = np.arange(size) / period
    i0 = np.floor(coords).astype(int)
    f = coords - i0
    f = f * f * (3 - 2 * f)
    a = grid[np.ix_(i0, i0)]
    b = grid[np.ix_(i0, i0 + 1)]
    c = grid[np.ix_(i0 + 1, i0)]
    d = grid[np.ix_(i0 + 1, i0 + 1)]
    fy, fx = f[:, None], f[None, :]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def lesion_field(leaf, rng, cell=22):
    size = leaf.shape[0]
    cells = size // cell + 1
    # One candidate spot per grid cell, jittered: [y, x] centre, radius and appearance threshold.
    centre = np.stack([np.arange(cells)[:, None].repeat(cells, 1) * cell + rng.random((cells, cells)) * cell,
                       np.arange(cells)[None, :].repeat(cells, 0) * cell + rng.random((cells, cells)) * cell], axis=-1)
    radius = rng.uniform(0.25, 0.65, (cells, cells)) * cell
    threshold = rng.uniform(0.0, 0.9, (cells, cells))
    cy = np.clip(centre[..., 0].astype(int), 0, size - 1)
    cx = np.clip(centre[..., 1].astype(int), 0, size - 1)
    active = leaf[cy, cx] > 0.5

    ys, xs = np.mgrid[0:size, 0:size]
    gy, gx = ys // cell, xs // cell
    field = np.ones((size, size))
    # A spot can grow to ~1.9 cells at full severity, so search two cells in each direction.
    for oy in (-2, -1, 0, 1, 2):
        for ox in (-2, -1, 0, 1, 2):
            ny = np.clip(gy + oy, 0, cells - 1)
            nx = np.clip(gx + ox, 0, cells - 1)
            d = np.hypot(ys - centre[ny, nx, 0], xs - centre[ny, nx, 1])
            v = threshold[ny, nx] + 0.35 * d / radius[ny, nx]
            v = np.where(active[ny, nx], v, 1.0)
            field = np.minimum(field, v)
    return np.clip(field, 0.0, 1.0)


def write_png(path, rgba):
    size = rgba.shape[0]
    img = bpy.data.images.new("mask", size, size, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


# ---------------------------------------------------------------- main

def main():
    args = parse_args()
    rng = np.random.default_rng(args.seed)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=args.mesh)
    else:
        bpy.ops.import_scene.fbx(filepath=args.mesh)
    obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")

    # Work in world metres whatever unit scale the FBX import applied.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    print(f"MESH {obj.name} verts={len(obj.data.vertices)} size={tuple(round(d, 3) for d in obj.dimensions)}")

    leaf_geo = geometry_leaf_mask(obj, args.radius, args.normal_spread, args.fruit_roundness)
    write_vertex_colours(obj, leaf_geo)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=args.out_mesh, use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", colors_type="LINEAR",
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print(f"EXPORT {args.out_mesh}")

    rgb = load_image_rgb(args.albedo, args.mask_size)
    leaf = leaf_texel_mask(rgb)
    lesions = lesion_field(leaf, rng)
    noise = 0.6 * value_noise(args.mask_size, 96, rng) + 0.4 * value_noise(args.mask_size, 32, rng)
    noise = (noise - noise.min()) / (noise.max() - noise.min())
    chlorosis = np.clip(0.65 * noise + 0.35 * lesions, 0.0, 1.0)
    rgba = np.stack([leaf, lesions, chlorosis, np.ones_like(leaf)], axis=-1)
    write_png(args.out_mask, rgba)
    print(f"MASK {args.out_mask} leaf texels: {np.mean(leaf > 0.5):.1%}")

    if args.preview_dir:
        os.makedirs(args.preview_dir, exist_ok=True)
        render_vertex_colour_preview(obj, os.path.join(args.preview_dir, "TomatoLab_leaf_geometry.png"))


def render_vertex_colour_preview(obj, path):
    from mathutils import Vector
    scene = bpy.context.scene
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    scene.collection.objects.link(cam)
    cam.location = (1.4, -1.4, 0.9)
    cam.rotation_euler = (Vector((0, 0, 0.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "FLAT"
    scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x = scene.render.resolution_y = 768
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print(f"PREVIEW {path}")


main()
