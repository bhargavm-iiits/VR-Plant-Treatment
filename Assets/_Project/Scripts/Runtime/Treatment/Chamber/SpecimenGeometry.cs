using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Geometry helpers for carrying a Farm plant into the treatment chamber: finding the plant's model inside a Farm
    /// slot, measuring its visible LOD0 geometry in any space (also while inactive, when Renderer.bounds is empty),
    /// stripping a clone down to visuals, and giving it its own disease materials.
    /// </summary>
    public static class SpecimenGeometry
    {
        public const string DiseaseAmountProperty = "_DiseaseAmount";
        static readonly int DiseaseAmountId = Shader.PropertyToID(DiseaseAmountProperty);
        /// <summary>At most this many vertices are transformed per renderer (larger meshes are sampled evenly).</summary>
        public const int MaxVerticesPerRenderer = 20000;

        /// <summary>
        /// The plant model of a Farm slot ("Plant_03"): its child with a LODGroup (the farm prefab instance). Stakes,
        /// colliders, canvases, rings and cards are never part of it. Falls back to the slot itself when it has a LODGroup.
        /// </summary>
        public static GameObject ModelOf(Transform farmPlant)
        {
            if (farmPlant == null)
                return null;
            if (farmPlant.GetComponent<LODGroup>() != null)
                return farmPlant.gameObject;
            foreach (Transform child in farmPlant)
                if (child.GetComponent<LODGroup>() != null)
                    return child.gameObject;
            foreach (var lod in farmPlant.GetComponentsInChildren<LODGroup>(true))
                return lod.gameObject;
            return null;
        }

        /// <summary>The renderers that make the visible near model: LOD0 when there is a LODGroup, else every mesh renderer.</summary>
        public static List<Renderer> NearRenderers(GameObject model)
        {
            var result = new List<Renderer>();
            if (model == null)
                return result;
            var group = model.GetComponent<LODGroup>();
            IEnumerable<Renderer> candidates = group != null && group.lodCount > 0 ? group.GetLODs()[0].renderers : model.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in candidates)
            {
                if (renderer == null || renderer is ParticleSystemRenderer || renderer is BillboardRenderer || renderer is TrailRenderer ||
                    renderer is LineRenderer)
                    continue;
                if (!renderer.transform.IsChildOf(model.transform))
                    continue;
                result.Add(renderer);
            }

            return result;
        }

        static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// Calls <paramref name="visit"/> with every measured point of the model's near renderers in
        /// <paramref name="space"/>'s local coordinates: mesh vertices when the mesh is readable (sampled), else the 8
        /// corners of the mesh bounds. Works on inactive objects.
        /// </summary>
        public static int VisitPoints(GameObject model, Transform space, System.Action<Vector3> visit)
        {
            var count = 0;
            var toSpace = space != null ? space.worldToLocalMatrix : Matrix4x4.identity;
            var vertices = new List<Vector3>();
            foreach (var renderer in NearRenderers(model))
            {
                var mesh = MeshOf(renderer);
                if (mesh == null)
                    continue;
                var matrix = toSpace * renderer.transform.localToWorldMatrix;
                if (mesh.isReadable)
                {
                    vertices.Clear();
                    mesh.GetVertices(vertices);
                    var stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Count / (float)MaxVerticesPerRenderer));
                    for (var i = 0; i < vertices.Count; i += stride)
                    {
                        visit(matrix.MultiplyPoint3x4(vertices[i]));
                        count++;
                    }
                }
#if UNITY_EDITOR
                else if (VisitEditorVertices(mesh, matrix, visit, ref count))
                {
                    // The Editor can read meshes that are not CPU-readable (tools and verification measure real vertices).
                }
#endif
                else
                {
                    var b = mesh.bounds;
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var p = new Vector3((corner & 1) == 0 ? b.min.x : b.max.x, (corner & 2) == 0 ? b.min.y : b.max.y, (corner & 4) == 0 ? b.min.z : b.max.z);
                        visit(matrix.MultiplyPoint3x4(p));
                        count++;
                    }
                }
            }

            return count;
        }

#if UNITY_EDITOR
        static bool VisitEditorVertices(Mesh mesh, Matrix4x4 matrix, System.Action<Vector3> visit, ref int count)
        {
            try
            {
                using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
                {
                    if (data.Length == 0)
                        return false;
                    var vertices = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(vertices);
                    var stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Length / (float)MaxVerticesPerRenderer));
                    for (var i = 0; i < vertices.Length; i += stride)
                    {
                        visit(matrix.MultiplyPoint3x4(vertices[i]));
                        count++;
                    }

                    var any = vertices.Length > 0;
                    vertices.Dispose();
                    return any;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }
