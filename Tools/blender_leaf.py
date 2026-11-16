"""Create an upright, front-facing lab leaf without changing the source export."""
import argparse
import json
import os
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

p = argparse.ArgumentParser()
p.add_argument('--source', required=True)
p.add_argument('--output', required=True)
p.add_argument('--name', required=True)
p.add_argument('--triangles', type=int, default=12000)
p.add_argument('--weld', type=float, default=0.0)
a = p.parse_args(sys.argv[sys.argv.index('--') + 1:])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=a.source)
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not objects:
    raise RuntimeError('No leaf mesh: ' + a.source)
for o in objects:
    world = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = world
for o in list(bpy.context.scene.objects):
    if o not in objects:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
if len(objects) > 1:
    bpy.ops.object.join()
leaf = bpy.context.view_layer.objects.active
xyz = np.empty(len(leaf.data.vertices) * 3, dtype=np.float32)
leaf.data.vertices.foreach_get('co', xyz)
xyz = xyz.reshape((-1, 3))
centre = xyz.mean(axis=0)
_, axes = np.linalg.eigh(np.cov((xyz - centre)[::max(1, len(xyz)//100000)].T))
long = axes[:, 2]
if long[2] < 0:
    long = -long
front = axes[:, 0]
if front[1] < 0:
    front = -front
right = np.cross(front, long)
front = np.cross(long, right)
rotation = np.stack((right, front, long))
aligned = (xyz - centre) @ rotation.T
lo, hi = aligned.min(axis=0), aligned.max(axis=0)
# Normalized to 1 m high. Unity's display fits the leaf into a fixed inspection space.
pivot = np.array(((lo[0]+hi[0])/2, (lo[1]+hi[1])/2, lo[2]))
aligned = (aligned - pivot) / max(hi[2] - lo[2], 0.001)
leaf.data.vertices.foreach_set('co', aligned.astype(np.float32).ravel())
leaf.data.update()
if a.weld > 0:
    # UVs are per loop, so merging coincident geometry retains the texture seams.
    mesh = bmesh.new()
    mesh.from_mesh(leaf.data)
    bmesh.ops.remove_doubles(mesh, verts=list(mesh.verts), dist=a.weld)
    bmesh.ops.dissolve_degenerate(mesh, edges=list(mesh.edges), dist=a.weld)
    mesh.to_mesh(leaf.data)
    mesh.free()
    leaf.data.update()
leaf.data.calc_loop_triangles()
before = len(leaf.data.loop_triangles)
if before > a.triangles:
    modifier = leaf.modifiers.new('Display detail', 'DECIMATE')
    modifier.ratio = a.triangles / before
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
leaf.name = leaf.data.name = a.name
for polygon in leaf.data.polygons:
    polygon.use_smooth = True
    polygon.material_index = 0
leaf.data.materials.clear()
os.makedirs(a.output, exist_ok=True)
target = os.path.join(a.output, a.name + '.fbx')
bpy.ops.export_scene.fbx(filepath=target, use_selection=True, object_types={'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True,
    axis_forward='-Z', axis_up='Y', add_leaf_bones=False, bake_anim=False)
leaf.data.calc_loop_triangles()
print('LEAF ' + json.dumps({'name': a.name, 'before': before,
    'after': len(leaf.data.loop_triangles), 'output': target}), flush=True)
