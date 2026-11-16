"""Exports the whole-plant disease models of the ten field, tropical and shrub crops for the Farm rows.

Sources: Assets/Plant_Asstes/<crop folder>/<Crop>_<Disease>.glb (34 plants: blueberry 1, chilli 3, cotton 3,
mango 4, orange 1, rice 5, soybean 9, squash 1, sugarcane 4, wheat 3). They are film-resolution Blender plants:
50k to 840k triangles as stored and far more once their linked leaves are counted (the mango tree is about
6.9 million), with 7 to 17 materials each. A Farm row repeats one plant many times on a Quest, so every plant
is reduced to what the existing Farm plants use (see Tools/blender_bell_pepper_plant.py):

  * two meshes, <Stem>_<disease>_LOD0 and _LOD1, at the crop's triangle budgets. Every unique source mesh is
    decimated once (edge collapse, UVs kept) and then placed for each of its linked instances, so a leaf that is
    used 700 times is simplified once. Leaves are not collapsed at all: any simplified outline reads as a
    damaged or half-missing leaf. Each leaf is a card that follows the leaf's own surface, drawn with a cut-out
    sprite of that leaf: the leaf redrawn flat from the supplied texture (which is a patchwork of UV charts, so
    it cannot be used as a sprite itself), its alpha the leaf's true outline, lesion holes included. Leaves that
    share their UVs share one sprite; Budgets are shared by a single ratio, with a floor per part so no leaf or
    twig vanishes; LOD1 also leaves out the parts that are tiny at its distance (flowers, pedicels), and more of them when the
    floors alone exceed the budget of a plant;
  * two materials: <Stem>Leaf (the leaf sprites, a 1024 or 2048 px cut-out sheet, the only part that changes
    over the Farm's six-week timeline) and <Stem>Parts (everything else on one atlas: each textured part in a 512 px cell,
    solid colours as swatches). A part whose UVs tile is drawn in its texture's mean colour;
  * roots, soil plugs and hair-like detail (trichomes, pubescence, filaments, anthers, pollen) are left out: the
    Farm beds supply the soil, and hairs cost tens of thousands of triangles for nothing visible at 1 m;
  * the plant stands on the origin (centre of its stem base, lowest kept point at 0), scaled only where the
    source is bigger than a Farm row allows (the mango: 6.9 m tall and 8.7 m wide, to 3.8 m).
Some source materials are procedural in the .blend and reach the GLB as plain white; those take a colour from
their own name (FALLBACK_COLOURS), which the export report lists so they can be checked.

Wind: the sources carry a 10 s wind loop (240 frames at 24 fps) on every leaf and, slighter, on stems, fruit and
flowers. Every moving object becomes one wind part: its vertices carry the part index in the second UV set
(u = index + 1, 0 = still; v = 1 on one tagged vertex) and <Stem>_<disease>_Wind.bytes holds, per part, its pivot
and KEYS rigid motions relative to the first frame (rotation and the travel of the pivot, so a leaf also follows
its swaying stem). LeafWindBaker turns that into the texture LeafWind.hlsl replays. The motion is exact for
objects animated as a whole; the rice blades, which an armature bends, get the best rigid fit.

Run:  blender -b --factory-startup -P Tools/blender_farm_crops.py -- [--only rice,wheat] [--out <dir>] [--no-preview]
Output: Assets/_Project/Models/FarmCrops/<Stem>/ with <Stem>_<disease>.fbx, <Stem>_<disease>_Leaf.png,
<Stem>_Parts.png (per disease when a variant's parts differ) and <Stem>_export.json, which
Assets/_Project/Scripts/Editor/FarmCropBuilder.cs reads. Previews go to Tools/out/previews/farm_crops.
The sources are only read.
"""
import json
import math
import os
import struct
import sys

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "Assets", "Plant_Asstes")
OUT_DEFAULT = os.path.join(ROOT, "Assets", "_Project", "Models", "FarmCrops")
PREVIEWS = os.path.join(ROOT, "Tools", "out", "previews", "farm_crops")

LEAF_SIZE = 1024
KEYS = 60           # wind keys over the loop (the sources: 240 frames at 24 fps, so one key every 4 frames)
CELL = 512          # atlas cell
PAD = 8             # edge padding inside a cell, against mip bleeding
SWATCH_GRID = 8     # solid colours per row of the swatch cell

# plantId, file stem, source folder, target height (None = as modelled), (LOD0, LOD1) triangles, diseaseId -> file.
CROPS = [
    dict(id="blueberry", stem="Blueberry", folder="Blue_Berry_Plant", height=None, tris=(14000, 4500),
         variants={"rust": "Blueberry_Plant.glb"}),
    dict(id="chilli", stem="Chilli", folder="Chilli_Plant", height=None, tris=(12000, 4000),
         variants={"anthracnose": "Chilli_Anthracnose.glb", "cercospora_leaf_spot": "Chilli_Cercospora.glb",
                   "leaf_curl": "Chilli_Leaf_Curl.glb"}),
    dict(id="cotton", stem="Cotton", folder="Cotton_Plant", height=None, tris=(14000, 4500),
         variants={"bacterial_blight": "Cotton_Bacterial_Blight.glb", "fusarium_wilt": "Cotton_Fusarium_Wilt.glb",
                   "leaf_curl_virus": "Cotton_Leaf_Curl_Virus.glb"}),
    # stalks: the fruit of the trees hangs in the air in the sources (the stalks are hair-thin or missing), so
    # each fruit gets a generated stalk up to the nearest wood above it. fruit = material-name keys of the fruit.
    dict(id="mango", stem="Mango", folder="Mango_Tree", height=3.8, tris=(42000, 14000), keep=("Mango roots",),
         stalks=dict(fruit=("mango | ",), colour=(0.40, 0.36, 0.17), thickness=(0.035, 0.005, 0.009)),
         variants={"anthracnose": "Mango_Anthracnose.glb", "bacterial_canker": "Mango_Bacterial_Canker.glb",
                   "die_back": "Mango_Die_Back.glb", "gall_midge": "Mango_Gall_Midge.glb"}),
    # The orange tree's own leaf sprays are a pre-reduced "runtime" mesh whose texture mapping is broken (every
    # triangle shows an unrelated piece of the leaf texture, in the source GLB itself), so its leaf sprite is
    # drawn from the supplied citrus greening leaf specimen instead.
    dict(id="orange", stem="Orange", folder="Orange_tree", height=None, max_height=3.6, tris=(54000, 16000), fruit="hull",
         stalks=dict(fruit=("peel",), colour=(0.30, 0.40, 0.15), thickness=(0.016, 0.003, 0.0045)),
         leaf_model=("Assets/Leaf_Assets/Orange/Orange Huanglongbing (Citrus Greening)/Meshy_AI_Citrus_Greening_Leaf_20261111_image-to-3d-texture_fbx/Meshy_AI_Citrus_Greening_Leaf_20261111_image-to-3d-texture.fbx",
                     "Assets/Leaf_Assets/Orange/Orange Huanglongbing (Citrus Greening)/Meshy_AI_Citrus_Greening_Leaf_20261111_image-to-3d-texture_fbx/Meshy_AI_Citrus_Greening_Leaf_20261111_image-to-3d-texture.png"),
         variants={"huanglongbing": "Orange_Citrus_Greening.glb"}),
    dict(id="rice", stem="Rice", folder="Rice_plant", height=None, tris=(12000, 3500),
         variants={"bacterial_leaf_blight": "Rice_Bacterial_Leaf_Blight.glb", "brown_spot": "Rice_Brown_Spot.glb",
                   "leaf_blast": "Rice_Leaf_Blast.glb", "sheath_blight": "Rice_Sheath_Blight.glb", "tungro": "Rice_Tungro.glb"}),
    dict(id="soybean", stem="Soybean", folder="Soyabean_Plant", height=None, tris=(10000, 3200),
         variants={"bacterial_blight": "Soybean_Bacterial_Blight.glb", "cercospora_leaf_blight": "Soybean_Cercospora_Leaf_Blight.glb",
                   "downy_mildew": "Soybean_Downy_Mildew.glb", "frogeye_leaf_spot": "Soybean_Frogeye_Leaf_Spot.glb",
                   "mosaic_virus": "Soybean_Mosaic_Virus.glb", "septoria_brown_spot": "Soybean_Septoria_Brown_Spot.glb",
                   "soybean_rust": "Soybean_Rust.glb", "sudden_death_syndrome": "Soybean_Sudden_Death_Syndrome.glb",
                   "target_spot": "Soybean_Target_Spot.glb"}),
    dict(id="squash", stem="Squash", folder="Squash_plant", height=None, tris=(16000, 5000), origin="footprint",
         variants={"powdery_mildew": "Squash_Powdery_Mildew.glb"}),
    dict(id="sugarcane", stem="Sugarcane", folder="Sugarcane_Plant", height=None, max_height=3.2, tris=(14000, 4500),
         variants={"mosaic": "Sugarcane_Mosaic.glb", "red_rot": "Sugarcane_Red_Rot.glb", "rust": "Sugarcane_Rust.glb",
                   "yellow_leaf": "Sugarcane_Yellow_Leaf.glb"}),
    dict(id="wheat", stem="Wheat", folder="Wheat_plant", height=None, tris=(14000, 4000),
         variants={"blast": "Wheat_Blast.glb", "leaf_blight": "Wheat_Leaf_Blight.glb", "rust": "Wheat_Rust.glb"}),
]

# Material-name keywords (lower case). Order matters only inside each list.
DROP = ("root", "soil", "loam", "trichome", "pubescence", "filament", "anther", "pollen")
LEAF = ("leaf", "leaflet")
# sRGB colours for materials that arrive white and untextured (procedural in the .blend). First match wins.
FALLBACK_COLOURS = [
    ("ivory", (0.93, 0.92, 0.84)), ("white", (0.93, 0.93, 0.90)), ("corolla", (0.93, 0.74, 0.16)), ("bud", (0.62, 0.68, 0.22)), ("summer squash", (0.89, 0.72, 0.20)),
    ("golden", (0.86, 0.68, 0.22)), ("yellow", (0.88, 0.76, 0.24)), ("mature basal stem", (0.42, 0.42, 0.22)),
    ("olive", (0.40, 0.46, 0.20)), ("petiole", (0.42, 0.52, 0.22)), ("pedicel", (0.40, 0.50, 0.22)),
    ("green", (0.36, 0.50, 0.20)), ("stem", (0.40, 0.48, 0.22)), ("ochre", (0.60, 0.48, 0.26)),
    ("straw", (0.72, 0.62, 0.38)), ("brown", (0.36, 0.26, 0.17)),
]


STALK = "Fruit stalk | generated"
WOOD = ("twig", "bark", "wood", "branch", "flush")


class GeneratedSource:
    """A solid colour in the parts atlas for geometry this script makes itself (the fruit stalks)."""

    def __init__(self, name, colour):
        self.name, self.colour = name, colour
        self.image, self.factor, self.fallback = None, (1.0, 1.0, 1.0), None
        self.leaf = self.drop = self.tiles = False


