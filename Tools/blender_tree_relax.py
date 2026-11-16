"""Collision relaxation for the exported apple and peach trees (used by blender_apple_tree.py and
blender_peach_tree.py).

A physics-style solver, run once at export, so fruit and leaves do not intersect:
- every fruit (body plus its stem) is a sphere that is pushed away from other fruit and out of the branches;
- every leaf card is pushed out of the branches and out of the fruit, and apart from leaves it overlaps.
Branches are the fixed skeleton. Moves are capped so leaves and fruit stay attached where they grew.
"""
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


def islands(mesh):
    """Connected vertex groups of a mesh (union-find over edges)."""
    parent = list(range(len(mesh.vertices)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in mesh.edges:
        a, b = root(e.vertices[0]), root(e.vertices[1])
        if a != b:
            parent[a] = b
    groups = {}
    for i in range(len(mesh.vertices)):
        groups.setdefault(root(i), []).append(i)
    return list(groups.values())


def _centre_radius(mesh, verts):
    c = Vector()
    for i in verts:
        c += mesh.vertices[i].co
    c /= len(verts)
    r = max((mesh.vertices[i].co - c).length for i in verts)
    return c, r


def _kd(points):
    kd = KDTree(len(points))
    for i, p in enumerate(points):
        kd.insert(p, i)
    kd.balance()
    return kd


def _move(mesh, verts, delta):
    for i in verts:
        mesh.vertices[i].co += delta


def _clusters(mesh, distance):
    """Groups islands whose centres lie within `distance` of each other (e.g. the berries of one bunch)."""
    parts = [(verts, _centre_radius(mesh, verts)[0]) for verts in islands(mesh)]
    parent = list(range(len(parts)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    kd = _kd([c for _, c in parts])
    for i, (_, c) in enumerate(parts):
        for (_, j, _) in kd.find_range(c, distance):
            a, b = root(i), root(j)
            if a != b:
                parent[a] = b
    groups = {}
    for i, (verts, _) in enumerate(parts):
        groups.setdefault(root(i), []).extend(verts)
    result = []
    for verts in groups.values():
        c, r = _centre_radius(mesh, verts)
        result.append([verts, c, r])
    return result


def fruit_bodies(mesh, cluster=None):
    """Groups each fruit's body with its stem: large islands are bodies, small islands join the nearest body.
    With `cluster`, nearby islands form one rigid group instead (a grape bunch)."""
    if cluster:
        return _clusters(mesh, cluster)
    parts = []
    for verts in islands(mesh):
        c, r = _centre_radius(mesh, verts)
        parts.append([verts, c, r])
    if not parts:
        return []
    biggest = max(p[2] for p in parts)
    bodies = [[list(p[0]), p[1], p[2]] for p in parts if p[2] > biggest * 0.45]
    kd = _kd([b[1] for b in bodies])
    for verts, c, r in parts:
        if r > biggest * 0.45:
            continue
        _, index, _ = kd.find(c)
        bodies[index][0].extend(verts)
    return bodies


def relax(bark, fruit, leaves, depsgraph, iterations=30, fruit_margin=0.004, report=None, fruit_cluster=None, leaf_limit=1.2, leaf_zmax=None):
    report = report if report is not None else {}
    report["before"] = check(bark, fruit, leaves, depsgraph, fruit_cluster)
    bvh = BVHTree.FromObject(bark, depsgraph)

    # ---- fruit: spheres of 90% of the body radius (the skin, not the stem tip)
    bodies = fruit_bodies(fruit.data, fruit_cluster) if fruit is not None else []
    centres = [b[1].copy() for b in bodies]
    radii = [b[2] * 0.9 for b in bodies]
    start = [c.copy() for c in centres]
    for _ in range(iterations):
        kd = _kd(centres) if centres else None
        push = [Vector() for _ in centres]
        for i, c in enumerate(centres):
            for (co, j, dist) in kd.find_range(c, radii[i] * 2.2 + fruit_margin):
                if j == i:
                    continue
                overlap = radii[i] + radii[j] + fruit_margin - dist
                if overlap > 0:
                    away = (c - co).normalized() if dist > 1e-6 else Vector((0, 0, 1))
                    push[i] += away * overlap * 0.5
            loc, normal, _, dist = bvh.find_nearest(c)
            if loc is not None and dist < radii[i] + fruit_margin:
                away = (c - loc).normalized() if dist > 1e-6 else normal
                push[i] += away * (radii[i] + fruit_margin - dist)
        for i in range(len(centres)):
            centres[i] += push[i]
    moved_fruit = 0
    for b, c, s in zip(bodies, centres, start):
        delta = c - s
        if delta.length > 1e-5:
            _move(fruit.data, b[0], delta)
            moved_fruit += 1

    # ---- leaves: each card is an island of 4 vertices
    cards = []
    for verts in islands(leaves.data):
        c, r = _centre_radius(leaves.data, verts)
        cards.append([verts, c, r])
    leaf_centres = [c[1].copy() for c in cards]
    leaf_start = [c.copy() for c in leaf_centres]
    # The visible leaf fills roughly the middle of its card: half-size ~ 0.5 * card radius.
    half = [c[2] * 0.5 for c in cards]
    fruit_kd = _kd(centres) if centres else None
    max_radius = max(radii) if radii else 0.0
    for _ in range(iterations):
        kd = _kd(leaf_centres)
        push = [Vector() for _ in leaf_centres]
        for i, c in enumerate(leaf_centres):
            loc, normal, _, dist = bvh.find_nearest(c)
            if loc is not None and dist < half[i] * 0.55:
                away = (c - loc).normalized() if dist > 1e-6 else normal
                push[i] += away * (half[i] * 0.55 - dist)
            if fruit_kd is not None:
                for (co, j, dist) in fruit_kd.find_range(c, max_radius + half[i]):
                    need = radii[j] + half[i] * 0.45
                    if dist < need:
                        away = (c - co).normalized() if dist > 1e-6 else Vector((0, 0, 1))
                        push[i] += away * (need - dist)
            for (co, j, dist) in kd.find_range(c, half[i] * 0.9):
                if j == i:
                    continue
                overlap = half[i] * 0.9 - dist
                away = (c - co).normalized() if dist > 1e-6 else Vector((0, 0, 1))
                push[i] += away * overlap * 0.5
        for i in range(len(leaf_centres)):
            leaf_centres[i] += push[i]
            # Stay attached near where the leaf grew.
            offset = leaf_centres[i] - leaf_start[i]
            limit = half[i] * leaf_limit
            if offset.length > limit:
                leaf_centres[i] = leaf_start[i] + offset.normalized() * limit
            # Keep a flat canopy (pergola): leaves are separated sideways, not lifted above it.
            if leaf_zmax is not None and leaf_centres[i].z > max(leaf_zmax, leaf_start[i].z):
                leaf_centres[i].z = max(leaf_zmax, leaf_start[i].z)
    moved_leaves = 0
    for card, c, s in zip(cards, leaf_centres, leaf_start):
        delta = c - s
        if delta.length > 1e-5:
            _move(leaves.data, card[0], delta)
            moved_leaves += 1

    for obj in (fruit, leaves):
        if obj is not None:
            obj.data.update()

    report.update(fruit_count=len(bodies), fruit_moved=moved_fruit, leaf_count=len(cards), leaves_moved=moved_leaves)
    report.update(check(bark, fruit, leaves, depsgraph, fruit_cluster))
    return report


def check(bark, fruit, leaves, depsgraph, fruit_cluster=None):
    """Counts remaining intersections after relaxation."""
    bvh = BVHTree.FromObject(bark, depsgraph)
    result = {}
    if fruit is not None:
        bodies = fruit_bodies(fruit.data, fruit_cluster)
        centres = [b[1] for b in bodies]
        rs = [b[2] * 0.9 for b in bodies]
        kd = _kd(centres)
        ff = sum(1 for i, c in enumerate(centres) for (co, j, d) in kd.find_range(c, rs[i] * 2.2) if j > i and d < rs[i] + rs[j] - 0.002)
        fb = sum(1 for i, c in enumerate(centres) if bvh.find_nearest(c)[3] < rs[i] - 0.002)
        result.update(fruit_fruit_overlaps=ff, fruit_branch_overlaps=fb)
    cards = [_centre_radius(leaves.data, v) for v in islands(leaves.data)]
    lb = sum(1 for c, r in cards if bvh.find_nearest(c)[3] < r * 0.5 * 0.5)
    kd = _kd([c for c, _ in cards])
    ll = sum(1 for i, (c, r) in enumerate(cards) for (co, j, d) in kd.find_range(c, r * 0.5 * 0.85) if j > i)
    result.update(leaf_branch_overlaps=lb, leaf_leaf_overlaps=ll)
    return result