"""Print mesh statistics for one model file.

Usage: blender -b --factory-startup -P Tools/blender_inspect.py -- <model.fbx|model.glb>
"""
import sys
import bpy


def import_model(path):
    lower = path.lower()
    if lower.endswith((".glb", ".gltf")):
        bpy.ops.import_scene.gltf(filepath=path)
    elif hasattr(bpy.ops.wm, "fbx_import"):
        bpy.ops.wm.fbx_import(filepath=path)
    else:
        bpy.ops.import_scene.fbx(filepath=path)


def main():
    path = sys.argv[sys.argv.index("--") + 1]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    import_model(path)

    depsgraph = bpy.context.evaluated_depsgraph_get()
    total_tris = 0
    min_c = [float("inf")] * 3
    max_c = [float("-inf")] * 3
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            print(f"OBJ {obj.name} type={obj.type}")
            continue
        mesh = obj.evaluated_get(depsgraph).to_mesh()
        mesh.calc_loop_triangles()
        tris = len(mesh.loop_triangles)
        total_tris += tris
        for v in mesh.vertices:
            w = obj.matrix_world @ v.co
            for i in range(3):
                min_c[i] = min(min_c[i], w[i])
                max_c[i] = max(max_c[i], w[i])
        mats = [s.material.name if s.material else "None" for s in obj.material_slots]
        print(f"MESH {obj.name} verts={len(mesh.vertices)} tris={tris} uvs={len(mesh.uv_layers)} mats={mats}")
        obj.evaluated_get(depsgraph).to_mesh_clear()

    for mat in bpy.data.materials:
        images = []
        if mat.use_nodes:
            for node in mat.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    images.append(f"{node.image.name}({node.image.size[0]}x{node.image.size[1]})")
        print(f"MAT {mat.name} blend={getattr(mat, 'surface_render_method', '?')} images={images}")

    size = [max_c[i] - min_c[i] for i in range(3)]
    print(f"TOTAL tris={total_tris} size_xyz=({size[0]:.3f}, {size[1]:.3f}, {size[2]:.3f}) "
          f"min=({min_c[0]:.3f}, {min_c[1]:.3f}, {min_c[2]:.3f})")


main()
