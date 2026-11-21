"""Joins every leaf, fruit and stem of the Farm plants to the plant at the right anatomical point, and audits it.

Why: the supplied plants are not built like real ones. The apple, peach, grape and cherry trees place leaves and
fruit near the wood, not on it (gaps from millimetres to tens of centimetres); tomato and pepper trusses stop short
of the stem; the VR reductions (the mid-detail mesh that drops small twigs and pedicels, enlarged far leaves) leave
more parts in the air. At arm's length in a headset every gap shows.

What is joined, and where:
  * live wood: trunk, branches, stems, vines and canes connected wood to wood to the plant's base.
  * a leaf joins live wood at its BASE: the tip of its petiole (found in the leaf sprite, Tools/leaf_base.py), or
    for a leaf with its own wind part its tagged base vertex / wind pivot when that lies at the leaf's end (where
    the Blender leaf hinges on its petiole); otherwise the end of the blade nearest the wood.
  * a fruit organ (fruit with its pedicel, truss or bunch stalk) joins live wood at the FREE END of its stalk (the
    stalk point farthest from the fruit). A fruit without a stalk gets one: a tapered tube from the top of the
    fruit to the nearest wood above, of natural length (the fruit is lifted when the wood is further away).
  * a loose stem or twig joins live wood at its nearest (proximal) end.
Repairs move the organ rigidly on the near mesh (LOD0): leaf shapes, disease textures and UVs are untouched. The
mid mesh (LOD1) takes exactly the same move for the same part, so nothing jumps when the detail level switches;
what the mid mesh alone lost (a dropped pedicel or twig) gets a thin connector instead of a move.

Wind (<model>_Wind.bytes, replayed by Shaders/LeafWind.hlsl): a moved part's pivot and anchors move with it, so
the Unity bake still fits exactly. Every join is replayed through the loop (each key and half key, both LODs):
the child point under the child's motion against the parent point under the parent's. Where the format has
per-part travel (version 3, the Farm crops) and a join opens, the child organ's travel is corrected key by key so
the join follows the parent exactly. Versions 1 and 2 (cherry, tomato, pepper) move only leaves, about their base
pivots, on still wood: a leaf whose pivot is its base stays joined by construction.

Usage (numpy, scipy, pillow, Blender 5.2; one model at a time):
    python Tools/attach_plant_parts.py audit  [--only apple,tomato] [--source DIR] [--tag before]
    python Tools/attach_plant_parts.py repair [--only ...]
audit reads the saved FBX (or the same layout under --source, e.g. an extract of the Git originals) and writes
Tools/out/attachment/<tag>/<model>.json. repair rewrites the FBX and wind files in place.
Then rebuild in Unity: BTP/Setup/28 (BTP.Editor.PlantAttachmentRebuild.Run).
"""
import argparse
import glob
import json
import os
import struct
import subprocess
import sys
import tempfile

