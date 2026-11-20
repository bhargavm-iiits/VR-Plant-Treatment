using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// Runtime building blocks of the guided treatment's props (no Shader.Find: every material is a tinted copy of one the
    /// lab already uses, so the shaders are always in the build). Copies are tracked and destroyed with their owner.
    /// </summary>
    public sealed class GuidedProps
    {
        readonly Material litSource;
        readonly Material glowSource;
        readonly Material glassSource;
        readonly List<Object> owned = new List<Object>();
        readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public GuidedProps(Material lit, Material glow, Material glass)
        {
            litSource = lit;
            glowSource = glow;
            glassSource = glass;
        }

        /// <summary>An opaque lit colour (the source's texture removed).</summary>
        public Material Lit(string key, Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            if (cache.TryGetValue("lit/" + key, out var cached))
                return cached;
            var material = Copy(litSource, key);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_BumpMap"))
                material.SetTexture("_BumpMap", null);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            cache["lit/" + key] = material;
            return material;
        }

        /// <summary>An unlit glowing colour (the chamber's accent glow shader).</summary>
        public Material Glow(string key, Color color, float intensity = 1.5f)
        {
            if (cache.TryGetValue("glow/" + key, out var cached))
                return cached;
            var material = Copy(glowSource != null ? glowSource : litSource, key);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Intensity"))
                material.SetFloat("_Intensity", intensity);
            cache["glow/" + key] = material;
            return material;
        }

        /// <summary>A transparent tint (the chamber glass without reflections), drawn after the other transparents.</summary>
        public Material Tint(string key, Color color, int queue = 3050)
        {
            if (cache.TryGetValue("tint/" + key, out var cached))
                return cached;
            var material = Copy(glassSource != null ? glassSource : litSource, key);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Off);
            material.renderQueue = queue;
            cache["tint/" + key] = material;
            return material;
        }

        /// <summary>A material copy owned by these props (destroyed with them).</summary>
        public Material Copy(Material source, string key)
        {
            var material = source != null ? new Material(source) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            material.name = (source != null ? source.name : "Guided") + " (" + key + ")";
            owned.Add(material);
            return material;
        }

        public Mesh Own(Mesh mesh)
        {
            owned.Add(mesh);
            return mesh;
        }

        public void Dispose()
        {
            foreach (var item in owned)
                if (item != null)
                    Object.Destroy(item);
            owned.Clear();
            cache.Clear();
        }

        /// <summary>A primitive part without a collider unless asked (local space of <paramref name="parent"/>).</summary>
        public static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
                Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A mesh of small octahedra (granules) at <paramref name="points"/> (local), one draw call.</summary>
        public static void GranuleMesh(Mesh mesh, IReadOnlyList<Vector3> points, float radius)
        {
            var vertices = new List<Vector3>(points.Count * 6);
            var normals = new List<Vector3>(points.Count * 6);
            var triangles = new List<int>(points.Count * 24);
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            foreach (var centre in points)
            {
                var start = vertices.Count;
                foreach (var axis in axes)
                {
                    vertices.Add(centre + axis * radius);
                    normals.Add(axis);
                }

                foreach (var index in faces)
                    triangles.Add(start + index);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>An inside-out UV sphere of radius 1 (seen from inside; the room-dimming shells).</summary>
        public static Mesh InvertedSphere(int longitude = 28, int latitude = 16)
        {
            var mesh = new Mesh { name = "InvertedSphere" };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var y = 0; y <= latitude; y++)
            {
                var v = y / (float)latitude;
                var theta = v * Mathf.PI;
                for (var x = 0; x <= longitude; x++)
                {
                    var phi = x / (float)longitude * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)));
                }
            }

            for (var y = 0; y < latitude; y++)
            for (var x = 0; x < longitude; x++)
            {
                var a = y * (longitude + 1) + x;
                var b = a + longitude + 1;
                // Wound to face inwards.
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(b + 1);
                triangles.Add(b);
            }

            mesh.SetVertices(vertices);
            var normals = new List<Vector3>();
            foreach (var p in vertices)
                normals.Add(-p);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
