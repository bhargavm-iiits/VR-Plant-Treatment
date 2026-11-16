"""Blender: render a front view of one processed lab leaf with its supplied albedo, for the Lab reference card.

Used by Tools/prepare_leaf_references.py for conditions that have no PlantVillage photo. The image is a
render of the supplied 3D specimen, not a field photograph; the Lab labels it that way.
Run: blender -b --factory-startup -P Tools/blender_leaf_reference.py -- <model.fbx> <albedo.png> <out.png>
"""
import sys
import bpy
from mathutils import Vector

model, albedo, out = sys.argv[sys.argv.index('--') + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x = scene.render.resolution_y = 512
scene.world = bpy.data.worlds.new('World')
bpy.ops.wm.fbx_import(filepath=model)
leaf = [o for o in scene.objects if o.type == 'MESH'][0]
material = bpy.data.materials.new('Leaf')
material.use_nodes = True
texture = material.node_tree.nodes.new('ShaderNodeTexImage')
texture.image = bpy.data.images.load(albedo)
shader = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
material.node_tree.links.new(texture.outputs['Color'], shader.inputs['Base Color'])
leaf.data.materials.clear()
leaf.data.materials.append(material)
corners = [leaf.matrix_world @ Vector(c) for c in leaf.bound_box]
lo = Vector([min(c[i] for c in corners) for i in range(3)])
hi = Vector([max(c[i] for c in corners) for i in range(3)])
centre, extent = (lo + hi) / 2, hi - lo
size = max(extent)
depth = min(range(3), key=lambda i: extent[i])   # blender_leaf.py puts the leaf face on the Y axis
camera_data = bpy.data.cameras.new('Camera')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = size * 1.1
camera = bpy.data.objects.new('Camera', camera_data)
scene.collection.objects.link(camera)
scene.camera = camera
offset = Vector((0, 0, 0))
offset[depth] = -size * 3
camera.location = centre + offset
camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Z' if depth != 2 else 'Y').to_euler()
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print('REFERENCE ' + out, flush=True)
