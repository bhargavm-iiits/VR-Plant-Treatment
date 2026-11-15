using System;
using System.Collections.Generic;
using System.Linq;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    public static partial class TreatmentAssetBuilder
    {
        const float SpecimenHeight = 0.7f;

        /// <summary>
        /// One enlarged leaf specimen per case (the Lab leaf model from Leaf_Assets, 0.7 m tall, standing
        /// on its petiole) with LeafAnchors found by analysing its diseased texture: the least green
        /// areas are lesions, the greenest are healthy tissue.
        /// </summary>
        static void BuildLeafSpecimens()
        {
            var leaves = AssetDatabase.LoadAssetAtPath<LeafCatalog>(LeafAssetPipeline.CatalogPath);
            foreach (var info in DiseaseClasses.AllCases)
            {
                if (leaves == null || !leaves.TryGet(info.PlantId, info.DiseaseId, out var prefab))
                    throw new InvalidOperationException($"Leaf model missing for {info.PlantId}/{info.DiseaseId}.");
                var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
                var material = AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/Leaf_{info.PlantId}_{info.DiseaseId}.mat");
                var texture = prefab.GetComponentInChildren<Renderer>().sharedMaterial.GetTexture("_BaseMap");

                var root = Root($"leafspecimen.{info.PlantId}.{info.DiseaseId}");
                var leaf = new GameObject("Leaf", typeof(MeshFilter), typeof(MeshRenderer));
                leaf.transform.SetParent(root.transform, false);
                // Lab-only specimens take the Lab leaf's stem-down correction (Tools/leaf_orientations.json), so a leaf
                // supplied with its petiole sideways stands on its petiole here too. The 24 Farm-crop specimens keep
                // their existing, unrotated build so their saved anchors and positions do not move.
                var rotation = DiseaseClasses.IsLabOnlyPlant(info.PlantId)
                    ? prefab.GetComponentInChildren<MeshFilter>().transform.localRotation
                    : Quaternion.identity;
                var bounds = RotatedBounds(mesh.bounds, rotation);
                var scale = SpecimenHeight / bounds.size.y;
                leaf.transform.localRotation = rotation;
                leaf.transform.localScale = Vector3.one * scale;
                leaf.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                leaf.GetComponent<MeshFilter>().sharedMesh = mesh;
                leaf.GetComponent<MeshRenderer>().sharedMaterial = material;

                var anchors = root.AddComponent<LeafAnchors>();
                Analyse(root.transform, leaf, mesh, texture, anchors, DiseaseClasses.IsLabOnlyPlant(info.PlantId));
                Save(root, "Specimens", $"leafspecimen.{info.PlantId}.{info.DiseaseId}");
            }
        }

        /// <summary>The axis-aligned bounds of <paramref name="local"/> after rotating it about the origin.</summary>
        internal static Bounds RotatedBounds(Bounds local, Quaternion rotation)
        {
            if (rotation == Quaternion.identity)
                return local;
            var result = new Bounds(rotation * local.center, Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                result.Encapsulate(rotation * corner);
            }

            return result;
        }

        struct Sample
        {
            public Vector3 Position;
            public Vector3 Normal;
            public float Greenness;
        }

        static void Analyse(Transform root, GameObject leaf, Mesh mesh, Texture texture, LeafAnchors anchors, bool relaxSpacing = false)
        {
            var pixels = ReadPixels(texture, 256);
            var collider = leaf.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            var scene = collider.GetComponent<Collider>();
            var world = leaf.GetComponent<MeshRenderer>().bounds;

            List<Sample> Cast(bool front)
            {
                var samples = new List<Sample>();
                const int columns = 30, rows = 44;
                var opaque = new bool[columns + 1, rows + 1];
                var found = new Sample[columns + 1, rows + 1];
                for (var i = 0; i <= columns; i++)
                for (var j = 0; j <= rows; j++)
                {
                    var x = Mathf.Lerp(world.min.x, world.max.x, i / (float)columns);
                    var y = Mathf.Lerp(world.min.y, world.max.y, j / (float)rows);
                    var origin = new Vector3(x, y, front ? world.min.z - 0.5f : world.max.z + 0.5f);
                    if (!scene.Raycast(new Ray(origin, front ? Vector3.forward : Vector3.back), out var hit, 2f))
                        continue;
                    var uv = hit.textureCoord;
                    var c = pixels == null ? Color.green : pixels.GetPixelBilinear(uv.x, uv.y);
                    if (c.a < 0.5f)
                        continue;
                    var normal = hit.normal;
                    if (front ? normal.z > 0f : normal.z < 0f)
                        normal = -normal;
                    opaque[i, j] = true;
                    found[i, j] = (new Sample
                    {
                        Position = root.InverseTransformPoint(hit.point),
                        Normal = root.InverseTransformDirection(normal),
                        Greenness = c.g - (c.r + c.b) * 0.5f + (c.r + c.g + c.b < 0.25f ? 1f : 0f), // near-black background is not a lesion
                    });
                }

                // Interior points only: all four neighbours are also leaf, so markers never sit on the margin.
                for (var i = 1; i < columns; i++)
                for (var j = 1; j < rows; j++)
                    if (opaque[i, j] && opaque[i - 1, j] && opaque[i + 1, j] && opaque[i, j - 1] && opaque[i, j + 1])
                        samples.Add(found[i, j]);
                return samples;
            }

            var frontSamples = Cast(true);
            var frontPetiole = frontSamples; // interior points; the lowest is near the petiole
            var backSamples = Cast(false);
            Object.DestroyImmediate(collider);
            if (pixels != null)
                Object.DestroyImmediate(pixels);
            if (frontSamples.Count == 0)
                throw new InvalidOperationException($"Leaf analysis found no surface on {root.name}.");

            var spacing = SpecimenHeight * 0.13f;
            // Keep away from the petiole and the extreme margin, where markers are hard to reach.
            var usable = frontSamples.Where(s => s.Position.y > SpecimenHeight * 0.12f).ToList();
            var lesions = Pick(usable.OrderBy(s => s.Greenness), 4, spacing, null);
            var healthy = Pick(usable.OrderByDescending(s => s.Greenness), 3, spacing, lesions);
            // Narrow blades (rice, wheat, sugarcane) may not fit enough points at the normal spacing; the lessons place
            // three lesion and two healthy targets, so Lab-only specimens relax the spacing until each has its own point.
            // The Farm-crop specimens keep their original build.
            for (var relax = 0.75f; relaxSpacing && (lesions.Count < 3 || healthy.Count < 2) && relax > 0.2f; relax -= 0.15f)
            {
                lesions = Pick(usable.OrderBy(s => s.Greenness), 4, spacing * relax, null);
                healthy = Pick(usable.OrderByDescending(s => s.Greenness), 3, spacing * relax, lesions);
            }
            var underside = new List<LeafAnchors.Point>();
            foreach (var lesion in lesions)
            {
                var near = backSamples.OrderBy(s => (new Vector2(s.Position.x, s.Position.y) - new Vector2(lesion.position.x, lesion.position.y)).sqrMagnitude)
                    .FirstOrDefault();
                if (backSamples.Count > 0 && underside.All(u => (u.position - near.Position).magnitude > spacing * 0.5f))
                    underside.Add(new LeafAnchors.Point { position = near.Position, normal = near.Normal });
            }

            var lowest = (frontPetiole.Count > 0 ? frontPetiole : frontSamples).OrderBy(s => s.Position.y + Mathf.Abs(s.Position.x) * 0.3f).First();
            var petiole = new LeafAnchors.Point { position = lowest.Position, normal = lowest.Normal };
            var leafBounds = new Bounds(root.InverseTransformPoint(world.center), world.size);
            anchors.Set(lesions, healthy, underside, petiole, leafBounds);
            anchors.SetVeins(ComputeVeins(frontSamples.Select(x => (x.Position, x.Normal)).ToList()));
        }

        /// <summary>
        /// Approximate vein paths on the front surface (for the manual treatment's vein-following particles): the midrib
        /// through the middle of each height band, plus side veins from the midrib angled up towards the margin. Stored as
        /// consecutive start/end pairs in the specimen's local space.
        /// </summary>
        internal static List<LeafAnchors.Point> ComputeVeins(List<(Vector3 position, Vector3 normal)> surface)
        {
            var result = new List<LeafAnchors.Point>();
            if (surface.Count < 8)
                return result;
            var minY = surface.Min(s => s.position.y);
            var maxY = surface.Max(s => s.position.y);
            const int bands = 12;
            var height = (maxY - minY) / bands;
            var midrib = new List<(Vector3 position, Vector3 normal)>();
            var halfWidths = new List<float>();
            for (var b = 0; b < bands; b++)
            {
                var lo = minY + b * height;
                var band = surface.Where(s => s.position.y >= lo && s.position.y < lo + height).ToList();
                if (band.Count == 0)
                    continue;
                var minX = band.Min(s => s.position.x);
                var maxX = band.Max(s => s.position.x);
                var mid = (minX + maxX) * 0.5f;
                midrib.Add(band.OrderBy(s => Mathf.Abs(s.position.x - mid)).First());
                halfWidths.Add((maxX - minX) * 0.5f);
            }

            LeafAnchors.Point P((Vector3 position, Vector3 normal) s) => new LeafAnchors.Point { position = s.position, normal = s.normal };
            for (var i = 0; i + 1 < midrib.Count; i++)
            {
                result.Add(P(midrib[i]));
                result.Add(P(midrib[i + 1]));
            }

            for (var i = 1; i < midrib.Count - 1; i += 2)
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    var goal = midrib[i].position + new Vector3(side * halfWidths[i] * 0.7f, height * 1.6f, 0f);
                    var end = surface.OrderBy(s => (new Vector2(s.position.x, s.position.y) - new Vector2(goal.x, goal.y)).sqrMagnitude).First();
                    if ((end.position - midrib[i].position).magnitude < height * 0.5f)
                        continue;
                    result.Add(P(midrib[i]));
                    result.Add(P(end));
                }
            }

            return result;
        }

        /// <summary>
        /// Adds vein paths to the existing leaf specimen prefabs without rebuilding anything else (specimens built by
        /// step 2 before vein baking existed). Step 2 also bakes them.
        /// </summary>
        [MenuItem("BTP/Treatment/5 - Bake Leaf Vein Paths (Manual Treatment)", priority = 44)]
        public static void BakeLeafVeins()
        {
            var count = 0;
            foreach (var info in DiseaseClasses.AllCases)
            {
                var path = $"{Prefabs}/Specimens/leafspecimen_{info.PlantId}_{info.DiseaseId}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException($"{path} is missing. Run BTP/Treatment/2 - Build Treatment Assets first.");
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var anchors = root.GetComponent<LeafAnchors>();
                    var filter = root.GetComponentInChildren<MeshFilter>();
                    if (anchors == null || filter == null)
                        throw new InvalidOperationException($"{path} has no LeafAnchors or leaf mesh.");
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    Physics.SyncTransforms();
                    var world = filter.GetComponent<MeshRenderer>().bounds;
                    var surface = new List<(Vector3, Vector3)>();
                    const int columns = 30, rows = 44;
                    for (var i = 1; i < columns; i++)
                    for (var j = 1; j < rows; j++)
                    {
                        var origin = new Vector3(Mathf.Lerp(world.min.x, world.max.x, i / (float)columns), Mathf.Lerp(world.min.y, world.max.y, j / (float)rows),
                            world.min.z - 0.5f);
                        if (!collider.Raycast(new Ray(origin, Vector3.forward), out var hit, 2f))
                            continue;
                        var normal = hit.normal.z > 0f ? -hit.normal : hit.normal;
                        surface.Add((root.transform.InverseTransformPoint(hit.point), root.transform.InverseTransformDirection(normal)));
                    }

                    Object.DestroyImmediate(collider);
                    anchors.SetVeins(ComputeVeins(surface));
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"[BTP] Baked vein paths into {count} leaf specimens.");
        }

        static List<LeafAnchors.Point> Pick(IEnumerable<Sample> ordered, int count, float spacing, List<LeafAnchors.Point> avoid)
        {
            var result = new List<LeafAnchors.Point>();
            foreach (var s in ordered)
            {
                if (result.Any(p => (p.position - s.Position).magnitude < spacing))
                    continue;
                if (avoid != null && avoid.Any(p => (p.position - s.Position).magnitude < spacing))
                    continue;
                result.Add(new LeafAnchors.Point { position = s.Position, normal = s.Normal });
                if (result.Count == count)
                    break;
            }

            return result;
        }

        /// <summary>Reads a texture through a render target (no Read/Write import flag needed).</summary>
        static Texture2D ReadPixels(Texture texture, int size)
        {
            if (texture == null)
                return null;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        /// <summary>Leaf-surface targets: lesion or tissue area, petiole, sample tag and the cut mark left behind.</summary>
        static void BuildLeafTargets()
        {
            var tissue = Mat("CutTissue", new Color(0.45f, 0.33f, 0.18f), 0.1f);
            var cyan = Glow("CyanSoft", new Color(0.13f, 0.83f, 0.93f, 0.6f), 1.2f);
            var pale = Mat("CutPale", new Color(0.85f, 0.8f, 0.62f), 0.1f);
            var rim = Mat("CutRim", new Color(0.28f, 0.2f, 0.1f), 0.1f);
            var tag = Mat("SampleTag", new Color(0.98f, 0.82f, 0.12f), 0.2f);

            Make("Targets", "target.spot", t =>
            {
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.06f, 0.06f, 0.025f));
                Part(PrimitiveType.Cylinder, "Piece", Group("State_removed", t), Vector3.zero, new Vector3(0.05f, 0.002f, 0.05f), tissue, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cylinder, "Mist", Group("State_covered", t), new Vector3(0f, 0f, -0.002f), new Vector3(0.07f, 0.001f, 0.07f), Mist, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Sphere, "Check", Group("State_observed", t), new Vector3(0.035f, 0.035f, -0.004f), Vector3.one * 0.01f, cyan);
                Part(PrimitiveType.Sphere, "Released", Group("State_released", t), new Vector3(0f, -0.04f, -0.004f), Vector3.one * 0.015f, Cardboard);
            });
            Make("Targets", "target.petiole", t =>
            {
                var capsule = t.gameObject.AddComponent<CapsuleCollider>();
                capsule.radius = 0.02f;
                capsule.height = 0.09f;
                capsule.center = new Vector3(0f, -0.03f, 0f);
            });
            Make("Targets", "target.leaf_proxy", t =>
            {
                Part(PrimitiveType.Cube, "Tag", t, Vector3.zero, new Vector3(0.12f, 0.05f, 0.006f), tag);
                Label(t, "Infected sample", new Vector3(0f, 0f, -0.005f), 120f, 13f, Color.black);
                AddBox(t.gameObject, Vector3.zero, new Vector3(0.14f, 0.07f, 0.03f));
            });
            Make("UI", "ui.cut_mark", t =>
            {
                Part(PrimitiveType.Cylinder, "Rim", t, new Vector3(0f, 0f, -0.0015f), new Vector3(0.05f, 0.0005f, 0.05f), rim, new Vector3(90f, 0f, 0f));
                Part(PrimitiveType.Cylinder, "Cut", t, new Vector3(0f, 0f, -0.002f), new Vector3(0.042f, 0.0005f, 0.042f), pale, new Vector3(90f, 0f, 0f));
            });
        }
    }
}