def fruit_stalks(fruits, wood, level, thickness):
    """A stalk for every fruit: a tube that leaves the top of the fruit upwards and bends over to the nearest
    wood above it (a twig or branch within a metre and a half; without one it ends 25 cm up), with a small
    collar where it meets the fruit and a slightly thicker end at the wood, as a pedicel has. Six segments of
    five sides up close, three of three further away. fruits: (top point, fruit size, wind part number or 0).
    Returns triangle-soup corners, their normals and their wind part numbers."""
    segments, sides = (6, 5) if level == 0 else (3, 3)
    corners, normals, numbers = [], [], []
    for top, size, number in fruits:
        end = None
        if len(wood):
            offset = wood - top
            reachable = (offset[:, 2] > 0.005) & (np.linalg.norm(offset[:, :2], axis=1) < 1.0)
            if reachable.any():
                distance = np.where(reachable, np.linalg.norm(offset, axis=1), 1e9)
                nearest = int(distance.argmin())
                if distance[nearest] < 0.005:
                    continue                    # the fruit already sits on its twig
                if distance[nearest] < 1.5:
                    end = wood[nearest]
        if end is None:
            end = top + np.array([0.0, 0.0, 0.25])
        start = top - np.array([0.0, 0.0, 0.012])      # a little inside the fruit
        # Up out of the fruit first, then over to the wood: the fruit hangs under its stalk.
        bend = np.array([start[0], start[1], start[2] + 0.65 * (end[2] - start[2])])
        radius = float(np.clip(thickness[0] * size, thickness[1], thickness[2]))
        rings = []
        for i in range(segments + 1):
            t = i / segments
            point = (1 - t) ** 2 * start + 2 * t * (1 - t) * bend + t * t * end
            tangent = 2 * (1 - t) * (bend - start) + 2 * t * (end - bend)
            tangent = tangent / max(np.linalg.norm(tangent), 1e-9)
            side = np.cross(tangent, [1.0, 0.0, 0.0])
            if np.linalg.norm(side) < 0.2:
                side = np.cross(tangent, [0.0, 1.0, 0.0])
            side = side / np.linalg.norm(side)
            other = np.cross(tangent, side)
            width = radius * (2.4 if i == 0 else 1.0 + 0.5 * t)       # collar at the fruit, thicker at the wood
            ring = []
            for k in range(sides):
                angle = 2 * math.pi * k / sides
                outward = math.cos(angle) * side + math.sin(angle) * other
                ring.append((point + outward * width, outward))
            rings.append(ring)
        for i in range(segments):
            for k in range(sides):
                a, b = rings[i][k], rings[i][(k + 1) % sides]
                c, d = rings[i + 1][k], rings[i + 1][(k + 1) % sides]
                for triangle in ((a, b, d), (a, d, c)):
                    for position, normal in triangle:
                        corners.append(position)
                        normals.append(normal)
                        numbers.append(number)
    if not corners:
        return np.zeros((0, 3)), np.zeros((0, 3)), np.zeros(0)
    return np.array(corners), np.array(normals), np.array(numbers, dtype=np.float32)


def log(*parts):
    print("[farm_crops]", *parts, flush=True)


def srgb(linear):
    return tuple(12.92 * c if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055 for c in linear)


# ----------------------------------------------------------------------------- source materials

class Source:
    """What one source material contributes: the leaf texture, an atlas cell, a solid colour, or nothing."""

    def __init__(self, material, keep):
        self.name = material.name
        low = self.name.lower()
        self.image = None
        self.factor = (1.0, 1.0, 1.0)
        tree = material.node_tree
        if tree:
            for node in tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    socket = node.inputs["Base Color"]
                    self.factor = tuple(socket.default_value[:3])
                    self.image, factor = upstream_image(socket)
                    if self.image is not None:
                        self.factor = factor
                    break
        kept = any(k.lower() in low for k in keep)
        self.drop = not kept and any(k in low for k in DROP)
        self.leaf = not self.drop and any(k in low for k in LEAF) and self.image is not None
        self.tiles = False      # set while reading the instances
        self.fallback = None
        # A material that is procedural in the .blend reaches the GLB without a colour: white, which the importer
        # may show as its default 0.8 grey.
        unspecified = min(self.factor) > 0.75 and max(self.factor) - min(self.factor) < 0.01
        if self.image is None and unspecified:
            for key, colour in FALLBACK_COLOURS:
                if key in low:
                    self.fallback = key
                    self.colour = colour
                    break
            else:
                self.fallback = "none"
                self.colour = (0.45, 0.50, 0.25)
        elif self.image is None:
            self.colour = srgb(self.factor)
        else:
            self.colour = None  # filled from the image mean if the part turns out to tile


def upstream_image(socket):
    """The image texture feeding a socket (through the glTF importer's mix nodes) and the colour factor on the way."""
    factor = [1.0, 1.0, 1.0]
    seen = set()
    stack = [socket]
    image = None
    while stack:
        current = stack.pop()
        for link in current.links:
            node = link.from_node
            if node in seen:
                continue
            seen.add(node)
            if node.type == "TEX_IMAGE" and node.image is not None:
                image = image or node.image
                continue
            for inp in node.inputs:
                if inp.is_linked:
                    stack.append(inp)
                elif inp.type == "RGBA" and node.type in ("MIX", "MIX_RGB") and inp.enabled:
                    value = tuple(inp.default_value[:3])
                    if min(value) < 0.999:
                        factor = [f * v for f, v in zip(factor, value)]
    return image, tuple(factor)


def image_pixels(image, size):
    """sRGB pixels of an image scaled to size x size, as (size, size, 3) floats, bottom row first."""
    copy = image.copy()
    try:
        if copy.size[0] == 0:
            raise RuntimeError(f"{image.name} has no pixels")
        copy.scale(size, size)
        data = np.empty(size * size * 4, dtype=np.float32)
        copy.pixels.foreach_get(data)
        return data.reshape(size, size, 4)[:, :, :3].copy()
    finally:
        bpy.data.images.remove(copy)


def save_png(path, rgb, alpha=None):
    height, width = rgb.shape[:2]
    image = bpy.data.images.new("farm_crops_out", width, height, alpha=alpha is not None)
    try:
        rgba = np.ones((height, width, 4), dtype=np.float32)
        rgba[:, :, :3] = np.clip(rgb, 0.0, 1.0)
        if alpha is not None:
            image.alpha_mode = "STRAIGHT"
            rgba[:, :, 3] = np.clip(alpha, 0.0, 1.0)
        image.pixels.foreach_set(rgba.ravel())
        image.filepath_raw = path
        image.file_format = "PNG"
        image.save()
    finally:
        bpy.data.images.remove(image)


# ----------------------------------------------------------------------------- geometry

class Part:
    """One unique source mesh and the objects that use it."""

    def __init__(self, mesh):
        self.mesh = mesh
        self.instances = []     # world matrices (4x4 numpy)
        self.objects = []       # the name of the object of each instance
        self.tris = 0
        self.leaf = False
        self.small = False
        self.cache = {}


def work_scene():
    """An empty scene made the active one, so each decimation evaluates one object, not the whole plant."""
    scene = bpy.data.scenes.get("farm_crops_work")
    if scene is None:
        scene = bpy.data.scenes.new("farm_crops_work")
    if bpy.context.window.scene != scene:
        bpy.context.window.scene = scene
    return scene


def decimated(mesh, ratio):
    """(vertices, vertex normals, triangle vertex indices, triangle corner UVs, triangle material indices) of a
    copy of the mesh collapsed to the ratio."""
    scene = work_scene()
    obj = bpy.data.objects.new("farm_crops_tmp", mesh)
    scene.collection.objects.link(obj)
    try:
        if ratio < 0.999:
            modifier = obj.modifiers.new("decimate", "DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = max(ratio, 1e-5)
            modifier.use_collapse_triangulate = True
        depsgraph = bpy.context.evaluated_depsgraph_get()
        depsgraph.update()
        result = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), depsgraph=depsgraph)
    finally:
        bpy.data.objects.remove(obj)
    try:
        result.calc_loop_triangles()
        count = len(result.loop_triangles)
        vertices = np.empty(len(result.vertices) * 3, dtype=np.float32)
        result.vertices.foreach_get("co", vertices)
        normals = np.empty(len(result.vertices) * 3, dtype=np.float32)
        result.vertex_normals.foreach_get("vector", normals)
        tri_vertices = np.empty(count * 3, dtype=np.int32)
        result.loop_triangles.foreach_get("vertices", tri_vertices)
        tri_loops = np.empty(count * 3, dtype=np.int32)
        result.loop_triangles.foreach_get("loops", tri_loops)
        tri_materials = np.empty(count, dtype=np.int32)
        result.loop_triangles.foreach_get("material_index", tri_materials)
        if result.uv_layers:
            uvs = np.empty(len(result.loops) * 2, dtype=np.float32)
            result.uv_layers[0].data.foreach_get("uv", uvs)
            tri_uvs = uvs.reshape(-1, 2)[tri_loops].reshape(count, 3, 2)
        else:
            tri_uvs = np.zeros((count, 3, 2), dtype=np.float32)
        return vertices.reshape(-1, 3), normals.reshape(-1, 3), tri_vertices.reshape(count, 3), tri_uvs, tri_materials
    finally:
        bpy.data.meshes.remove(result)


def hull_mesh(mesh, points):
    """The convex hull of evenly spread vertices of a mesh, as a new mesh of about 2 x points triangles, each
    corner keeping the UV its vertex had. For round fruit whose scan (a peel with pores, a stalk, sometimes an
    inner shell) collapses into a crumpled or torn lump: the hull of well-spread surface points is the clean
    rounded body, built directly at the wanted size (collapsing a full hull afterwards tears it)."""
    import bmesh
    work = bmesh.new()
    work.from_mesh(mesh)
    layer = work.loops.layers.uv.active
    work.verts.ensure_lookup_table()
    coords = np.array([v.co[:] for v in work.verts], dtype=np.float64)
    if len(coords) < 8:
        work.free()
        return mesh
    # Farthest-point sampling: each new point is the one farthest from those already chosen.
    chosen = [int(np.linalg.norm(coords - coords.mean(0), axis=1).argmax())]
    distance = np.linalg.norm(coords - coords[chosen[0]], axis=1)
    while len(chosen) < min(points, len(coords)):
        index = int(distance.argmax())
        if distance[index] <= 1e-9:
            break
        chosen.append(index)
        distance = np.minimum(distance, np.linalg.norm(coords - coords[index], axis=1))
    uv_of, slot_of = {}, {}
    for index in chosen:
        vertex = work.verts[index]
        if layer is not None and vertex.link_loops:
            uv_of[index] = vertex.link_loops[0][layer].uv.copy()
            slot_of[index] = vertex.link_loops[0].face.material_index
    work.free()

    hull = bmesh.new()
    added = [hull.verts.new(coords[index]) for index in chosen]
    bmesh.ops.convex_hull(hull, input=added, use_existing_faces=False)
    loose = [v for v in hull.verts if not v.link_faces]
    source_of = {vertex: index for vertex, index in zip(added, chosen)}
    if loose:
        bmesh.ops.delete(hull, geom=loose, context="VERTS")
    if len(hull.faces) < 4:
        hull.free()
        return mesh
    bmesh.ops.triangulate(hull, faces=hull.faces[:])
    bmesh.ops.recalc_face_normals(hull, faces=hull.faces[:])
    hull_layer = hull.loops.layers.uv.new("UVMap")
    for face in hull.faces:
        slots = [slot_of.get(source_of.get(c.vert), 0) for c in face.loops]
        face.material_index = max(set(slots), key=slots.count)
        corner_uvs = [uv_of.get(source_of.get(c.vert)) for c in face.loops]
        if any(uv is None for uv in corner_uvs):
            continue
        # A hull triangle whose corners lie on different UV charts would stretch across the gap between them
        # (black cracks on the peel): it takes the texture of one corner instead. A peel is uniform enough.
        apart = max((corner_uvs[i] - corner_uvs[j]).length for i in range(3) for j in range(i))
        for corner, uv in zip(face.loops, corner_uvs):
            corner[hull_layer].uv = corner_uvs[0] if apart > 0.12 else uv
    result = bpy.data.meshes.new(mesh.name + " hull")
    hull.to_mesh(result)
    hull.free()
    for material in mesh.materials:
        result.materials.append(material)
    return result