#endif

        /// <summary>
        /// Adds world-space points ON the surface of the model's near geometry (its leaves, stems and fruit) to
        /// <paramref name="results"/>: the baked samples of <see cref="SpecimenSurfacePoints"/> (the crop meshes are not
        /// CPU-readable in a player), else the mesh's own vertices when readable (sampled to <paramref name="maxPerRenderer"/>).
        /// Unlike <see cref="VisitPoints"/> it never falls back to bounding-box corners, which are not on the plant. Returns
        /// how many points were added.
        /// </summary>
        public static int SurfacePoints(GameObject model, List<Vector3> results, int maxPerRenderer = SpecimenSurfacePoints.MaxPointsPerMesh)
        {
            var added = 0;
            var baked = SpecimenSurfacePoints.Load();
            var vertices = new List<Vector3>();
            foreach (var renderer in NearRenderers(model))
            {
                var mesh = MeshOf(renderer);
                if (mesh == null)
                    continue;
                var matrix = renderer.transform.localToWorldMatrix;
                if (baked != null && baked.TryGet(mesh, out var points))
                {
                    foreach (var point in points)
                        results.Add(matrix.MultiplyPoint3x4(point));
                    added += points.Length;
                    continue;
                }

                if (!mesh.isReadable)
                    continue;
                vertices.Clear();
                mesh.GetVertices(vertices);
                var stride = Mathf.Max(1, Mathf.CeilToInt(vertices.Count / (float)Mathf.Max(1, maxPerRenderer)));
                for (var i = 0; i < vertices.Count; i += stride)
                {
                    results.Add(matrix.MultiplyPoint3x4(vertices[i]));
                    added++;
                }
            }

            return added;
        }

        /// <summary>Bounds of the model's visible near geometry in <paramref name="space"/> (empty bounds at the origin when it has none).</summary>
        public static Bounds Measure(GameObject model, Transform space)
        {
            var first = true;
            var bounds = new Bounds();
            VisitPoints(model, space, p =>
            {
                if (first)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(p);
                }
            });
            return bounds;
        }

        /// <summary>Whether every measured point lies inside <paramref name="box"/> shrunk by <paramref name="clearance"/>; the worst gap is reported.</summary>
        public static bool AllInside(GameObject model, Transform space, Bounds box, float clearance, out float worstGap, out int points)
        {
            var worst = float.MaxValue;
            points = VisitPoints(model, space, p =>
            {
                var gap = Mathf.Min(Mathf.Min(p.x - box.min.x, box.max.x - p.x), Mathf.Min(Mathf.Min(p.y - box.min.y, box.max.y - p.y),
                    Mathf.Min(p.z - box.min.z, box.max.z - p.z)));
                worst = Mathf.Min(worst, gap);
            });
            worstGap = points > 0 ? worst : 0f;
            return points > 0 && worstGap >= clearance - 1e-4f;
        }

        /// <summary>
        /// Gives every renderer of <paramref name="root"/> its own copies of the materials that carry _DiseaseAmount
        /// (keeping their current value). Returns the copies; the caller owns and destroys them.
        /// </summary>
        public static List<Material> CloneMaterials(GameObject root)
        {
            var copies = new Dictionary<Material, Material>();
            var created = new List<Material>();
            if (root == null)
                return created;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var shared = materials[i];
                    if (shared == null || !shared.HasProperty(DiseaseAmountId))
                        continue;
                    if (!copies.TryGetValue(shared, out var copy))
                    {
                        copy = new Material(shared) { name = shared.name + " (chamber)" };
                        copies[shared] = copy;
                        created.Add(copy);
                    }

                    materials[i] = copy;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            return created;
        }

        /// <summary>The first _DiseaseAmount on the model's materials (1 = diseased, the default).</summary>
        public static float DiseaseAmount(GameObject root)
        {
            if (root == null)
                return 1f;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.HasProperty(DiseaseAmountId))
                    return material.GetFloat(DiseaseAmountId);
            return 1f;
        }

        public static void SetDiseaseAmount(IEnumerable<Material> materials, float amount)
        {
            foreach (var material in materials)
                if (material != null && material.HasProperty(DiseaseAmountId))
                    material.SetFloat(DiseaseAmountId, Mathf.Clamp01(amount));
        }

        /// <summary>
        /// Removes everything but visuals from a clone: colliders, rigidbodies, behaviours (scripts, interactables, LOD
        /// helpers) and canvas objects (labels, cards). Use on an inactive clone, so nothing in it ever wakes up.
        /// </summary>
        public static void Strip(GameObject clone)
        {
            if (clone == null)
                return;
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
                if (canvas != null && canvas.gameObject != clone)
                    Kill(canvas.gameObject);
            // Behaviours first (some require colliders or rigidbodies), several passes for RequireComponent chains.
            for (var pass = 0; pass < 3; pass++)
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null)
                        Kill(behaviour);
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
                if (collider != null)
                    Kill(collider);
            foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
                if (body != null)
                    Kill(body);
        }

        static void Kill(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