import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import leaf_base  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "Assets", "_Project", "Models")
TEXTURES = os.path.join(ROOT, "Assets", "_Project", "Textures", "Plants")
REPORTS = os.path.join(ROOT, "Tools", "out", "attachment")
BLENDER = os.environ.get("BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
IO = os.path.join(ROOT, "Tools", "blender_attach_io.py")

TOL = 0.003         # a join is closed when the surfaces are within 3 mm
EMBED = 0.001       # a moved part is set this far into the wood it lands on
SPACING = 0.003     # surface sample spacing
PASSES = 8
WOOD_WORDS = ("bark", "wood", "stem", "cane", "branch", "twig", "trunk", "pedicel", "petiole", "stalk", "vine", "stake")
SOIL_WORDS = ("soil", "ground", "loam", "mulch")
STALK_WORDS = ("pedicel", "stalk", "stem")


def farm_models(source=None):
    """Every plant model the Farm rows use: name, fbx, wind bytes or None, {leaf material: (alpha png, sprite base)}.
    source: a folder laid out like Assets/_Project/Models to read the models and wind files from instead."""
    base = source or MODELS
    models = []
    m = MODELS

    def add(name, rel, leaves, wind=None, sprite_bases=False, parts=None):
        fbx = os.path.join(base, rel)
        if not os.path.exists(fbx):
            return
        alpha = {k: (png, leaf_base.sprite_base(png) if sprite_bases else None) for k, png in leaves.items()}
        w = os.path.join(base, wind) if wind else None
        models.append(dict(name=name, fbx=fbx, alpha=alpha, wind=w if w and os.path.exists(w) else None, parts=parts))

    add("apple", "AppleTree/AppleTree.fbx",
        {f"AppleLeaf_{d}": os.path.join(m, "AppleTree", f"AppleLeaf_{d}.png") for d in ("BlackRot", "Scab", "Rust")}, sprite_bases=True)
    add("peach", "PeachTree/PeachTree.fbx", {"PeachLeaf": os.path.join(m, "PeachTree", "PeachLeaf.png")}, sprite_bases=True)
    add("grape", "GrapeVine/GrapeVine.fbx",
        {f"GrapeLeaf_{d}": os.path.join(m, "GrapeVine", f"GrapeLeaf_{d}.png") for d in ("BlackRot", "Esca", "LeafBlight")}, sprite_bases=True)
    add("cherry", "CherryTree/CherryTree.fbx", {"CherryLeaf": os.path.join(TEXTURES, "Cherry_BaseAlpha.png")}, "CherryTree/CherryTree_Wind.bytes")
    add("bell_pepper", "BellPepperPlant/BellPepper.fbx", {"BellPepperLeaf": os.path.join(m, "BellPepperPlant", "BellPepperLeaf.png")},
        "BellPepperPlant/BellPepper_Wind.bytes")
    for fbx in sorted(glob.glob(os.path.join(m, "TomatoPlant", "Tomato_*.fbx"))):
        d = os.path.basename(fbx)[len("Tomato_"):-4]
        add(f"tomato/{d}", f"TomatoPlant/Tomato_{d}.fbx", {f"TomatoLeaf_{d}": os.path.join(m, "TomatoPlant", f"TomatoLeaf_{d}.png")},
            f"TomatoPlant/Tomato_{d}_Wind.bytes")
    for export in sorted(glob.glob(os.path.join(m, "FarmCrops", "*", "*_export.json"))):
        folder = os.path.dirname(export)
        crop = os.path.basename(folder)
        with open(export) as f:
            data = json.load(f)
        for v in data["variants"]:
            add(f"{data['plantId']}/{v['disease']}", f"FarmCrops/{crop}/{v['fbx']}", {f"{data['stem']}Leaf": os.path.join(folder, v["leaf_texture"])},
                f"FarmCrops/{crop}/{v['wind_file']}" if v.get("wind_file") else None,
                parts=dict(material=f"{data['stem']}Parts", textured=v["atlas"], solid=list(v["solid"].keys())))
    return models


PART_WOOD = ("cane", "pedicel", "shoot", "stem", "twig", "bark", "flush", "culm", "node", "sheath", "petiole", "axillary",
             "root", "stalk", "cuticle")


def part_names(layout, uv):
    """Source material of each triangle of a Farm crop's parts atlas (Tools/blender_farm_crops.py Layout), from
    its UV centroid: textured materials in 512 px cells, solid colours as 64 px swatches in the next cell."""
    textured, solid = layout["textured"], layout["solid"]
    cells = len(textured) + (1 if solid else 0)
    grid = 1 if cells <= 1 else 2 if cells <= 4 else 4
    px = uv.mean(1) * grid * 512
    cx, cy = np.clip((px[:, 0] // 512).astype(int), 0, grid - 1), np.clip((px[:, 1] // 512).astype(int), 0, grid - 1)
    cell = cx + cy * grid
    sx = np.clip(((px[:, 0] - cx * 512) // 64).astype(int), 0, 7)
    sy = np.clip(((px[:, 1] - cy * 512) // 64).astype(int), 0, 7)
    names = []
    for c, a, b in zip(cell, sx, sy):
        if c < len(textured):
            names.append(textured[c])
        else:
            i = a + b * 8
            names.append(solid[i] if i < len(solid) else "")
    return names


def classify(name, area, extent):
    low = name.lower()
    if "leaf" in low or "leaves" in low or "foliage" in low:
        return "leaf"
    if any(w in low for w in SOIL_WORDS):
        return "soil"
    if "parts" in low:      # a Farm crop's mixed atlas: a closed compact body is fruit, a thin part is wood
        return "fruit" if area > 0.3 * extent * extent and extent > 0.008 else "wood"
    if any(w in low for w in WOOD_WORDS):
        return "wood"
    return "fruit"          # fruit, berries, skins, flowers: they hang from wood


def point_triangle_distance(p, a, b, c):
    """Distances from points p (n,3) to triangles a, b, c (n,3 each), and the closest points (Ericson)."""
    ab, ac = b - a, c - a
    ap, bp, cp = p - a, p - b, p - c
    d1, d2 = (ab * ap).sum(1), (ac * ap).sum(1)
    d3, d4 = (ab * bp).sum(1), (ac * bp).sum(1)
    d5, d6 = (ab * cp).sum(1), (ac * cp).sum(1)
    va, vb, vc = d3 * d6 - d5 * d4, d5 * d2 - d1 * d6, d1 * d4 - d3 * d2

    def safe(x):
        return np.where(np.abs(x) < 1e-30, 1e-30, x)

    den = safe(va + vb + vc)
    q = a + ab * (vb / den)[:, None] + ac * (vc / den)[:, None]
    m = (va <= 0) & ((d4 - d3) >= 0) & ((d5 - d6) >= 0)
    t = (d4 - d3) / safe((d4 - d3) + (d5 - d6)); q[m] = (b + (c - b) * t[:, None])[m]
    m = (vb <= 0) & (d2 >= 0) & (d6 <= 0)
    t = d2 / safe(d2 - d6); q[m] = (a + ac * t[:, None])[m]
    m = (vc <= 0) & (d1 >= 0) & (d3 <= 0)
    t = d1 / safe(d1 - d3); q[m] = (a + ab * t[:, None])[m]
    m = (d6 >= 0) & (d5 <= d6); q[m] = c[m]
    m = (d3 >= 0) & (d4 <= d3); q[m] = b[m]
    m = (d1 <= 0) & (d2 <= 0); q[m] = a[m]
    return np.linalg.norm(p - q, axis=1), q


def tube(start, end, r0, r1, sides, segments):
    """Triangle soup of a tapered tube: corners (T,3,3), normals (T,3,3), fraction along it per corner (T,3)."""
    axis = end - start
    d = axis / max(np.linalg.norm(axis), 1e-9)
    side = np.cross(d, [1.0, 0.0, 0.0])
    if np.linalg.norm(side) < 0.2:
        side = np.cross(d, [0.0, 1.0, 0.0])
    side /= np.linalg.norm(side)
    other = np.cross(d, side)
    rings, normals, ts = [], [], []
    for i in range(segments + 1):
        t = i / segments
        r = r0 + (r1 - r0) * t
        outs = [np.cos(2 * np.pi * k / sides) * side + np.sin(2 * np.pi * k / sides) * other for k in range(sides)]
        rings.append([start + axis * t + o * r for o in outs]); normals.append(outs); ts.append(t)
    P, N, F = [], [], []
    for i in range(segments):
        for k in range(sides):
            k1 = (k + 1) % sides
            for tri in (((i, k), (i, k1), (i + 1, k1)), ((i, k), (i + 1, k1), (i + 1, k))):
                P.append([rings[a][b] for a, b in tri]); N.append([normals[a][b] for a, b in tri]); F.append([ts[a] for a, _ in tri])
    return np.array(P), np.array(N), np.array(F)

class Wind:
    """A *_Wind.bytes file (LeafWindBaker formats 1-3) and its map onto the mesh (Blender world space)."""

    def __init__(self, path):
        raw = open(path, "rb").read()
        if raw[:4] == b"BTPW":
            self.version, n, keys, _, sets = struct.unpack_from("<IIIfI", raw, 4)
            self.header = raw[:24]
            floats = np.frombuffer(raw, "<f4", offset=24).astype(np.float64)
        else:
            self.version, keys, sets, self.header = 1, 48, 0, b""
            floats = np.frombuffer(raw, "<f4").astype(np.float64)
            n = len(floats) // (3 + 48 * 4)
        body = floats.reshape(n, 3 + 3 * sets + keys * (7 if self.version == 3 else 4))
        self.n, self.keys, self.sets = n, keys, sets
        self.pivot = body[:, 0:3].copy()
        self.anchors = [body[:, 3 + 3 * s:6 + 3 * s].copy() for s in range(sets)]
        o = 3 + 3 * sets
        self.quat = body[:, o:o + 4 * keys].reshape(n, keys, 4).copy()
        self.travel = body[:, o + 4 * keys:].reshape(n, keys, 3).copy() if self.version == 3 else None

    def write(self, path):
        parts = [self.pivot] + self.anchors + [self.quat.reshape(self.n, -1)]
        if self.travel is not None:
            parts.append(self.travel.reshape(self.n, -1))
        with open(path, "wb") as f:
            f.write(self.header)
            f.write(np.concatenate(parts, 1).astype("<f4").tobytes())

    def fit(self, tagged_world, has):
        """File space -> world: least squares affine over the LOD0 tagged vertices, as LeafWindBaker fits it."""
        anchors = self.anchors[0] if self.sets else self.pivot
        design = np.concatenate([anchors[has], np.ones((has.sum(), 1))], 1)
        sol, *_ = np.linalg.lstsq(design, tagged_world[has], rcond=None)
        self.A, self.t = sol[:3].T, sol[3]
        self.Ainv = np.linalg.inv(self.A)
        self.Pm = self.A / abs(np.linalg.det(self.A)) ** (1 / 3)
        self.residual = float(np.abs(design @ sol - tagged_world[has]).max())

    def world_pivot(self):
        return self.pivot @ self.A.T + self.t

    def _phase(self, s, samples):
        f = s * self.keys / samples
        k0 = int(np.floor(f))
        return k0, (k0 + 1) % self.keys, f - k0

    def rotations(self, samples):
        out = np.empty((self.n, samples, 3, 3))
        for s in range(samples):
            k0, k1, a = self._phase(s, samples)
            q0, q1 = self.quat[:, k0], self.quat[:, k1]
            q1 = np.where((q0 * q1).sum(1, keepdims=True) < 0, -q1, q1)
            q = (1 - a) * q0 + a * q1
            q /= np.linalg.norm(q, axis=1, keepdims=True)
            w, x, y, z = q.T
            R = np.stack([np.stack([1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)], -1),
                          np.stack([2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)], -1),
                          np.stack([2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)], -1)], 1)
            out[:, s] = self.Pm @ R @ self.Pm.T
        return out

    def travels(self, samples):
        out = np.zeros((self.n, samples, 3))
        if self.travel is not None:
            for s in range(samples):
                k0, k1, a = self._phase(s, samples)
                out[:, s] = ((1 - a) * self.travel[:, k0] + a * self.travel[:, k1]) @ self.A.T
        return out


class Motion:
    """Points through the wind loop: x_s = p + R_s (x - p) + T_s for their part (part -1 is still)."""

    def __init__(self, wind, samples):
        self.p, self.R, self.T, self.samples = wind.world_pivot(), wind.rotations(samples), wind.travels(samples), samples

    def path(self, part, x):
        if part < 0:
            return np.repeat(np.asarray(x)[None], self.samples, 0)
        return self.p[part] + (self.R[part] @ (x - self.p[part])) + self.T[part]


class Mesh:
    """One LOD object in world space (Blender axes, z up), split into pieces (welded connected triangles)."""

    def __init__(self, data, index, alpha, parts=None):
        self.parts_layout = parts
        prefix = f"o{index}_"
        self.prefix, self.name = prefix, str(data["objects"][index])
        self.W = data[prefix + "W"]
        self.P = data[prefix + "P"] @ self.W[:3, :3].T + self.W[:3, 3]
        self.N = data[prefix + "N"] @ np.linalg.inv(self.W[:3, :3])
        T = len(self.P)
        self.UV0 = data[prefix + "UV0"] if prefix + "UV0" in data else np.zeros((T, 3, 2))
        self.UV1 = data[prefix + "UV1"] if prefix + "UV1" in data else None
        self.M = data[prefix + "M"].astype(np.int64)
        self.mats = [str(x) for x in data[prefix + "mats"]]
        self.alpha = {self.mats.index(k): v for k, v in alpha.items() if k in self.mats}
        self.added = []
        self._refresh()

    def _refresh(self):
        T = len(self.P)
        if self.UV1 is not None:
            self.wind = np.rint(self.UV1[:, :, 0]).astype(np.int64) - 1
            self.tag = self.UV1[:, :, 1] >= 0.5
        else:
            self.wind = np.full((T, 3), -1)
            self.tag = np.zeros((T, 3), bool)
        key = np.round(self.P.reshape(-1, 3) / 5e-5).astype(np.int64)
        _, vid = np.unique(key, axis=0, return_inverse=True)
        vid = vid.reshape(T, 3)
        inc = coo_matrix((np.ones(3 * T), (vid.ravel(), np.repeat(np.arange(T), 3))), shape=(vid.max() + 1, T)).tocsr()
        _, self.piece = connected_components(inc.T @ inc, directed=False)
        self.K = self.piece.max() + 1
        c = self.P
        self.area = 0.5 * np.linalg.norm(np.cross(c[:, 1] - c[:, 0], c[:, 2] - c[:, 0]), axis=1)
        lo = np.full((self.K, 3), np.inf); hi = np.full((self.K, 3), -np.inf)
        np.minimum.at(lo, self.piece, c.min(1)); np.maximum.at(hi, self.piece, c.max(1))
        extent = np.linalg.norm(hi - lo, axis=1)
        parea = np.bincount(self.piece, weights=self.area, minlength=self.K)
        pmat = np.zeros(self.K, np.int64); pmat[self.piece] = self.M
        self.cls = np.array([classify(self.mats[pmat[k]] if pmat[k] < len(self.mats) else "", parea[k], extent[k]) for k in range(self.K)])
        layout = self.parts_layout
        if layout and layout["material"] in self.mats:
            sel = np.where(self.M == self.mats.index(layout["material"]))[0]
            names = part_names(layout, self.UV0[sel])
            woody = np.array([any(w in n.lower() for w in PART_WOOD) for n in names], dtype=float)
            votes = np.zeros(self.K); total = np.zeros(self.K)
            np.add.at(votes, self.piece[sel], woody * self.area[sel]); np.add.at(total, self.piece[sel], self.area[sel])
            ks = np.where(total > 0)[0]
            self.cls[ks] = np.where(votes[ks] >= 0.5 * total[ks], "wood", "fruit")
        self.piece_part = np.full(self.K, -1)
        moving = self.wind[:, 0] >= 0
        self.piece_part[self.piece[moving]] = self.wind[moving, 0]
        self.unit = np.arange(self.K)
        first = {}
        for k in range(self.K):
            if self.piece_part[k] >= 0:
                self.unit[k] = first.setdefault(self.piece_part[k], k)
        # sample pattern: by area and along the longest edge (thin twigs are long slivers)
        edges = np.linalg.norm(c - np.roll(c, 1, axis=1), axis=2).max(1)
        count = np.clip(np.maximum(np.ceil(self.area / (0.5 * SPACING * SPACING)), np.ceil(2 * edges / SPACING)).astype(int), 1, 8000)
        rng = np.random.default_rng(7)
        tri = np.repeat(np.arange(T), count)
        r1, r2 = rng.random(len(tri)), rng.random(len(tri))
        flip = r1 + r2 > 1; r1[flip], r2[flip] = 1 - r1[flip], 1 - r2[flip]
        w = np.concatenate([np.stack([1 - r1 - r2, r1, r2], 1), np.tile(np.eye(3), (T, 1))])
        tri = np.concatenate([tri, np.repeat(np.arange(T), 3)])
        vis = np.ones(len(tri), bool)
        for mat, (png, _) in self.alpha.items():
            img = leaf_base.alpha_of(png)
            sel = np.where(self.M[tri] == mat)[0]
            uv = np.einsum("nb,nbd->nd", w[sel], self.UV0[tri[sel]])
            h, wd = img.shape
            x = np.clip((np.mod(uv[:, 0], 1) * wd).astype(int), 0, wd - 1)
            y = np.clip((np.mod(uv[:, 1], 1) * h).astype(int), 0, h - 1)
            vis[sel] = img[y, x] > leaf_base.ALPHA_CUT
        self.stri, self.sw = tri[vis], w[vis]
        self.visible_piece = np.zeros(self.K, bool); self.visible_piece[self.piece[self.stri]] = True

    def samples(self):
        return np.einsum("nb,nbd->nd", self.sw, self.P[self.stri]), self.piece[self.stri], self.stri

    def piece_centres(self):
        S, SP, _ = self.samples()
        sums = np.zeros((self.K, 3)); np.add.at(sums, SP, S)
        return sums / np.maximum(np.bincount(SP, minlength=self.K), 1)[:, None]

    def contacts(self):
        S, SP, _ = self.samples()
        pairs = cKDTree(S).query_pairs(TOL, output_type="ndarray")
        a, b = SP[pairs[:, 0]], SP[pairs[:, 1]]
        keep = a != b
        e = np.unique(np.sort(np.stack([a[keep], b[keep]], 1), 1), axis=0)
        rigid = np.stack([np.arange(self.K), self.unit], 1)
        return np.concatenate([e, rigid[rigid[:, 0] != rigid[:, 1]]])

    def _components(self, contacts, member):
        e = contacts[member[contacts[:, 0]] & member[contacts[:, 1]]]
        e = np.concatenate([e, np.stack([np.arange(self.K)] * 2, 1)])
        _, comp = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(self.K, self.K)), directed=False)
        return comp

    def live_wood(self, contacts):
        """Wood (and soil) pieces connected wood to wood to the plant's base."""
        woody = (self.cls == "wood") | (self.cls == "soil")
        comp = self._components(contacts, woody)
        bottom = self.P[:, :, 2].min()
        lo = np.full(self.K, np.inf); np.minimum.at(lo, self.piece, self.P[:, :, 2].min(1))
        roots = set(comp[woody & (lo < bottom + 0.02) & self.visible_piece])
        if not roots and (woody & self.visible_piece).any():
            # a sprawling plant whose lowest point is a leaf or fruit on the ground: its lowest stem is the base
            k = np.where(woody & self.visible_piece)[0]
            low = lo[k].min()
            roots = set(comp[k[lo[k] < low + 0.02]])
        return np.isin(comp, list(roots)) & woody

    def organs(self, contacts, live):
        """Fruit and loose wood, grouped (fruit-fruit, fruit-wood, wood-wood contacts, rigid wind parts)."""
        member = ((self.cls == "fruit") | ((self.cls == "wood") & ~live)) & self.visible_piece
        comp = self._components(contacts, member)
        groups = {}
        for k in np.where(member)[0]:
            groups.setdefault(comp[k], []).append(k)
        return [dict(pieces=np.array(g), kind="fruit" if (self.cls[g] == "fruit").any() else "wood") for g in groups.values()]

    def wood_query(self, live):
        S, SP, ST = self.samples()
        mask = live[SP] & (self.cls[SP] != "soil")
        self._wq = (cKDTree(S[mask]) if mask.any() else None, ST[mask])

    def nearest_wood(self, points, above=False):
        """Closest point on live wood to each point: (distance, point, triangle)."""
        points = np.asarray(points, dtype=np.float64)
        tree, tris = self._wq
        n = len(points)
        best_d = np.full(n, np.inf); best_q = points.copy(); best_t = np.zeros(n, int)
        if tree is None:
            return best_d, best_q, best_t
        kk = min(24 if above else 12, tree.n)
        _, idx = tree.query(points, k=kk)
        idx = idx.reshape(n, kk)
        for j in range(kk):
            t = tris[idx[:, j]]
            d, q = point_triangle_distance(points, self.P[t, 0], self.P[t, 1], self.P[t, 2])
            if above:
                d = np.where(q[:, 2] > points[:, 2] + 0.003, d, np.inf)
            better = d < best_d
            best_d[better], best_q[better], best_t[better] = d[better], q[better], t[better]
        return best_d, best_q, best_t

    def wind_at(self, tri, point):
        return int(self.wind[tri, int(np.linalg.norm(self.P[tri] - point, axis=1).argmin())])

    def uv_at(self, tri, point):
        uv = self._uv_at(tri, point)
        return uv if np.all(np.isfinite(uv)) else self.UV0[tri, 0]

    def _uv_at(self, tri, point):
        a, b, c = self.P[tri]
        v0, v1, v2 = b - a, c - a, point - a
        d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
        den = d00 * d11 - d01 * d01
        if abs(den) < 1e-20:
            return self.UV0[tri, 0]
        v = (d11 * d20 - d01 * d21) / den; w = (d00 * d21 - d01 * d20) / den
        return (1 - v - w) * self.UV0[tri, 0] + v * self.UV0[tri, 1] + w * self.UV0[tri, 2]
    def leaf_units(self):
        units = {}
        for k in np.where((self.cls == "leaf") & self.visible_piece)[0]:
            units.setdefault(self.unit[k], []).append(k)
        return {u: np.array(v) for u, v in units.items()}

    def sprite_bases(self):
        """{piece: world point} of the petiole tip, for leaves drawn from a sprite with a petiole."""
        out = {}
        for mat, (_, base) in self.alpha.items():
            if base is None or base[2] != "petiole":
                continue
            u, v, _ = base
            tris = np.where(self.M == mat)[0]
            uv = self.UV0[tris]
            a, b, c = uv[:, 0], uv[:, 1], uv[:, 2]
            d = (b[:, 1] - c[:, 1]) * (a[:, 0] - c[:, 0]) + (c[:, 0] - b[:, 0]) * (a[:, 1] - c[:, 1])
            ok = np.abs(d) > 1e-14
            d = np.where(ok, d, 1.0)
            w0 = ((b[:, 1] - c[:, 1]) * (u - c[:, 0]) + (c[:, 0] - b[:, 0]) * (v - c[:, 1])) / d
            w1 = ((c[:, 1] - a[:, 1]) * (u - c[:, 0]) + (a[:, 0] - c[:, 0]) * (v - c[:, 1])) / d
            w2 = 1 - w0 - w1
            inside = ok & (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
            for t, x0, x1, x2 in zip(tris[inside], w0[inside], w1[inside], w2[inside]):
                out.setdefault(self.piece[t], x0 * self.P[t, 0] + x1 * self.P[t, 1] + x2 * self.P[t, 2])
        return out

    def leaf_bases(self, units, pivots=None, tags=None):
        """{unit: (base point, how)}: tagged base vertex, wind pivot at the leaf's end, sprite petiole tip, or the
        end of the blade nearest the wood."""
        S, SP, _ = self.samples()
        order = np.argsort(SP, kind="stable")
        starts = np.searchsorted(SP[order], np.arange(self.K + 1))
        sprite = self.sprite_bases()
        out = {}
        for u, pieces in units.items():
            pts = np.concatenate([S[order[starts[k]:starts[k + 1]]] for k in pieces])
            if len(pts) < 3:
                continue
            centre = pts.mean(0)
            _, vec = np.linalg.eigh(np.cov((pts - centre).T))
            axis = vec[:, 2]
            along = (pts - centre) @ axis
            length = max(float(np.ptp(along)), 1e-3)
            part = self.piece_part[pieces[0]]
            if tags and part in tags:
                # formats 1 and 2 hinge the leaf exactly at its tagged base vertex: that is where it must join
                out[u] = (tags[part], "tagged base"); continue
            if pivots is not None and part >= 0:
                b = pivots[part]
                near = np.linalg.norm(pts - b, axis=1).min()
                # on the leaf (where its petiole inserts, also mid-blade for a palmate leaf), or at its end
                # (a whorl or spray of several leaves hinges where they meet: its pivot, among them)
                if near < 0.05 * length or (near < 0.2 * length and abs((b - centre) @ axis) > 0.3 * length)                         or (len(pieces) > 1 and near < 0.25 * length):
                    out[u] = (b, "wind pivot"); continue
            sp = [sprite[k] for k in pieces if k in sprite]
            if sp:
                out[u] = (sp[0], "petiole tip"); continue
            ends = np.array([pts[np.argmax(along)], pts[np.argmin(along)]])
            d, _, _ = self.nearest_wood(ends)
            out[u] = (ends[int(np.argmin(d))], "blade end")
        return out

    def rigid(self, pieces):
        """The pieces plus every piece of the same wind parts: one Blender object moves as one rigid body."""
        pieces = np.atleast_1d(np.asarray(pieces))
        parts = self.piece_part[pieces]
        parts = parts[parts >= 0]
        if not len(parts):
            return pieces
        return np.union1d(pieces, np.where(np.isin(self.piece_part, parts))[0])

    def translate(self, pieces, offset, rigid=True):
        self.P[np.isin(self.piece, self.rigid(pieces) if rigid else pieces)] += offset

    def add_tube(self, start, end, r0, r1, sides, segments, material, uv, part_start, part_end):
        P, N, F = tube(np.asarray(start, float), np.asarray(end, float), r0, r1, sides, segments)
        T = len(P)
        UV1 = np.zeros((T, 3, 2))
        UV1[:, :, 0] = np.where(F < 0.5, part_start + 1, part_end + 1)     # each half follows its end in the wind
        self.added.append(dict(P=P, N=N, UV0=np.broadcast_to(np.asarray(uv, float), (T, 3, 2)).copy(), UV1=UV1, M=np.full(T, material)))

    def fold(self):
        """Generated triangles become part of the mesh (later passes see them as wood)."""
        if not self.added:
            return
        for a in self.added:
            self.P = np.concatenate([self.P, a["P"]]); self.N = np.concatenate([self.N, a["N"]])
            self.UV0 = np.concatenate([self.UV0, a["UV0"]]); self.M = np.concatenate([self.M, a["M"]])
            if self.UV1 is not None:
                self.UV1 = np.concatenate([self.UV1, a["UV1"]])
        self.added = []
        self._refresh()


def tag_positions(mesh, parts):
    sums = np.zeros((parts, 3)); counts = np.zeros(parts)
    tri, corner = np.where(mesh.tag)
    idx = mesh.wind[tri, corner]
    ok = (idx >= 0) & (idx < parts)
    np.add.at(sums, idx[ok], mesh.P[tri[ok], corner[ok]])
    np.add.at(counts, idx[ok], 1)
    return sums / np.maximum(counts, 1)[:, None], counts > 0


# ============================================================================ joins of one LOD

def joins_of(mesh, pivots=None, tags=None):
    """Every organ's join: list of dict(kind, gap, child point, child part, parent point, parent part, pieces),
    with live wood and the leaf base method counts."""
    con = mesh.contacts()
    live = mesh.live_wood(con)
    mesh.wood_query(live)
    S, SP, ST = mesh.samples()
    out = []
    for org in mesh.organs(con, live):
        mask = np.isin(SP, org["pieces"])
        pts, pcs, ptri = S[mask], SP[mask], ST[mask]
        if org["kind"] == "fruit":
            fruit = pts[mesh.cls[pcs] == "fruit"]
            woody = np.where(mesh.cls[pcs] == "wood")[0]
            if len(woody):
                # the stalk end that leads to the plant: of the stalk points in the far half from the fruit (a pedicel
                # tip, a truss or panicle base, a bunch's peduncle), the one nearest live wood
                far = np.linalg.norm(pts[woody] - fruit.mean(0), axis=1)
                ends = woody[far >= 0.5 * far.max()]
                d_e, _, _ = mesh.nearest_wood(pts[ends])
                i = ends[int(np.argmin(d_e))]
                d, q, t = mesh.nearest_wood(pts[i:i + 1])
                how = "stalk end"
            else:
                d_all, q_all, t_all = mesh.nearest_wood(pts)
                if d_all.min() <= TOL:      # a berry resting on its rachis
                    i = int(np.argmin(d_all)); how = "rests on wood"
                else:
                    fi = np.where(mesh.cls[pcs] == "fruit")[0]
                    i = fi[np.argmax(pts[fi, 2])]; how = "no stalk"
                d, q, t = mesh.nearest_wood(pts[i:i + 1])
        else:
            d_all, q_all, t_all = mesh.nearest_wood(pts)
            i = int(np.argmin(d_all)); how = "proximal end"
            d, q, t = d_all[i:i + 1], q_all[i:i + 1], t_all[i:i + 1]
        out.append(dict(kind=org["kind"], how=how, gap=float(d[0]), c=pts[i], cpart=int(mesh.wind_at(ptri[i], pts[i])),
                        q=q[0], qpart=int(mesh.wind_at(int(t[0]), q[0])) if np.isfinite(d[0]) else -1, qtri=int(t[0]), pieces=org["pieces"],
                        size=float(np.linalg.norm(pts.max(0) - pts.min(0))), top=pts[np.argmax(pts[:, 2])]))
    units = mesh.leaf_units()
    for u, (b, how) in mesh.leaf_bases(units, pivots, tags).items():
        d, q, t = mesh.nearest_wood(b[None])
        part = int(mesh.piece_part[units[u][0]])
        out.append(dict(kind="leaf", how=how, gap=float(d[0]), c=b, cpart=part, q=q[0],
                        qpart=int(mesh.wind_at(int(t[0]), q[0])) if np.isfinite(d[0]) else -1, qtri=int(t[0]), pieces=units[u]))
    return out, live


def summarise(mesh, joins, motion=None):
    res = dict(triangles=int(len(mesh.P)))
    for kind, label in (("leaf", "leaves"), ("fruit", "fruit"), ("wood", "loose_wood")):
        js = [j for j in joins if j["kind"] == kind]
        g = np.array([j["gap"] for j in js])
        bad = g[g > TOL] if len(g) else g
        hows = {}
        for j in js:
            hows[j["how"]] = hows.get(j["how"], 0) + 1
        res[label] = dict(total=len(js), joined=int((g <= TOL).sum()) if len(g) else 0, gaps=int(len(bad)),
                          median_gap_mm=round(float(np.median(bad)) * 1000, 1) if len(bad) else 0.0,
                          max_gap_mm=round(float(np.max(bad)) * 1000, 1) if len(bad) else 0.0, joined_at=hows)
    if motion is not None:
        worst, opened, checked = 0.0, 0, 0
        for j in joins:
            if j["gap"] > TOL or (j["cpart"] < 0 and j["qpart"] < 0) or j["cpart"] == j["qpart"]:
                continue
            checked += 1
            g = np.linalg.norm(motion.path(j["cpart"], j["c"]) - motion.path(j["qpart"], j["q"]), axis=1).max()
            worst = max(worst, g); opened += g > TOL
        res["wind"] = dict(moving_joins_checked=checked, open_in_cycle_over_3mm=int(opened), worst_gap_in_cycle_mm=round(worst * 1000, 1))
    return res

# ============================================================================ repair of one model

def repair(model, data, log):
    names = [str(n) for n in data["objects"]]
    near_i = next(i for i, n in enumerate(names) if "LOD0" in n)
    near = Mesh(data, near_i, model["alpha"], model.get("parts"))
    wind = Wind(model["wind"]) if model["wind"] else None
    tags_v12 = wind is not None and wind.version in (1, 2)
    if wind is not None:
        tw, has = tag_positions(near, wind.n)
        wind.fit(tw, has)
    T0 = len(near.P)
    orig_piece, orig_cls, orig_centres = near.piece.copy(), near.cls.copy(), near.piece_centres()
    tri_move = np.zeros((T0, 3))
    part_move = {}
    stalks, wood_joins = [], []
    stats = dict(moved_wood=0, moved_fruit=0, new_stalks=0, moved_leaves=0, pinned_in_wind=0, mid_moved_by_near=0,
                 mid_connectors=0, mid_moved_leaves=0, mid_stalks=0)
    # a stalk material actually used on this mesh (an unused slot has no colour to take)
    stalk_mat = next((i for i, m in enumerate(near.mats) if any(w in m.lower() for w in STALK_WORDS) and "leaf" not in m.lower()
                      and (near.M == i).any()), None)

    def move(pieces, off):
        tris = np.where(np.isin(near.piece, near.rigid(pieces)))[0]
        near.P[tris] += off
        orig = tris[tris < T0]
        tri_move[orig] += off
        for p in set(near.wind[orig, 0].tolist()) - {-1}:
            part_move[p] = part_move.get(p, 0) + off

    def carried(joins, pieces):
        """The organ's pieces plus every leaf whose base rests on it (a leaf goes with its petiole or truss)."""
        S, SP, _ = near.samples()
        mine = S[np.isin(SP, pieces)]
        leaves = [j for j in joins if j["kind"] == "leaf"]
        if not len(mine) or not leaves:
            return pieces
        d, _ = cKDTree(mine).query(np.array([j["c"] for j in leaves]))
        extra = [j["pieces"] for j, dd in zip(leaves, d) if dd <= TOL]
        return np.unique(np.concatenate([pieces] + extra)) if extra else pieces

    for _ in range(PASSES):
        piv0 = wind.world_pivot() + np.array([part_move.get(i, np.zeros(3)) for i in range(wind.n)]) if wind is not None else None
        tg0 = None
        if tags_v12:
            tw0, has0 = tag_positions(near, wind.n)
            tg0 = {i: tw0[i] for i in range(wind.n) if has0[i]}
        joins, _ = joins_of(near, piv0, tg0)
        todo = [j for j in joins if j["kind"] != "leaf" and j["gap"] > TOL and np.isfinite(j["gap"])]
        if not todo:
            break
        for j in todo:
            if j["kind"] == "wood" or j["how"] == "stalk end":
                off = j["q"] - j["c"]
                off = off + off / np.linalg.norm(off) * EMBED
                move(carried(joins, j["pieces"]), off)
                stats["moved_wood" if j["kind"] == "wood" else "moved_fruit"] += 1
                if j["kind"] == "wood":
                    wood_joins.append(dict(j, c=j["c"] + off))
                continue
            # a fruit without a stalk: a new stalk up to the wood above, the fruit lifted if that is too far
            top = j["top"]
            d, q, t = near.nearest_wood(top[None], above=True)
            if not np.isfinite(d[0]):
                d, q, t = near.nearest_wood(top[None])
            natural = float(np.clip(0.45 * j["size"], 0.012, 0.05))
            g = float(np.linalg.norm(q[0] - top))
            off = (q[0] - top) * (1 - natural / g) if g > natural else np.zeros(3)
            if g > natural:
                move(carried(joins, j["pieces"]), off); stats["moved_fruit"] += 1
            start = top + off - np.array([0.0, 0.0, 0.004])
            mat = stalk_mat if stalk_mat is not None else int(near.M[t[0]])
            uv = near.UV0[near.M == stalk_mat].reshape(-1, 2).mean(0) if stalk_mat is not None else near.uv_at(int(t[0]), q[0])
            radius = float(np.clip(0.05 * j["size"], 0.0012, 0.004))
            parts = (j["cpart"], near.wind_at(int(t[0]), q[0]))
            near.add_tube(start, q[0], radius * 1.2, radius, 5, 3, mat, uv, *parts)
            stalks.append(dict(start=start, end=q[0], radius=radius, mat=mat, uv=uv, parts=parts))
            stats["new_stalks"] += 1
        near.fold()

    # leaves, at their base
    def wind_state():
        if wind is None:
            return None, None
        piv = wind.world_pivot() + np.array([part_move.get(i, np.zeros(3)) for i in range(wind.n)])
        tg = None
        if tags_v12:
            tw, has = tag_positions(near, wind.n)
            tg = {i: tw[i] for i in range(wind.n) if has[i]}
        return piv, tg

    for _ in range(3):      # a leaf can land on wood that another leaf's move then shifts: settle in rounds
        piv, tg = wind_state()
        joins, _ = joins_of(near, piv, tg)
        todo = [j for j in joins if j["kind"] == "leaf" and j["gap"] > TOL and np.isfinite(j["gap"])]
        for j in todo:
            off = j["q"] - j["c"]
            move(j["pieces"], off + off / np.linalg.norm(off) * (EMBED + 0.001))
            stats["moved_leaves"] += 1
        if not todo:
            break

    if wind is not None:
        for p, off in part_move.items():
            f = wind.Ainv @ off
            wind.pivot[p] += f
            if wind.sets >= 1:
                wind.anchors[0][p] += f
        if wind.version == 3:
            # every join through the loop: correct the child's travel where it opens
            motion = Motion(wind, wind.keys)
            piv, tg = wind_state()
            joins, _ = joins_of(near, wind.world_pivot(), tg)
            order = wood_joins + [j for j in joins if j["kind"] != "leaf"] + [j for j in joins if j["kind"] == "leaf"]
            for j in order:
                cp, qp = j["cpart"], j["qpart"]
                if cp < 0 or cp == qp or j["gap"] > TOL:
                    continue
                gap = motion.path(cp, j["c"]) - motion.path(qp, j["q"])
                rest = j["c"] - j["q"]
                if np.linalg.norm(gap - rest, axis=1).max() <= TOL * 0.5:
                    continue
                delta = -(gap - rest)
                parts = {int(p) for p in near.piece_part[j["pieces"]] if p >= 0} | {cp}
                for p in parts:
                    wind.travel[p] += delta @ wind.Ainv.T
                    motion.T[p] += delta
                stats["pinned_in_wind"] += 1
    # the near mesh's final leaf bases, per wind part: the mid mesh hinges its leaves at the same joint
    near_bases = {}
    if wind is not None:
        piv, tg = wind_state() if wind.version != 3 else (wind.world_pivot(), None)
        if wind.version in (1, 2):
            tw, has = tag_positions(near, wind.n)
            tg = {i: tw[i] for i in range(wind.n) if has[i]}
        for j in joins_of(near, wind.world_pivot(), tg)[0]:
            if j["kind"] == "leaf" and j["cpart"] >= 0:
                near_bases[j["cpart"]] = j["c"]
    write_back(data, near)

    # mid meshes: the near mesh's moves part for part, then connectors for what they alone lost
    piece_move = np.zeros((orig_piece.max() + 1, 3))
    piece_move[orig_piece] = tri_move
    for mi in [i for i in range(len(names)) if i != near_i]:
        mid = Mesh(data, mi, model["alpha"], model.get("parts"))
        centres = mid.piece_centres()
        for k in range(mid.K):
            p = mid.piece_part[k]
            off = None
            if p >= 0:
                off = part_move.get(p)
            else:
                cand = np.where(orig_cls == mid.cls[k])[0]
                if len(cand):
                    dist = np.linalg.norm(orig_centres[cand] - centres[k], axis=1)
                    i = int(np.argmin(dist))
                    if dist[i] < 0.03 and np.any(piece_move[cand[i]]):
                        off = piece_move[cand[i]]
            if off is not None and np.any(off):
                # piece by piece: every piece of a part gets the part's move once
                mid.translate([k], off, rigid=False); stats["mid_moved_by_near"] += 1
        for s in stalks:
            mid.add_tube(s["start"], s["end"], s["radius"] * 1.2, s["radius"], 3, 1, s["mat"], s["uv"], *s["parts"]); stats["mid_stalks"] += 1
        mid.fold()
        for _ in range(4):
            joins, _ = joins_of(mid)
            todo = [j for j in joins if j["kind"] != "leaf" and j["gap"] > TOL and np.isfinite(j["gap"])]
            for j in todo:
                c = j["c"]
                if j["how"] == "no stalk":
                    c = j["top"] - np.array([0.0, 0.0, 0.004])
                    d, q, t = mid.nearest_wood(j["top"][None], above=True)
                    if not np.isfinite(d[0]):
                        d, q, t = mid.nearest_wood(c[None])
                    q, t = q[0], int(t[0])
                else:
                    q, t = j["q"], j["qtri"]
                r = float(np.clip(0.05 * j["size"], 0.0012, 0.004)) if j["kind"] == "fruit" else float(np.clip(0.01 * j["size"], 0.0015, 0.006))
                mid.add_tube(c, q, r, r, 3, 1, int(mid.M[t]), mid.uv_at(t, q), j["cpart"], mid.wind_at(t, q))
                stats["mid_connectors"] += 1
            if not todo:
                break
            mid.fold()
        piv, _ = wind_state()
        tg1 = None
        if tags_v12:
            tw1, has1 = tag_positions(mid, wind.n)
            tg1 = {i: tw1[i] for i in range(wind.n) if has1[i]}
        for _round in range(3):
          if _round:
              mid.fold()
          if tags_v12:
              tw1, has1 = tag_positions(mid, wind.n)
              tg1 = {i: tw1[i] for i in range(wind.n) if has1[i]}
          joins, _ = joins_of(mid, wind.world_pivot() if wind is not None else None, tg1)
          if _round and not any(j["kind"] == "leaf" and j["gap"] > TOL for j in joins):
              break
          for j in joins:
            if j["kind"] != "leaf":
                continue
            b0 = near_bases.get(j["cpart"])
            if b0 is not None and (np.linalg.norm(j["c"] - b0) > TOL or j["gap"] > TOL):
                # exactly onto the near mesh's joint, so both levels hinge at one point; where this mesh has lost the
                # twig there, a short petiole joins that point to its nearest wood (it stretches with the leaf)
                off = b0 - j["c"]
                mid.translate(j["pieces"], off)
                stats["mid_moved_leaves"] += 1
                d, q, t = mid.nearest_wood(b0[None])
                if d[0] > TOL and np.isfinite(d[0]):
                    t = int(t[0])
                    mid.add_tube(b0, q[0], 0.0012, 0.0015, 3, 1, int(mid.M[t]), mid.uv_at(t, q[0]), j["cpart"], mid.wind_at(t, q[0]))
                    stats["mid_connectors"] += 1
                continue
            elif j["gap"] > TOL and np.isfinite(j["gap"]):
                off = j["q"] - j["c"]
            else:
                continue
            n = np.linalg.norm(off)
            mid.translate(j["pieces"], off + (off / n * EMBED if n > 1e-9 else 0))
            stats["mid_moved_leaves"] += 1
        if wind is not None and wind.sets >= 2:
            tw1, has1 = tag_positions(mid, wind.n)
            wind.anchors[1][has1] = (tw1[has1] - wind.t) @ wind.Ainv.T
        write_back(data, mid)
    return wind, stats


def write_back(data, mesh):
    mesh.fold()
    W = mesh.W
    p = mesh.prefix
    data[p + "P"] = (mesh.P - W[:3, 3]) @ np.linalg.inv(W[:3, :3]).T
    data[p + "N"] = mesh.N @ W[:3, :3]
    data[p + "UV0"] = mesh.UV0
    if mesh.UV1 is not None:
        data[p + "UV1"] = mesh.UV1
    data[p + "M"] = mesh.M.astype(np.int32)


# ============================================================================ audit of one model

def audit_model(model, data):
    names = [str(n) for n in data["objects"]]
    order = sorted(range(len(names)), key=lambda i: (0 if "LOD0" in names[i] else 1, names[i]))
    wind = Wind(model["wind"]) if model["wind"] else None
    out = dict(model=model["name"], lods={})
    motion, fruit_centres = None, []
    for i in order:
        mesh = Mesh(data, i, model["alpha"], model.get("parts"))
        tags = None
        if wind is not None and mesh.UV1 is not None:
            tw, has = tag_positions(mesh, wind.n)
            if motion is None:
                wind.fit(tw, has)
                motion = Motion(wind, 2 * wind.keys)
                out["wind"] = dict(version=wind.version, parts=wind.n, file_fit_residual_mm=round(wind.residual * 1000, 4))
            if wind.version in (1, 2):
                tags = {k: tw[k] for k in range(wind.n) if has[k]}
        joins, _ = joins_of(mesh, wind.world_pivot() if wind is not None else None, tags)
        out["lods"][mesh.name] = summarise(mesh, joins, motion)
        c = mesh.piece_centres()
        fruit_centres.append(c[(mesh.cls == "fruit") & mesh.visible_piece])
    if len(fruit_centres) > 1 and len(fruit_centres[0]) and len(fruit_centres[1]):
        d, _ = cKDTree(fruit_centres[0]).query(fruit_centres[1])
        out["detail_switch"] = dict(mid_fruit_pieces=int(len(d)), shifted_over_10mm=int((d > 0.01).sum()), max_shift_mm=round(float(d.max()) * 1000, 1))
    return out


# ============================================================================ driver

def blender(*args):
    r = subprocess.run([BLENDER, "-b", "--factory-startup", "-P", IO, "--", *args], capture_output=True, text=True)
    if r.returncode != 0 or "ATTACH_IO" not in r.stdout:
        raise RuntimeError(f"Blender {args[0]} failed:\n{r.stdout[-2000:]}\n{r.stderr[-2000:]}")


def load(fbx):
    work = tempfile.mkdtemp(prefix="attach_")
    path = os.path.join(work, "dump.npz")
    blender("dump", fbx, path)
    return dict(np.load(path)), work


def line(name, r):
    s = (f"  {name}: leaves {r['leaves']['gaps']}/{r['leaves']['total']} unjoined (max {r['leaves']['max_gap_mm']} mm) "
         f"fruit {r['fruit']['gaps']}/{r['fruit']['total']} (max {r['fruit']['max_gap_mm']} mm) "
         f"loose wood {r['loose_wood']['gaps']}/{r['loose_wood']['total']} (max {r['loose_wood']['max_gap_mm']} mm)")
    if "wind" in r:
        s += f"  wind: {r['wind']['open_in_cycle_over_3mm']}/{r['wind']['moving_joins_checked']} open (worst {r['wind']['worst_gap_in_cycle_mm']} mm)"
    return s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["audit", "repair"])
    ap.add_argument("--only", default="")
    ap.add_argument("--source", default=None)
    ap.add_argument("--tag", default=None)
    args = ap.parse_args()
    only = {s.strip() for s in args.only.split(",") if s.strip()}
    folder = os.path.join(REPORTS, args.tag or args.mode)
    os.makedirs(folder, exist_ok=True)
    failures = []
    for model in farm_models(args.source):
        if only and model["name"] not in only and model["name"].split("/")[0] not in only:
            continue
        print(f"[attach] {args.mode} {model['name']}", flush=True)
        try:
            data, work = load(model["fbx"])
            if args.mode == "audit":
                report = audit_model(model, data)
                for name, r in report["lods"].items():
                    print(line(name, r), flush=True)
                if "detail_switch" in report:
                    print(f"  detail switch: {report['detail_switch']}", flush=True)
            else:
                wind, stats = repair(model, data, print)
                fixed = os.path.join(work, "fixed.npz")
                np.savez_compressed(fixed, **data)
                blender("build", fixed, model["fbx"])
                if wind is not None:
                    wind.write(model["wind"])
                report = dict(model=model["name"], stats=stats)
                print(f"  {stats}", flush=True)
        except Exception as error:
            import traceback
            traceback.print_exc()
            failures.append(f"{model['name']}: {error}")
            continue
        with open(os.path.join(folder, model["name"].replace("/", "_") + ".json"), "w") as f:
            json.dump(report, f, indent=2, default=float)
    if failures:
        print("[attach] FAILED:", *failures, sep="\n  ")
        sys.exit(1)
    print("[attach] done")


if __name__ == "__main__":
    main()