def face_islands(work):
    """Connected groups of faces (joined across edges)."""
    seen = set()
    islands = []
    for face in work.faces:
        if face in seen:
            continue
        island = [face]
        seen.add(face)
        index = 0
        while index < len(island):
            for edge in island[index].edges:
                for other in edge.link_faces:
                    if other not in seen:
                        seen.add(other)
                        island.append(other)
            index += 1
        islands.append(island)
    return islands


def smooth_regions(work, limit=0.64):
    """Groups of faces joined across edges that are not creased (normals within about 50 degrees): on a thin
    leaf the upper surface, the lower surface and the strip of the rim come out as separate regions."""
    seen = set()
    regions = []
    for face in work.faces:
        if face in seen:
            continue
        region = [face]
        seen.add(face)
        index = 0
        while index < len(region):
            current = region[index]
            for edge in current.edges:
                for other in edge.link_faces:
                    if other not in seen and other.normal.dot(current.normal) > limit:
                        seen.add(other)
                        region.append(other)
            index += 1
        regions.append(region)
    return regions


def leaf_sheet(part):
    """The supplied leaves are closed solids a few millimetres thick, often pierced by lesion holes, and edge
    collapse cannot go below a hundred or more triangles on such a shell (it never changes the number of
    holes). A leaf is drawn double-sided anyway, so only one of its two surfaces is kept.

    The surfaces are found as smooth regions (the rim is a crease), and each region is paired with the one a ray
    shot through the leaf from it lands on: of each pair, the region that faces up on the plant is kept. That
    works for a spray of leaves at every angle and for cupped or folded leaves. A leaf whose rim is rounded has
    no crease to stop at, so its one region is split by its flattest axis instead. Rim strips and other slivers
    are dropped and the lesion holes closed. Returns the mesh to decimate: the own mesh of the part when it is
    not a thin closed shell (the rays then hit nothing)."""
    import bmesh
    from mathutils.bvhtree import BVHTree
    mesh = part.mesh
    if len(mesh.polygons) < 24:
        return mesh
    work = bmesh.new()
    work.from_mesh(mesh)
    work.faces.ensure_lookup_table()
    work.faces.index_update()
    work.normal_update()
    points = np.array([v.co[:] for v in work.verts], dtype=np.float64)
    diagonal = float(np.linalg.norm(points.max(0) - points.min(0)))
    tree = BVHTree.FromBMesh(work)
    faces = list(work.faces)

    def through(face, sign):
        direction = face.normal * sign
        _, _, index, distance = tree.ray_cast(face.calc_center_median() + direction * (diagonal * 1e-4), direction, diagonal * 0.2)
        return index, distance

    # Through the leaf from sampled faces: inwards (against the normal), or along it when the shell is inside
    # out, in which case it is turned the right way first.
    samples = faces[::max(1, len(faces) // 240)]
    hits = {-1.0: [], 1.0: []}
    for face in samples:
        for sign in hits:
            _, distance = through(face, sign)
            if distance is not None:
                hits[sign].append(distance)
    inside_out = len(hits[1.0]) > len(hits[-1.0])
    distances = hits[1.0] if inside_out else hits[-1.0]
    if len(distances) < 0.5 * len(samples) or float(np.median(distances)) > 0.15 * diagonal:
        # Not a closed thin shell: a single sheet already. Its lesion holes still stop the collapse.
        return cleaned_sheet(work, mesh, faces)
    inward = 1.0 if inside_out else -1.0

    rotation = part.instances[0][:3, :3]
    regions = smooth_regions(work)
    region_of = {}
    for number, region in enumerate(regions):
        for face in region:
            region_of[face.index] = number
    areas = [sum(f.calc_area() for f in region) for region in regions]
    largest = max(areas)
    under = []
    decided = {}
    for number in sorted(range(len(regions)), key=lambda n: -areas[n]):
        if number in decided:
            continue
        region = regions[number]
        if areas[number] < 0.04 * largest:
            decided[number] = False           # a rim strip or a hole wall
            continue
        # The region across the leaf, by vote of a few rays.
        votes = {}
        for face in region[::max(1, len(region) // 24)]:
            index, _ = through(face, inward)
            if index is not None:
                other = region_of[index]
                votes[other] = votes.get(other, 0) + 1
        across = max(votes, key=votes.get) if votes else None
        if across == number:
            # No crease at the rim: one region wraps the whole leaf. Split it by its flattest axis.
            corners = np.array([v.co[:] for v in {v for f in region for v in f.verts}], dtype=np.float64)
            centred = corners - corners.mean(0)
            _, vectors = np.linalg.eigh(centred.T @ centred / max(len(corners), 1))
            axis = vectors[:, 0]
            if (rotation @ axis)[2] * inward > 0.0:
                axis = -axis
            axis = Vector(axis)
            under.extend(f for f in region if f.normal.dot(axis) <= 0.0)
            decided[number] = True
            continue

        def facing_up(n):
            total = Vector((0.0, 0.0, 0.0))
            for f in regions[n]:
                total += f.normal * f.calc_area()
            return float((rotation @ np.array(total[:]))[2]) * -inward

        if across is None or across in decided:
            decided[number] = True
        elif facing_up(number) >= facing_up(across):
            decided[number], decided[across] = True, False
        else:
            decided[number], decided[across] = False, True
    for number, kept in decided.items():
        if not kept:
            under.extend(regions[number])
    under = list(set(under))
    if len(under) < 0.2 * len(faces) or len(under) > 0.8 * len(faces):
        work.free()
        return mesh
    bmesh.ops.delete(work, geom=under, context="FACES")
    if inside_out:
        bmesh.ops.reverse_faces(work, faces=work.faces[:])
    return cleaned_sheet(work, mesh, faces)


def cleaned_sheet(work, mesh, faces):
    """Drops the slivers a cut leaves at the rim and the hole walls (loose pieces that cannot collapse and would
    make the blades absorb the whole reduction), closes the lesion holes, and returns the result as a new mesh;
    the original mesh when nothing changed."""
    import bmesh
    before = len(work.faces), len(work.edges)
    islands = face_islands(work)
    limit = max(8, max(len(i) for i in islands) // 25)
    slivers = [f for island in islands if len(island) < limit for f in island]
    if slivers and len(slivers) < len(work.faces):
        bmesh.ops.delete(work, geom=slivers, context="FACES")
    if (len(work.faces), len(work.edges)) == before and before[0] == len(faces):
        work.free()
        return mesh
    loose = [v for v in work.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(work, geom=loose, context="VERTS")
    sheet = bpy.data.meshes.new(mesh.name + " sheet")
    work.to_mesh(sheet)
    work.free()
    for material in mesh.materials:
        sheet.materials.append(material)
    return sheet


def fill_holes(work):
    """Closes every boundary loop of each leaf of a sheet except that leaf's outline (its longest loop). Shot
    holes are modelled as real holes, and each would keep its own ring of triangles through the collapse; the
    lesion is still in the texture. The patches take the UVs of the hole's rim."""
    import bmesh
    inner = []
    for island in face_islands(work):
        remaining = {e for f in island for e in f.edges if e.is_boundary}
        loops = []
        while remaining:
            stack = [remaining.pop()]
            loop = []
            while stack:
                edge = stack.pop()
                loop.append(edge)
                for vertex in edge.verts:
                    for other in vertex.link_edges:
                        if other in remaining:
                            remaining.remove(other)
                            stack.append(other)
            loops.append(loop)
        if len(loops) < 2:
            continue
        loops.sort(key=lambda loop: sum(e.calc_length() for e in loop))
        inner.extend(e for loop in loops[:-1] for e in loop)
    if not inner:
        return
    before = set(work.faces)
    bmesh.ops.holes_fill(work, edges=inner, sides=0)
    layer = work.loops.layers.uv.active
    if layer is None:
        return
    for face in work.faces:
        if face in before:
            continue
        for corner in face.loops:
            for other in corner.vert.link_loops:
                if other.face in before:
                    corner[layer].uv = other[layer].uv
                    break


def leaf_islands(mesh, leaf_slots):
    """The separate leaves of a leaf mesh (a tree's "leaf spray" holds a dozen): for each, its triangles as
    positions P (T, 3, 3), texture coordinates UV (T, 3, 2) and face normals N, its UV rectangle, centre and
    mean normal. Largest first; specks under 3 % of the largest are left out."""
    vertices, _, triangles, tri_uvs, tri_materials = decimated(mesh, 1.0)
    keep = np.isin(tri_materials, leaf_slots)
    triangles, tri_uvs = triangles[keep], tri_uvs[keep]
    if len(triangles) == 0:
        return []
    labels = np.arange(len(vertices))
    while True:                                   # each vertex takes the lowest label of its triangles
        lowest = labels[triangles].min(axis=1)
        updated = labels.copy()
        for corner in range(3):
            np.minimum.at(updated, triangles[:, corner], lowest)
        if np.array_equal(updated, labels):
            break
        labels = updated
    corners = vertices[triangles].astype(np.float64)
    cross = np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0])
    doubled = np.linalg.norm(cross, axis=1)
    face_normals = cross / np.maximum(doubled, 1e-20)[:, None]
    areas = 0.5 * doubled
    tri_label = labels[triangles[:, 0]]
    islands = []
    for label in np.unique(tri_label):
        mask = tri_label == label
        if mask.sum() < 2:
            continue
        uv = tri_uvs[mask].astype(np.float64)
        normal = (face_normals[mask] * areas[mask][:, None]).sum(0)
        length = np.linalg.norm(normal)
        islands.append(dict(area=float(areas[mask].sum()), P=corners[mask], UV=uv, N=face_normals[mask],
                            normal=normal / length if length > 1e-12 else np.array([0.0, 0.0, 1.0]),
                            centre=corners[mask].reshape(-1, 3).mean(0), material=int(leaf_slots[0]),
                            low=uv.reshape(-1, 2).min(0), high=uv.reshape(-1, 2).max(0)))
    if not islands:
        return []
    largest = max(i["area"] for i in islands)
    return sorted((i for i in islands if i["area"] >= 0.03 * largest), key=lambda i: -i["area"])


def flatten(island):
    """Lays a leaf flat: its triangles in the plane of its two longest axes, as S (T, 3, 2) in 0..1 with the
    length along u. The supplied leaf textures are patchworks (the leaf surface is cut into dozens of scattered
    UV charts), so a card cannot be mapped onto them; the leaf is redrawn flat into a sprite instead."""
    points = island["P"].reshape(-1, 3)
    centre = points.mean(0)
    _, vectors = np.linalg.eigh((points - centre).T @ (points - centre) / len(points))
    # Along the length, and across it along whichever other axis lies in the surface (the one the normals lean
    # along least). For a broad leaf that is its width; for an arching grass blade too, where the second
    # largest extent is the height of the arch and would show the blade edge-on.
    corners = island["P"]
    weights = np.linalg.norm(np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]), axis=1)
    lean = [float((weights * (island["N"] @ vectors[:, axis]) ** 2).sum()) for axis in (1, 0)]
    across = 1 if lean[0] <= lean[1] else 0
    flat = (points - centre) @ vectors[:, [2, across]]
    low, high = flat.min(0), flat.max(0)
    size = np.maximum(high - low, 1e-9)
    island["S"] = ((flat - low) / size).reshape(-1, 3, 2)
    island["flat_aspect"] = float(size[0] / size[1])


def reference_leaf(model, texture):
    """The leaf of a separate leaf model (a Lab leaf specimen), laid flat, and its texture: for a plant whose
    own leaves cannot be drawn from their texture."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=model)
    imported = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    obj = max(imported, key=lambda o: len(o.data.polygons))
    part = Part(obj.data)
    part.instances = [np.array(obj.matrix_world, dtype=np.float64)]
    sheet = leaf_sheet(part)
    islands = leaf_islands(sheet, list(range(max(1, len(obj.material_slots)))))
    if not islands:
        raise RuntimeError(f"{model}: no leaf surface found")
    flatten(islands[0])
    return islands[0], bpy.data.images.load(texture, check_existing=False)


class LeafAtlas:
    """The sprite sheet of a plant's leaves: one cell per distinct leaf (leaves that share their UVs are the
    same leaf, placed and scaled many times, so they share a cell), each leaf redrawn flat from the supplied
    texture. Alpha is the leaf itself (its exact outline, lesion holes included, antialiased 2 x 2), and the
    leaf colour is bled outwards under the transparent texels so filtering never pulls in a background."""

    PAD = 3
    MAX_DISTINCT = 64
    SHARED = 16

    def __init__(self):
        self.cells = {}         # UV layout -> (index, S, UV, aspect) of the first leaf with that layout
        self.shared = {}        # UV layout -> the layout whose sprite it uses (only when sprites are shared)

    def add(self, island):
        key = island["UV"].astype(np.float32).tobytes()
        flatten(island)
        if key not in self.cells:
            self.cells[key] = (len(self.cells), island["S"], island["UV"].copy(), island["flat_aspect"])
        island["cell"] = key

    def use_single(self, island):
        """Every leaf uses the sprite of one reference leaf, stretched over its own flat outline rectangle."""
        key = "reference"
        self.shared = {old: key for old in self.cells}
        self.cells = {key: (0, island["S"], island["UV"].copy(), island["flat_aspect"])}

    def bake(self, image, factor):
        """Draws every cell. Returns (rgb, alpha) of the sheet."""
        if len(self.cells) > self.MAX_DISTINCT:
            # A tree whose thousands of leaves all differ a little (the orange: 292 layouts) would get a
            # thumbnail per leaf. It keeps SHARED sprites instead, spread over the leaves' proportions, and every
            # other leaf is drawn with the kept sprite nearest its own length-to-width ratio: still a real leaf
            # of that tree, at full sharpness.
            ordered = sorted(self.cells.items(), key=lambda item: item[1][3])
            kept = [ordered[int((i + 0.5) * len(ordered) / self.SHARED)] for i in range(self.SHARED)]
            for key, cell in ordered:
                self.shared[key] = min(kept, key=lambda item: abs(math.log(item[1][3] / cell[3])))[0]
            self.cells = {key: (index,) + cell[1:] for index, (key, cell) in enumerate(kept)}
        count = len(self.cells)
        self.size = LEAF_SIZE if count <= 4 else LEAF_SIZE * 2
        long_leaves = float(np.median([c[3] for c in self.cells.values()])) > 3.0
        if count == 1:
            columns, rows = 1, 1
        elif long_leaves:       # blades: wide, low cells
            columns = max(1, int(math.ceil(math.sqrt(count / 4.0))))
            rows = int(math.ceil(count / columns))
        else:
            columns = int(math.ceil(math.sqrt(count)))
            rows = int(math.ceil(count / columns))
        self.cell_width, self.cell_height = self.size // columns, self.size // rows
        self.columns = columns
        fine = self.size * 2
        source_size = 2048
        source = image_pixels(image, source_size) * np.array(factor, dtype=np.float32)
        colour = np.zeros((fine, fine, 3), dtype=np.float32)
        covered = np.zeros((fine, fine), dtype=bool)
        for index, flat, uvs, _ in self.cells.values():
            origin, extent = self.rect(index)
            points = (origin + flat * extent) * 2.0          # fine texel coordinates
            for corners, uv in zip(points, uvs):
                a, b, c = corners
                x0, x1 = max(int(math.floor(min(a[0], b[0], c[0]))), 0), min(int(math.ceil(max(a[0], b[0], c[0]))) + 1, fine)
                y0, y1 = max(int(math.floor(min(a[1], b[1], c[1]))), 0), min(int(math.ceil(max(a[1], b[1], c[1]))) + 1, fine)
                d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
                if x1 <= x0 or y1 <= y0 or abs(d) < 1e-12:
                    continue
                x, y = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
                w0 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / d
                w1 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / d
                w2 = 1.0 - w0 - w1
                # A small tolerance, so neighbouring triangles leave no pinholes between them.
                inside = (w0 >= -0.03) & (w1 >= -0.03) & (w2 >= -0.03)
                if not inside.any():
                    continue
                w0, w1, w2 = np.clip(w0[inside], 0.0, 1.0), np.clip(w1[inside], 0.0, 1.0), np.clip(w2[inside], 0.0, 1.0)
                u = w0 * uv[0, 0] + w1 * uv[1, 0] + w2 * uv[2, 0]
                v = w0 * uv[0, 1] + w1 * uv[1, 1] + w2 * uv[2, 1]
                source_columns = np.clip((u % 1.0 * source_size).astype(np.int32), 0, source_size - 1)
                source_rows = np.clip((v % 1.0 * source_size).astype(np.int32), 0, source_size - 1)
                view_colour = colour[y0:y1, x0:x1]
                view_colour[inside] = source[source_rows, source_columns]
                covered[y0:y1, x0:x1] |= inside
        weight = covered.reshape(self.size, 2, self.size, 2).astype(np.float32)
        alpha = weight.mean(axis=(1, 3))
        summed = (colour.reshape(self.size, 2, self.size, 2, 3) * weight[..., None]).sum(axis=(1, 3))
        rgb = summed / np.maximum(weight.sum(axis=(1, 3)), 1.0)[..., None]
        filled = alpha > 0.0
        for _ in range(24):
            if filled.all():
                break
            total = np.zeros_like(rgb)
            near_count = np.zeros(filled.shape, dtype=np.float32)
            for shift in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                near = np.roll(filled, shift, axis=(0, 1))
                total += np.roll(rgb, shift, axis=(0, 1)) * near[..., None]
                near_count += near
            new = ~filled & (near_count > 0)
            rgb[new] = total[new] / near_count[new][:, None]
            filled |= new
        if (alpha > 0.5).any():
            rgb[~filled] = rgb[alpha > 0.5].mean(0)
        return rgb, alpha

    def rect(self, index):
        """Origin and extent (texels) of the leaf inside its cell."""
        origin = np.array([(index % self.columns) * self.cell_width + self.PAD, (index // self.columns) * self.cell_height + self.PAD], dtype=np.float64)
        return origin, np.array([self.cell_width - 2 * self.PAD, self.cell_height - 2 * self.PAD], dtype=np.float64)

    def place(self, island):
        """Replaces the island's supplied UVs with its sprite UVs."""
        key = island["cell"]
        if key in self.cells and self.shared.get(key, key) == key:
            index, flat, _, _ = self.cells[key]
        else:
            # Another leaf's sprite, stretched over this leaf's own flat outline rectangle.
            index, flat = self.cells[self.shared[key]][0], island["S"]
        origin, extent = self.rect(index)
        island["UV"] = (origin + flat * extent) / self.size
        island["low"] = island["UV"].reshape(-1, 2).min(0)
        island["high"] = island["UV"].reshape(-1, 2).max(0)


def leaf_card(island, cells, alpha, grow):
    """One leaf as a card: a grid over the rectangle of the sprite the leaf uses, every grid point lifted onto
    the leaf surface (found through the leaf's own UVs, so the card follows its curl), cells without leaf in
    the sprite dropped. The outline is the sprite's alpha, so it is exact whatever the grid. Returns
    (vertices, normals, triangles, triangle UVs)."""
    span = island["high"] - island["low"]
    low = island["low"] - span * 0.01 - 1e-5
    high = island["high"] + span * 0.01 + 1e-5
    tri_p, tri_uv, tri_n = island["P"], island["UV"], island["N"]
    a, b, c = tri_uv[:, 0], tri_uv[:, 1], tri_uv[:, 2]
    d = (b[:, 1] - c[:, 1]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 1] - c[:, 1])
    valid = np.abs(d) > 1e-14
    safe = np.where(valid, d, 1.0)
    centroids = tri_uv.mean(axis=1)
    # The leaf's overall plane: the least-squares map from a step in UV to a step in space.
    flat_uv = tri_uv.reshape(-1, 2)
    design = np.concatenate([flat_uv, np.ones((len(flat_uv), 1))], axis=1)
    plane = np.linalg.lstsq(design, tri_p.reshape(-1, 3), rcond=None)[0][:2].T

    def locate(uv):
        w0 = ((b[:, 1] - c[:, 1]) * (uv[0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (uv[1] - c[:, 1])) / safe
        w1 = ((c[:, 1] - a[:, 1]) * (uv[0] - c[:, 0]) + (a[:, 0] - c[:, 0]) * (uv[1] - c[:, 1])) / safe
        w2 = 1.0 - w0 - w1
        inside = valid & (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
        if inside.any():
            t = int(np.argmax(inside))
            weights = np.array([w0[t], w1[t], w2[t]])
            return weights @ tri_p[t], tri_n[t]
        # Outside the leaf (a corner of its rectangle; the sprite is transparent there): from the nearest point
        # of the leaf, continue along the leaf's overall plane. Continuing the nearest triangle's own plane
        # instead sends the corners of a long curved blade metres away.
        distance = ((centroids - uv) ** 2).sum(axis=1)
        distance[~valid] = 1e9
        t = int(distance.argmin())
        weights = np.clip(np.array([w0[t], w1[t], w2[t]]), 0.0, 1.0)
        total = weights.sum()
        weights = weights / total if total > 1e-9 else np.array([1 / 3, 1 / 3, 1 / 3])
        return weights @ tri_p[t] + plane @ (np.asarray(uv) - weights @ tri_uv[t]), tri_n[t]

    middle = (low + high) * 0.5
    length_u = np.linalg.norm(locate((high[0], middle[1]))[0] - locate((low[0], middle[1]))[0])
    length_v = np.linalg.norm(locate((middle[0], high[1]))[0] - locate((middle[0], low[1]))[0])
    cells = max(1, int(cells))
    cells_u = int(min(12, max(1, round(math.sqrt(cells * max(length_u, 1e-6) / max(length_v, 1e-6))))))
    cells_v = int(min(12, max(1, round(cells / cells_u))))
    vertices, normals, uvs = [], [], []
    for j in range(cells_v + 1):
        for i in range(cells_u + 1):
            uv = low + (high - low) * np.array([i / cells_u, j / cells_v])
            position, normal = locate(uv)
            vertices.append(island["centre"] + (position - island["centre"]) * grow)
            # Mostly the leaf's overall normal, with some of the local one: a scanned leaf is bumpy at the scale
            # of its triangles, and the normal of whichever tiny triangle a grid point lands on would light the
            # card like crumpled foil. This keeps the curl of the leaf and drops the noise.
            local = normal if np.dot(normal, island["normal"]) >= 0.0 else -normal
            blended = island["normal"] + 0.35 * local
            normals.append(blended / max(np.linalg.norm(blended), 1e-9))
            uvs.append(uv)
    vertices, normals, uvs = np.array(vertices), np.array(normals), np.array(uvs)
    size = alpha.shape[0]
    triangles = []
    for j in range(cells_v):
        for i in range(cells_u):
            corner = j * (cells_u + 1) + i
            cell_low, cell_high = uvs[corner], uvs[corner + cells_u + 2]
            c0, c1 = max(0, int(cell_low[0] * size) - 1), min(size, int(math.ceil(cell_high[0] * size)) + 1)
            r0, r1 = max(0, int(cell_low[1] * size) - 1), min(size, int(math.ceil(cell_high[1] * size)) + 1)
            if c1 > c0 and r1 > r0 and alpha[r0:r1, c0:c1].max() > 0.02:
                triangles.append((corner, corner + 1, corner + cells_u + 2))
                triangles.append((corner, corner + cells_u + 2, corner + cells_u + 1))
    if not triangles:
        triangles = [(0, 1, cells_u + 2), (0, cells_u + 2, cells_u + 1)]
    triangles = np.array(triangles, dtype=np.int32)
    first = vertices[triangles[0]]
    if np.dot(np.cross(first[1] - first[0], first[2] - first[0]), island["normal"]) < 0.0:
        triangles = triangles[:, ::-1]
    return vertices, normals, triangles, uvs[triangles]


def leaf_cards(part, target, level, alpha):
    """The cards of every leaf of a part, for a triangle target: a finer grid the more triangles a leaf gets
    (up to 24 cells up close, 8 further away). Below two triangles a leaf (the trees further away), evenly
    chosen leaves are kept and enlarged to keep the foliage as dense (up to 1.6 x). Same return shape as
    decimated()."""
    islands = part.islands
    per_leaf = target / len(islands)
    if per_leaf < 2.0:
        count = int(min(len(islands), max(1, target // 2)))
        chosen = [islands[int(i * len(islands) / count)] for i in range(count)]
        grow, cells = min(1.6, (len(islands) / count) ** 0.5), 1
    else:
        chosen, grow, cells = islands, 1.0, int(min(per_leaf / 2.0, 24 if level == 0 else 8))
    key = (cells, len(chosen))
    if key in part.cache:
        return part.cache[key]
    vertices, normals, triangles, uvs, materials = [], [], [], [], []
    offset = 0
    for island in chosen:
        card_vertices, card_normals, card_triangles, card_uvs = leaf_card(island, cells, alpha, grow)
        vertices.append(card_vertices)
        normals.append(card_normals)
        triangles.append(card_triangles + offset)
        uvs.append(card_uvs)
        materials.append(np.full(len(card_triangles), island["material"], dtype=np.int32))
        offset += len(card_vertices)
    result = (np.concatenate(vertices).astype(np.float32), np.concatenate(normals).astype(np.float32),
              np.concatenate(triangles), np.concatenate(uvs).astype(np.float32), np.concatenate(materials))
    part.cache[key] = result
    return result


def nearest_rotation(matrix):
    """The rotation closest to a 3 x 3 matrix (its scale and shear removed)."""
    u, _, vt = np.linalg.svd(matrix)
    rotation = u @ vt
    if np.linalg.det(rotation) < 0.0:
        u[:, -1] = -u[:, -1]
        rotation = u @ vt
    return rotation


def sample_motion(scene, first, last):
    """The wind of every mesh object over the loop, as KEYS rigid motions relative to the first frame:
    {object name: (pivot, rotations [KEYS] 3 x 3, travel [KEYS] of the pivot, largest angle in degrees,
    largest travel in metres)}, in world space. An object animated directly (or through its parents) moves
    rigidly, so this is its exact motion; a mesh bent by an armature (the rice blades) gets the best rigid fit
    to its vertices, about the part of it that moves least (where it joins the stem)."""
    objects = [o for o in scene.objects if o.type == "MESH" and len(o.data.vertices) > 0]
    skinned = {o.name for o in objects if any(m.type == "ARMATURE" for m in o.modifiers)}
    matrices = {o.name: [] for o in objects}
    shapes = {name: [] for name in skinned}
    for key in range(KEYS):
        frame = first + (last - first) * key / KEYS
        scene.frame_set(int(math.floor(frame)), subframe=frame - math.floor(frame))
        depsgraph = bpy.context.evaluated_depsgraph_get()
        for obj in objects:
            matrix = np.array(obj.matrix_world, dtype=np.float64)
            matrices[obj.name].append(matrix)
            if obj.name in skinned:
                data = obj.evaluated_get(depsgraph).data
                coords = np.empty(len(data.vertices) * 3, dtype=np.float32)
                data.vertices.foreach_get("co", coords)
                shapes[obj.name].append(coords.reshape(-1, 3).astype(np.float64) @ matrix[:3, :3].T + matrix[:3, 3])
    scene.frame_set(int(first))
    bpy.context.view_layer.update()

    motion = {}
    for obj in objects:
        name = obj.name
        keys = matrices[name]
        if name in skinned and len(shapes[name][0]) == len(obj.data.vertices):
            # The geometry is exported in its rest shape, so that is the reference.
            rest = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
            obj.data.vertices.foreach_get("co", rest)
            rest = rest.reshape(-1, 3).astype(np.float64) @ keys[0][:3, :3].T + keys[0][:3, 3]
            moved = np.max([np.linalg.norm(shape - rest, axis=1) for shape in shapes[name]], axis=0)
            still = moved <= np.quantile(moved, 0.1)
            pivot = rest[still].mean(0)
            rotations, travel = [], []
            centre = rest.mean(0)
            for shape in shapes[name]:
                shape_centre = shape.mean(0)
                rotation = nearest_rotation((shape - shape_centre).T @ (rest - centre))
                rotations.append(rotation)
                travel.append(rotation @ (pivot - centre) + shape_centre - pivot)
        else:
            inverse = np.linalg.inv(keys[0])
            pivot = keys[0][:3, 3].copy()
            rotations, travel = [], []
            for matrix in keys:
                delta = matrix @ inverse
                rotations.append(nearest_rotation(delta[:3, :3]))
                travel.append(delta[:3, :3] @ pivot + delta[:3, 3] - pivot)
        # Relative to key 0 exactly (identity, no travel), which is the exported pose.
        base, base_travel = rotations[0].copy(), travel[0].copy()
        rotations = [r @ base.T for r in rotations]
        travel = [t - base_travel for t in travel]
        angle = max(math.degrees(math.acos(max(-1.0, min(1.0, (np.trace(r) - 1.0) * 0.5)))) for r in rotations)
        distance = max(float(np.linalg.norm(t)) for t in travel)
        motion[name] = (pivot, rotations, np.array(travel), angle, distance)
    return motion


def write_wind(path, entries, motion, centre, scale, seconds):
    """<Stem>_<disease>_Wind.bytes, version 3 of the format read by Assets/_Project/Scripts/Editor/LeafWindBaker.cs:
    b"BTPW", uint32 version = 3, uint32 parts, uint32 keys, float32 loop seconds, uint32 anchor sets (1), then per
    part: pivot xyz, anchor xyz (a tagged vertex of the part in LOD0), keys x (w, x, y, z) rotation relative to
    key 0, keys x (x, y, z) travel of the pivot relative to key 0. In the exported plant's own space (metres,
    Blender axes)."""
    from mathutils import Matrix
    with open(path, "wb") as f:
        f.write(b"BTPW")
        f.write(struct.pack("<IIIfI", 3, len(entries), KEYS, seconds, 1))
        for name, anchor in entries:
            pivot, rotations, travel, _, _ = motion[name]
            f.write(struct.pack("<3f", *((pivot - centre) * scale)))
            f.write(struct.pack("<3f", *((anchor - centre) * scale)))
            previous = None
            for rotation in rotations:
                q = Matrix(rotation.tolist()).to_quaternion().normalized()
                if (previous is None and q.w < 0.0) or (previous is not None and q.dot(previous) < 0.0):
                    q.negate()
                previous = q
                f.write(struct.pack("<4f", q.w, q.x, q.y, q.z))
            for offset in travel:
                f.write(struct.pack("<3f", *(offset * scale)))


def largest_islands(vertices, triangles, limit):
    """Mask of the triangles in the largest connected pieces (by surface area) whose triangle count fits the
    limit; always at least the largest piece."""
    parent = np.arange(len(vertices))

    def find(i):
        root = i
        while parent[root] != root:
            root = parent[root]
        while parent[i] != root:
            parent[i], i = root, parent[i]
        return root

    for a, b, c in triangles:
        ra, rb, rc = find(a), find(b), find(c)
        parent[rb] = ra
        parent[rc] = ra
    roots = np.array([find(v) for v in triangles[:, 0]])
    corners = vertices[triangles]
    areas = 0.5 * np.linalg.norm(np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]), axis=1)
    labels, inverse, counts = np.unique(roots, return_inverse=True, return_counts=True)
    island_area = np.bincount(inverse, weights=areas, minlength=len(labels))
    keep = np.zeros(len(labels), dtype=bool)
    total = 0
    for island in np.argsort(-island_area):
        if total and total + counts[island] > limit:
            continue
        keep[island] = True
        total += counts[island]
    return keep[inverse]


def solve_ratio(parts, budget):
    """The single ratio r with sum(instances x max(floor, r x triangles)) = budget (0 when the floors alone exceed it)."""
    def total(r):
        return sum(len(p.instances) * max(min(p.floor, p.tris), r * p.tris) for p in parts)

    if total(0.0) >= budget:
        return 0.0
    low, high = 0.0, 1.0
    if total(1.0) <= budget:
        return 1.0
    for _ in range(40):
        mid = (low + high) * 0.5
        if total(mid) > budget:
            high = mid
        else:
            low = mid
    return low


class Layout:
    """Where each source material lands in the parts atlas."""

    def __init__(self, sources):
        self.textured = sorted((s for s in sources if not s.leaf and not s.drop and s.image is not None and not s.tiles),
                               key=lambda s: s.name)[:15]
        names = {s.name for s in self.textured}
        self.solid = sorted((s for s in sources if not s.leaf and not s.drop and s.name not in names), key=lambda s: s.name)
        cells = len(self.textured) + (1 if self.solid else 0)
        self.grid = 1 if cells <= 1 else 2 if cells <= 4 else 4
        self.size = self.grid * CELL
        self.key = "|".join([s.name for s in self.textured] + ["#"] + [s.name for s in self.solid])
        self.cell_of = {s.name: i for i, s in enumerate(self.textured)}
        self.swatch_cell = len(self.textured)
        self.swatch_of = {s.name: i for i, s in enumerate(self.solid)}
        if len(self.solid) > SWATCH_GRID * SWATCH_GRID:
            raise RuntimeError("too many solid colours for one swatch cell")

    def cell_origin(self, cell):
        return (cell % self.grid) * CELL, (cell // self.grid) * CELL

    def remap(self, name, uvs):
        """Atlas UVs for a triangle soup of one source material."""
        if name in self.cell_of:
            x, y = self.cell_origin(self.cell_of[name])
            inner = CELL - 2 * PAD
            out = np.empty_like(uvs)
            out[..., 0] = (x + PAD + np.clip(uvs[..., 0], 0.0, 1.0) * inner) / self.size
            out[..., 1] = (y + PAD + np.clip(uvs[..., 1], 0.0, 1.0) * inner) / self.size
            return out
        index = self.swatch_of[name]
        x, y = self.cell_origin(self.swatch_cell)
        step = CELL / SWATCH_GRID
        out = np.empty_like(uvs)
        out[..., 0] = (x + (index % SWATCH_GRID + 0.5) * step) / self.size
        out[..., 1] = (y + (index // SWATCH_GRID + 0.5) * step) / self.size
        return out

    def render(self):
        atlas = np.zeros((self.size, self.size, 3), dtype=np.float32)
        atlas[:, :] = (0.3, 0.35, 0.2)
        inner = CELL - 2 * PAD
        for source in self.textured:
            x, y = self.cell_origin(self.cell_of[source.name])
            pixels = image_pixels(source.image, inner) * np.array(srgb_factor(source.factor), dtype=np.float32)
            atlas[y:y + CELL, x:x + CELL] = np.pad(pixels, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="edge")
        if self.solid:
            x, y = self.cell_origin(self.swatch_cell)
            step = CELL // SWATCH_GRID
            for source in self.solid:
                index = self.swatch_of[source.name]
                sx, sy = x + (index % SWATCH_GRID) * step, y + (index // SWATCH_GRID) * step
                atlas[sy:sy + step, sx:sx + step] = source.colour
        return atlas


def srgb_factor(factor):
    """A linear colour factor as the multiplier to apply to sRGB pixels (exact for white, close otherwise)."""
    return tuple(1.0 if f > 0.999 else srgb((f,))[0] for f in factor)


def build_mesh(name, soup, materials):
    """A mesh object from per-material triangle soups: {material index: (vertices, normals, uvs)}, three vertices
    per triangle. Identical corners are welded so shading stays smooth across the collapsed surface."""
    all_vertices, all_normals, all_uvs, all_wind, all_materials = [], [], [], [], []
    for index in sorted(soup):
        vertices, normals, uvs, wind = soup[index]
        all_vertices.append(vertices)
        all_normals.append(normals)
        all_uvs.append(uvs)
        all_wind.append(wind)
        all_materials.append(np.full(len(vertices) // 3, index, dtype=np.int32))
    corners = np.concatenate(all_vertices)
    normals = np.concatenate(all_normals)
    uvs = np.concatenate(all_uvs)
    wind = np.concatenate(all_wind)
    polygon_materials = np.concatenate(all_materials)
    # Weld corners that share a position and a normal (0.01 mm / 1e-3 grid).
    key = np.concatenate([np.round(corners * 1e5), np.round(normals * 1e3)], axis=1)
    _, first, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.reshape(-1)
    vertices = corners[first]
    vertex_normals = normals[first]
    triangles = len(corners) // 3

    mesh = bpy.data.meshes.new(name)
    mesh.vertices.add(len(vertices))
    mesh.vertices.foreach_set("co", vertices.astype(np.float32).ravel())
    mesh.loops.add(triangles * 3)
    mesh.loops.foreach_set("vertex_index", inverse.astype(np.int32))
    mesh.polygons.add(triangles)
    mesh.polygons.foreach_set("loop_start", np.arange(0, triangles * 3, 3, dtype=np.int32))
    mesh.polygons.foreach_set("loop_total", np.full(triangles, 3, dtype=np.int32))
    mesh.polygons.foreach_set("material_index", polygon_materials)
    mesh.polygons.foreach_set("use_smooth", np.ones(triangles, dtype=bool))
    layer = mesh.uv_layers.new(name="UVMap")
    layer.data.foreach_set("uv", uvs.astype(np.float32).ravel())
    # Second UV set (Unity UV1): u = wind part index + 1 (0 = still), v = 1 on the part's tagged vertex.
    mesh.uv_layers.new(name="Wind").data.foreach_set("uv", wind.astype(np.float32).ravel())
    mesh.uv_layers.active_index = 0
    mesh.uv_layers["UVMap"].active_render = True
    for material in materials:
        mesh.materials.append(material)
    mesh.update(calc_edges=True)
    mesh.validate(clean_customdata=False)
    lengths = np.linalg.norm(vertex_normals, axis=1, keepdims=True)
    vertex_normals = np.where(lengths > 1e-6, vertex_normals / np.maximum(lengths, 1e-6), (0.0, 0.0, 1.0))
    if len(mesh.vertices) == len(vertex_normals):
        mesh.normals_split_custom_set_from_vertices(vertex_normals.tolist())
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def preview_material(name, path):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(path, check_existing=False)
    tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    shader.inputs["Roughness"].default_value = 0.8
    tree.nodes.active = texture
    material.use_backface_culling = False
    return material


def render_preview(path, objects, height):
    """LOD0 and LOD1 side by side from the front, with their textures (Workbench)."""
    scene = bpy.context.scene
    spread = max(max(o.dimensions.x, o.dimensions.y) for o in objects) * 1.15
    for i, obj in enumerate(objects):
        obj.location.x = (i - (len(objects) - 1) * 0.5) * spread
    camera_data = bpy.data.cameras.new("preview")
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = max(spread * len(objects), height * 1.1)
    camera = bpy.data.objects.new("preview", camera_data)
    scene.collection.objects.link(camera)
    camera.location = (0.0, -max(20.0, height * 6.0), height * 0.5)
    camera.rotation_euler = (math.radians(90.0), 0.0, 0.0)
    camera_data.clip_end = 500.0
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_backface_culling = False
    scene.render.resolution_x = 1400
    scene.render.resolution_y = int(1400 * max(0.45, min(1.2, height * 1.1 / camera_data.ortho_scale)))
    scene.render.film_transparent = False
    if scene.world is None:
        scene.world = bpy.data.worlds.new("preview")
    scene.world.color = (0.55, 0.6, 0.65)
    scene.render.filepath = path
    scene.render.image_settings.file_format = "PNG"
    try:
        bpy.ops.render.render(write_still=True)
    except Exception as error:  # a preview must never fail the export
        log("preview failed:", error)
    for obj in objects:
        obj.location.x = 0.0
    bpy.data.objects.remove(camera)


# ----------------------------------------------------------------------------- one plant

def export_variant(crop, disease, file_name, out, atlas_cache, previews):
    path = os.path.join(SOURCE, crop["folder"], file_name)
    stem = crop["stem"]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Merged vertices: the GLB splits them at every UV seam and hard edge, which edge collapse cannot cross.
    bpy.ops.import_scene.gltf(filepath=path, merge_vertices=True)
    scene = bpy.context.scene
    ranges = [action.frame_range for action in bpy.data.actions]
    first = float(min(r[0] for r in ranges)) if ranges else float(scene.frame_start)
    last = float(max(r[1] for r in ranges)) if ranges else float(scene.frame_start)
    fps = scene.render.fps / scene.render.fps_base
    seconds = (last - first) / fps
    # The wind first (it leaves the scene on the loop's first frame, the pose that is exported).
    motion = sample_motion(scene, first, last) if last > first else {}

    sources = {}
    root_points = []
    parts = {}
    uv_ranges = {}
    for obj in scene.objects:
        if obj.type != "MESH" or not obj.material_slots or len(obj.data.polygons) == 0:
            continue
        mesh = obj.data
        for slot in obj.material_slots:
            if slot.material is not None and slot.material.name not in sources:
                sources[slot.material.name] = Source(slot.material, crop.get("keep", ()))
        # Where the plant stands: its roots and soil plug are not exported, but they mark the spot.
        if any(s.material and any(k in s.material.name.lower() for k in ("root", "soil", "loam")) for s in obj.material_slots):
            box = np.array([obj.matrix_world @ Vector(c) for c in obj.bound_box])
            root_points.append((box.min(0) + box.max(0)) * 0.5)
        part = parts.get(mesh.name)
        if part is None:
            part = parts[mesh.name] = Part(mesh)
            mesh.calc_loop_triangles()
            materials = np.empty(len(mesh.loop_triangles), dtype=np.int32)
            mesh.loop_triangles.foreach_get("material_index", materials)
            slots = [s.material.name if s.material else None for s in obj.material_slots]
            counts = np.bincount(materials, minlength=len(slots))
            part.slots = slots
            kept = [counts[i] for i, n in enumerate(slots) if n and not sources[n].drop and i < len(counts)]
            part.tris = int(sum(kept))
            leaf = sum(counts[i] for i, n in enumerate(slots) if n and sources[n].leaf and i < len(counts))
            part.leaf = leaf * 2 > part.tris and part.tris > 0
            # UV extent per material, to find the parts whose texture tiles.
            if mesh.uv_layers:
                uvs = np.empty(len(mesh.loops) * 2, dtype=np.float32)
                mesh.uv_layers[0].data.foreach_get("uv", uvs)
                loops = np.empty(len(mesh.loop_triangles) * 3, dtype=np.int32)
                mesh.loop_triangles.foreach_get("loops", loops)
                tri_uvs = uvs.reshape(-1, 2)[loops].reshape(-1, 3, 2)
                for i, n in enumerate(slots):
                    selected = tri_uvs[materials == i]
                    if n and len(selected):
                        low, high = uv_ranges.get(n, (np.array([1e9, 1e9]), np.array([-1e9, -1e9])))
                        uv_ranges[n] = (np.minimum(low, selected.min((0, 1))), np.maximum(high, selected.max((0, 1))))
            corners = np.array([obj.matrix_world @ Vector(c) for c in obj.bound_box])
            part.diagonal = float(np.linalg.norm(corners.max(0) - corners.min(0)))
        if part.tris > 0:
            part.instances.append(np.array(obj.matrix_world, dtype=np.float64))
            part.objects.append(obj.name)

    for name, (low, high) in uv_ranges.items():
        source = sources[name]
        source.tiles = bool(low.min() < -0.02 or high.max() > 1.02)
        if source.image is not None and (source.tiles or source.drop):
            if not source.drop:
                mean = image_pixels(source.image, 32).reshape(-1, 3).mean(0)
                source.colour = tuple(float(c) for c in mean * np.array(srgb_factor(source.factor)))
    used = [p for p in parts.values() if p.tris > 0 and p.instances]
    atlas = LeafAtlas()
    # The plant's height is measured properly further down; the tallest object is close enough to tell a fruit
    # from a bud here.
    source_height_guess = max(p.diagonal for p in used)
    for part in used:
        part.source_tris = part.tris
        if part.leaf:
            part.mesh = leaf_sheet(part)
            part.mesh.calc_loop_triangles()
            part.tris = len(part.mesh.loop_triangles)
            part.islands = leaf_islands(part.mesh, [i for i, n in enumerate(part.slots) if n and sources[n].leaf])
            for island in part.islands:
                atlas.add(island)
        points = np.empty(len(part.mesh.vertices) * 3, dtype=np.float32)
        part.mesh.vertices.foreach_get("co", points)
        points = points.reshape(-1, 3).astype(np.float64)
        centred = points - points.mean(0)
        spread = np.sqrt(np.maximum(np.linalg.eigvalsh(centred.T @ centred / max(len(points), 1)), 1e-12))
        part.aspect = float(spread[2] / spread[1])      # length over width
        part.girth = float(spread[0])                   # spread across the thinnest direction
        areas = np.empty(len(part.mesh.polygons), dtype=np.float32)
        part.mesh.polygons.foreach_get("area", areas)
        part.area = float(areas.sum())
        # Compact parts (fruit, bolls, pods: a closed body, surface area comparable to its extent squared) against
        # thin ones (stems, twigs, panicle branches: little surface for their length).
        extent = float(np.linalg.norm(points.max(0) - points.min(0)))
        part.compact = not part.leaf and part.area > 0.3 * extent * extent
        if part.compact and crop.get("fruit") == "hull" and part.diagonal >= 0.02 * source_height_guess:
            before_hull = part.tris
            part.hulled = True
            # About 160 triangles up close, about 44 further away.
            part.mesh_far = hull_mesh(part.mesh, 24)
            part.mesh = hull_mesh(part.mesh, 82)
            part.mesh.calc_loop_triangles()
            part.tris = max(1, len(part.mesh.loop_triangles))
            if os.environ.get("FARM_CROPS_DEBUG"):
                log(f"  hull {part.mesh.name[:44]:44s} x{len(part.instances):3d} {before_hull} -> {part.tris} triangles, {part.diagonal:.3f} m")
    leaves = [s for s in sources.values() if s.leaf]
    if not leaves:
        raise RuntimeError(f"{file_name}: no disease leaf material found among {sorted(sources)}")
    leaf_source = max(leaves, key=lambda s: sum(p.tris * len(p.instances) for p in used if p.leaf and s.name in p.slots))
    used = [p for p in used if not p.leaf or p.islands]
    leaf_image, leaf_factor = leaf_source.image, srgb_factor(leaf_source.factor)
    if crop.get("leaf_model"):
        reference, leaf_image = reference_leaf(*(os.path.join(ROOT, *path.split("/")) for path in crop["leaf_model"]))
        atlas.use_single(reference)
        leaf_factor = (1.0, 1.0, 1.0)
    leaf_rgb, leaf_alpha = atlas.bake(leaf_image, leaf_factor)
    for part in used:
        if part.leaf:
            for island in part.islands:
                atlas.place(island)
    leaf_total = sum(len(p.islands) * len(p.instances) for p in used if p.leaf)
    wind_index = {}         # object name -> wind part index
    wind_entries = []       # (object name, anchor position), in index order

    # Size of the kept plant, for the scale and the LOD1 small-part cut.
    low, high = np.full(3, 1e9), np.full(3, -1e9)
    for part in used:
        vertices = np.empty(len(part.mesh.vertices) * 3, dtype=np.float32)
        part.mesh.vertices.foreach_get("co", vertices)
        vertices = vertices.reshape(-1, 3)
        for matrix in part.instances:
            world = vertices @ matrix[:3, :3].T + matrix[:3, 3]
            low, high = np.minimum(low, world.min(0)), np.maximum(high, world.max(0))
    source_height = float(high[2] - low[2])
    compact_total = sum(len(p.instances) for p in used if p.compact and p.diagonal >= 0.02 * source_height)
    if crop.get("stalks"):
        sources[STALK] = GeneratedSource(STALK, crop["stalks"]["colour"])
    layout = Layout(list(sources.values()))
    report = dict(disease=disease, source=file_name, source_height=round(source_height, 3),
                  source_triangles=int(sum(p.source_tris * len(p.instances) for p in used)),
                  leaf_material=leaf_source.name,
                  atlas=[s.name for s in layout.textured],
                  solid={s.name: [round(c, 3) for c in s.colour] for s in layout.solid},
                  named_colours={s.name: s.fallback for s in layout.solid if s.fallback},
                  dropped=sorted(s.name for s in sources.values() if s.drop))

    def assemble(level, budget, cut, floor_scale, leaf_scale):
        # Fruit stays at every distance: a tree whose fruit appears as you walk up to it reads as a glitch.
        chosen = [p for p in used if p.leaf or p.diagonal >= cut * source_height
                  or (p.compact and p.diagonal >= 0.02 * source_height)]
        # Floors: a leaf keeps a recognisable outline, more of it the longer the blade (a grass blade collapsed
        # to eight triangles is a stub); any other part keeps triangles in proportion to its size, so a long
        # stem stays a tube (a 6-triangle stem is a wedge) while a pedicel may become a sliver.
        for part in chosen:
            if part.leaf:
                # Leaves are cards under the cut-out sprite, so the outline is exact at any count; the triangles
                # buy how closely the card follows the leaf's curl. A plant with a few hundred leaves gets a
                # 12-cell grid a leaf up close and 4 further away; a tree (thousands) one quad a leaf up close
                # and every other leaf further away.
                few = leaf_total <= 600
                part.tris = len(part.islands) * (48 if level == 0 else 16)
                part.floor = leaf_scale * len(part.islands) * ((24.0 if level == 0 else 8.0) if few else (2.0 if level == 0 else 1.0))
            elif part.compact and part.diagonal >= 0.02 * source_height:
                # A fruit is looked at closely and its outline is a smooth curve: under about a hundred triangles
                # it reads as a faceted, cut-open block.
                # Up to 160 triangles each up close (48 further away), within a third (a fifth) of the plant's
                # budget shared by all of them: an orange tree carries hundreds.
                share = (0.4 if level == 0 else 0.25) * crop["tris"][level] / max(compact_total, 1)
                part.floor = max(0.5, floor_scale) * (min(160.0, max(12.0, share)) if level == 0 else min(48.0, max(8.0, share)))
            else:
                part.floor = floor_scale * max(12 if level == 0 else 6, (260 if level == 0 else 130) * part.diagonal / source_height)
        ratio = solve_ratio(chosen, budget)
        soup = {0: [[], [], [], []], 1: [[], [], [], []]}
        for part in chosen:
            target = max(min(part.floor, part.tris), ratio * part.tris)
            part_ratio = min(1.0, target / part.tris)
            if part.leaf:
                vertices, normals, tri_vertices, tri_uvs, tri_materials = leaf_cards(part, target, level, leaf_alpha)
            elif getattr(part, "hulled", False):
                # Built at its final size for each level; never collapsed.
                vertices, normals, tri_vertices, tri_uvs, tri_materials = decimated(part.mesh if level == 0 else part.mesh_far, 1.0)
            else:
                vertices, normals, tri_vertices, tri_uvs, tri_materials = decimated(part.mesh, part_ratio)
            if os.environ.get("FARM_CROPS_DEBUG") and getattr(part, "hulled", False):
                c0 = vertices[tri_vertices]
                got_area = float((0.5 * np.linalg.norm(np.cross(c0[:, 1] - c0[:, 0], c0[:, 2] - c0[:, 0]), axis=1)).sum())
                log(f"  L{level} hulled {part.mesh.name[:34]:34s} target {target:6.0f} ratio {part_ratio:.3f} got {len(tri_vertices):5d} tris, area {got_area:.5f} of {part.area:.5f}, slots {np.bincount(tri_materials).tolist()} {part.slots}")
            if part.compact and len(tri_vertices) < 0.9 * part.source_tris and len(vertices):
                # A collapsed fruit is faceted, and its facet normals make it look crumpled. Normals
                # that point out from its centre shade it as the rounded body it is.
                used_vertices = np.unique(tri_vertices)
                outward = vertices - vertices[used_vertices].mean(0)
                lengths = np.linalg.norm(outward, axis=1, keepdims=True)
                normals = np.where(lengths > 1e-9, outward / np.maximum(lengths, 1e-9), normals).astype(np.float32)
            if not part.leaf and not getattr(part, "hulled", False) and len(tri_vertices) > 1.5 * target + 24:
                # Edge collapse stops at one small shell per loose piece (a flower's stamens, a panicle's grains):
                # keep the biggest pieces that fit the target.
                keep = largest_islands(vertices, tri_vertices, int(1.5 * target + 24))
                tri_vertices, tri_uvs, tri_materials = tri_vertices[keep], tri_uvs[keep], tri_materials[keep]
            if not part.leaf and not part.compact and len(tri_vertices) > 8:
                # A branching network (the stems of a panicle) collapses into webs between its branches: triangles
                # many times the size any triangle of the part should have. They are left out.
                corners = vertices[tri_vertices]
                areas = 0.5 * np.linalg.norm(np.cross(corners[:, 1] - corners[:, 0], corners[:, 2] - corners[:, 0]), axis=1)
                keep = areas <= 8.0 * part.area / len(tri_vertices)
                if keep.any() and not keep.all():
                    tri_vertices, tri_uvs, tri_materials = tri_vertices[keep], tri_uvs[keep], tri_materials[keep]
            if os.environ.get("FARM_CROPS_DEBUG") and len(tri_vertices) > 2.0 * target + 20:
                log(f"  L{level} {part.mesh.name[:40]:40s} x{len(part.instances):3d} tris {part.tris:6d} target {target:7.0f} got {len(tri_vertices):6d} leaf={part.leaf}")
            for slot, name in enumerate(part.slots):
                if name is None or sources[name].drop:
                    continue
                selected = tri_materials == slot
                if not selected.any():
                    continue
                indices = tri_vertices[selected]
                is_leaf = sources[name].leaf
                uvs = tri_uvs[selected] if is_leaf else layout.remap(name, tri_uvs[selected])
                # Every leaf material of a plant shares the one disease leaf texture (they are the same leaf).
                target_soup = soup[0 if is_leaf else 1]
                for matrix, object_name in zip(part.instances, part.objects):
                    rotation = matrix[:3, :3]
                    world = vertices[indices.ravel()] @ rotation.T + matrix[:3, 3]
                    world_normals = normals[indices.ravel()] @ np.linalg.inv(rotation)
                    corner_uvs = uvs.reshape(-1, 2)
                    if np.linalg.det(rotation) < 0.0:   # mirrored instance: restore the winding
                        world = world.reshape(-1, 3, 3)[:, ::-1].reshape(-1, 3)
                        world_normals = world_normals.reshape(-1, 3, 3)[:, ::-1].reshape(-1, 3)
                        corner_uvs = corner_uvs.reshape(-1, 3, 2)[:, ::-1].reshape(-1, 2)
                    # Wind: every moving object is one wind part; LOD1 reuses the parts LOD0 made.
                    corner_wind = np.zeros((len(world), 2), dtype=np.float32)
                    moving = object_name in motion and (motion[object_name][3] > 0.2 or motion[object_name][4] > 5e-4)
                    if moving and object_name not in wind_index and level == 0:
                        wind_index[object_name] = len(wind_entries)
                        wind_entries.append((object_name, world[0].astype(np.float64).copy()))
                    if moving and object_name in wind_index:
                        number = wind_index[object_name]
                        corner_wind[:, 0] = number + 1
                        if level == 0:
                            anchor = wind_entries[number][1]
                            corner_wind[np.abs(world - anchor).max(axis=1) < 1e-7, 1] = 1.0
                    target_soup[0].append(world)
                    target_soup[1].append(world_normals)
                    target_soup[2].append(corner_uvs)
                    target_soup[3].append(corner_wind)
        return {k: tuple(np.concatenate(v) for v in lists) for k, lists in soup.items() if lists[0]}, ratio

    soups = []
    for level, budget in enumerate(crop["tris"]):
        # Some parts stop collapsing above their share (a lobed leaf's outline). Ask the rest for less until the
        # whole plant is near its budget.
        # When the floors alone exceed it (the hundreds of leaves and twigs of a tree), the floors of whichever
        # side is over its share are lowered; LOD1 also leaves out more of the smallest parts, up to 8 % of the
        # plant height (LOD0 keeps every part: its fruit and twigs are what is looked at up close).
        asked = float(budget)
        cut = 0.045 if level == 1 else 0.0      # LOD1 starts without the parts under 4.5 % of the plant height
        floor_scale = leaf_scale = 1.0
        for _ in range(10):
            if level == 0:
                wind_index.clear()
                wind_entries.clear()
            soup, ratio = assemble(level, asked, cut, floor_scale, leaf_scale)
            leaf_count = len(soup[0][0]) // 3 if 0 in soup else 0
            other = len(soup[1][0]) // 3 if 1 in soup else 0
            count = leaf_count + other
            if count <= 1.12 * budget:
                break
            if ratio > 0.0:
                asked *= budget / count
            elif leaf_count > 0.6 * budget and leaf_scale > 0.25:
                leaf_scale *= 0.75
            elif other > 0.4 * budget and floor_scale > 0.2:
                floor_scale *= 0.75
                if level == 1:
                    cut = min(0.08, max(cut, 0.03) * 1.3)
            else:
                break
        soups.append(soup)
        report[f"lod{level}_ratio"] = round(ratio, 6)
        report[f"lod{level}_smallest_part"] = round(cut, 4)
        report[f"lod{level}_leaf_triangles"] = leaf_count

    if crop.get("stalks"):
        def world_points(part, matrix):
            points = np.empty(len(part.mesh.vertices) * 3, dtype=np.float32)
            part.mesh.vertices.foreach_get("co", points)
            return points.reshape(-1, 3).astype(np.float64) @ matrix[:3, :3].T + matrix[:3, 3]

        fruit_keys = crop["stalks"]["fruit"]
        wood, fruits = [], []
        for part in used:
            names = [n.lower() for n in part.slots if n]
            if part.leaf:
                continue
            if any(k in n for n in names for k in fruit_keys):
                for matrix, object_name in zip(part.instances, part.objects):
                    points = world_points(part, matrix)
                    size = float(np.linalg.norm(points.max(0) - points.min(0)))
                    fruits.append((points[points[:, 2].argmax()], size, wind_index.get(object_name, -1) + 1))
            elif any("bark" in n for n in names):
                # Bark-covered branches only: the twigs the fruit grows on are hair-thin in these models (which is
                # why the fruit seemed to float), and a stalk that stops at one of them is an invisible nub.
                for matrix in part.instances:
                    points = world_points(part, matrix)
                    wood.append(points[::max(1, len(points) // 300)])
        wood = np.concatenate(wood) if wood else np.zeros((0, 3))
        for level, soup in enumerate(soups):
            corners, stalk_normals, numbers = fruit_stalks(fruits, wood, level, crop["stalks"]["thickness"])
            if len(corners) == 0 or 1 not in soup:
                continue
            stalk_wind = np.zeros((len(corners), 2), dtype=np.float32)
            stalk_wind[:, 0] = numbers
            stalk_uvs = layout.remap(STALK, np.zeros((len(corners), 2), dtype=np.float32))
            vertices, normals, uvs, wind = soup[1]
            soup[1] = (np.concatenate([vertices, corners]), np.concatenate([normals, stalk_normals]),
                       np.concatenate([uvs, stalk_uvs]), np.concatenate([wind, stalk_wind]))
        report["fruit_stalks"] = len(fruits)
        if os.environ.get("FARM_CROPS_DEBUG"):
            lengths = []
            for top, size, number in fruits:
                offset = wood - top
                reachable = (offset[:, 2] > 0.005) & (np.linalg.norm(offset[:, :2], axis=1) < 1.0)
                lengths.append(float(np.where(reachable, np.linalg.norm(offset, axis=1), 1e9).min()) if len(wood) else -1.0)
            log(f"  stalks: {len(fruits)} fruit, {len(wood)} wood points, fruit size {np.mean([f[1] for f in fruits]):.3f}, "
                f"length to wood min {min(lengths):.3f} median {float(np.median(lengths)):.3f} max {max(lengths):.3f}, "
                f"fruit top z {np.mean([f[0][2] for f in fruits]):.2f}, wood z {wood[:, 2].min():.2f}..{wood[:, 2].max():.2f}, "
                f"stalk corners L0 {len(fruit_stalks(fruits, wood, 0, crop['stalks']['thickness'])[0])}")

    # Stand the plant on the origin, lowest point at 0: over its roots (they and the soil plug are not exported,
    # but they mark where it grows); without any, over the lowest slice of the plant. A sprawling plant (the
    # squash, whose vines run a metre from the root) is centred on its footprint instead, so it lies on its bed.
    everything = np.concatenate([v[0] for v in soups[0].values()])
    bottom = float(everything[:, 2].min())
    height = float(everything[:, 2].max() - bottom)
    if crop.get("origin") == "footprint":
        spot = (everything.min(0) + everything.max(0)) * 0.5
        origin = "footprint"
    elif root_points:
        spot = np.mean(root_points, axis=0)
        origin = "roots"
    else:
        spot = everything[everything[:, 2] < bottom + 0.03 * height].mean(0)
        origin = "lowest slice"
    centre = np.array([spot[0], spot[1], bottom])
    if len(wind_entries) >= 2040:
        raise RuntimeError(f"{len(wind_entries)} moving parts: wind indices above 2047 do not survive half-precision UVs")
    target = crop.get("height")
    if target is None and crop.get("max_height") and height > crop["max_height"]:
        target = crop["max_height"]
    scale = target / height if target else 1.0
    for soup in soups:
        for key, (vertices, normals, uvs, wind) in list(soup.items()):
            soup[key] = ((vertices - centre) * scale, normals, uvs, wind)

    os.makedirs(out, exist_ok=True)
    leaf_path = os.path.join(out, f"{stem}_{disease}_Leaf.png")
    save_png(leaf_path, leaf_rgb, leaf_alpha)
    wind_name = None
    if wind_entries:
        wind_name = f"{stem}_{disease}_Wind.bytes"
        write_wind(os.path.join(out, wind_name), wind_entries, motion, centre, scale, seconds)
    report["origin"] = origin
    report["leaves"] = leaf_total
    report["leaf_sprite_coverage"] = round(float((leaf_alpha > 0.5).mean()), 3)
    report["leaf_sprites"] = len(atlas.cells)
    report["leaf_sprite_source"] = crop["leaf_model"][0] if crop.get("leaf_model") else "own leaves"
    report["fruit_like_parts"] = compact_total
    report["leaf_atlas_size"] = atlas.size
    report["wind"] = dict(file=wind_name, parts=len(wind_entries), keys=KEYS, seconds=round(seconds, 4), fps=fps,
                          max_angle_deg=round(max((motion[n][3] for n, _ in wind_entries), default=0.0), 2),
                          max_travel_m=round(max((motion[n][4] for n, _ in wind_entries), default=0.0) * scale, 4))
    if atlas_cache.get("key") == layout.key:
        parts_name = atlas_cache["file"]
    else:
        parts_name = f"{stem}_Parts.png" if "key" not in atlas_cache else f"{stem}_{disease}_Parts.png"
        save_png(os.path.join(out, parts_name), layout.render())
        if "key" not in atlas_cache:
            atlas_cache.update(key=layout.key, file=parts_name)
    parts_path = os.path.join(out, parts_name)

    # A clean scene for the export: only the two LOD objects.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    leaf_material = preview_material(f"{stem}Leaf", leaf_path)
    parts_material = preview_material(f"{stem}Parts", parts_path)
    objects = []
    for level, soup in enumerate(soups):
        obj = build_mesh(f"{stem}_{disease}_LOD{level}", soup, [leaf_material, parts_material])
        objects.append(obj)
        mesh = obj.data
        report[f"lod{level}_triangles"] = len(mesh.polygons)
    size = objects[0].dimensions
    report.update(height=round(size.z, 3), width=round(max(size.x, size.y), 3), scale=round(scale, 4),
                  fbx=f"{stem}_{disease}.fbx", leaf_texture=os.path.basename(leaf_path), parts_texture=parts_name,
                  wind_file=wind_name or "",
                  parts_atlas_size=layout.size)

    for obj in bpy.data.objects:
        obj.select_set(obj in objects)
    fbx = os.path.join(out, f"{stem}_{disease}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx + ".tmp.fbx", use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", use_mesh_modifiers=False,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    os.replace(fbx + ".tmp.fbx", fbx)
    if previews:
        os.makedirs(PREVIEWS, exist_ok=True)
        render_preview(os.path.join(PREVIEWS, f"{stem}_{disease}.png"), objects, size.z)
    log(f"{stem}/{disease}: {report['source_triangles']} -> {report['lod0_triangles']} / {report['lod1_triangles']} triangles, "
        f"{report['height']} m tall, {report['width']} m wide, {leaf_total} leaves, "
        f"wind {len(wind_entries)} parts (max {report['wind']['max_angle_deg']} deg, {report['wind']['max_travel_m'] * 1000:.0f} mm), origin {origin}")
    return report


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    only = None
    out_root = OUT_DEFAULT
    previews = True
    i = 0
    while i < len(argv):
        if argv[i] == "--only":
            only = {s.strip().lower() for s in argv[i + 1].split(",")}
            i += 1
        elif argv[i] == "--out":
            out_root = argv[i + 1]
            i += 1
        elif argv[i] == "--no-preview":
            previews = False
        i += 1

    failures = []
    for crop in CROPS:
        if only and crop["id"] not in only:
            continue
        out = os.path.join(out_root, crop["stem"])
        atlas_cache = {}
        variants = []
        for disease, file_name in crop["variants"].items():
            try:
                variants.append(export_variant(crop, disease, file_name, out, atlas_cache, previews))
            except Exception as error:
                import traceback
                traceback.print_exc()
                failures.append(f"{crop['id']}/{disease}: {error}")
        if variants:
            with open(os.path.join(out, f"{crop['stem']}_export.json"), "w") as f:
                json.dump(dict(plantId=crop["id"], stem=crop["stem"], variants=variants), f, indent=2)
    if failures:
        log("FAILED:", *failures)
        sys.exit(1)
    log("DONE")


main()
