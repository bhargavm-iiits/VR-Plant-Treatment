using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// One open fertiliser sack on the TREATMENT TOOLS bench: the config's sack model (Poly Haven's open woven bag, CC0)
    /// fitted to <see cref="GuidedTreatmentConfig.packetFootprint"/>, with the product's printed label baked into its
    /// material (BTP/Setup/NPK Treatment Assets), its rolled-down mouth open and full of the product's granules: a dense
    /// granule-bed surface (the model's own contents surface, re-textured) under a few hundred real-size 3D granules
    /// drawn by the <see cref="GranuleRenderer"/>. Every scoop taken leaves a little hollow in the top layer where the
    /// bowl dipped (<see cref="TakeScoop"/>). The scoop dips into <see cref="MouthContains"/>. It is a <see cref="TreatmentGrabbable"/>:
    /// picked up, inspected and put back like every tool. Without the generated assets it falls back to a printed band
    /// and label on the bag and a simple granule heap.
    /// </summary>
    public sealed class FertiliserPacket : MonoBehaviour, IGranuleSource
    {
        /// <summary>Fallback box size when no model is configured.</summary>
        public const float BoxHeight = 0.25f, BoxWidth = 0.16f, BoxDepth = 0.096f;
        const float BedTile = 0.045f, BedLift = 0.0012f;

        public string ProductId { get; private set; }
        public string ProductLabel { get; private set; }
        public Color Colour { get; private set; }
        public GranuleLook Look { get; private set; }
        /// <summary>The product's granule-bed material (the scoop's heap uses it too).</summary>
        public Material BedMaterial { get; private set; }
        public TreatmentGrabbable Grabbable { get; private set; }
        /// <summary>Fitted size: height (y), depth (x, front at -X) and width (z), in metres.</summary>
        public float Height { get; private set; } = BoxHeight;
        public float Depth { get; private set; } = BoxDepth;
        public float Width { get; private set; } = BoxWidth;
        /// <summary>Height of the granule surface in the open mouth (local).</summary>
        public float FillLevel => fillCentre.y;
        /// <summary>3D granules on the fill now (scooped hollows excluded).</summary>
        public int HeapGranules => heapPositions.Count - scooped;
        /// <summary>The middle of the granule surface in the open mouth (world).</summary>
        public Vector3 Mouth => transform.TransformPoint(fillCentre);

        Vector3 fillCentre = new Vector3(0f, BoxHeight - 0.012f, 0f);
        float fillRadius = 0.04f;
        float hollowRadius = 0.022f;
        int scooped;
        Transform fill;
        readonly List<Vector3> heapPositions = new List<Vector3>();
        readonly List<Quaternion> heapRotations = new List<Quaternion>();
        readonly List<Vector4> heapColours = new List<Vector4>();
        readonly List<Vector3> heapShape = new List<Vector3>(); // radius, variant, seed
        readonly List<bool> heapTaken = new List<bool>();

        public static FertiliserPacket Build(Transform parent, GuidedTreatmentConfig.Product product, GuidedTreatmentConfig config, GuidedProps props,
            Material granuleMaterial, float floorY, GranuleRenderer granules = null)
        {
            var go = new GameObject("Packet_" + product.id);
            go.transform.SetParent(parent, false);
            var packet = go.AddComponent<FertiliserPacket>();
            packet.Create(product, config, props, granuleMaterial, floorY, granules);
            return packet;
        }

        void Create(GuidedTreatmentConfig.Product product, GuidedTreatmentConfig config, GuidedProps props, Material granuleMaterial, float floorY,
            GranuleRenderer granules)
        {
            ProductId = product.id;
            ProductLabel = product.label;
            Colour = product.bandColor;
            Look = GranuleLook.For(product);
            hollowRadius = config.scoopHollowRadius;
            var printed = product.packetMaterial != null;

            var contents = new List<Vector3>();
            if (config.packetModel != null)
            {
                var model = Instantiate(config.packetModel, transform, false);
                model.name = "Model";
                // Keep the model's own import rotation (Z-up exports carry -90 degrees about X).
                model.transform.localRotation = Quaternion.Euler(0f, config.packetModelYaw, 0f) * config.packetModel.transform.localRotation;
                var material = printed ? product.packetMaterial : config.packetMaterial != null ? config.packetMaterial : props.Lit("PacketPaper", new Color(0.9f, 0.88f, 0.8f));
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = material;
                    r.shadowCastingMode = ShadowCastingMode.On;
                }

                foreach (var c in model.GetComponentsInChildren<Collider>(true))
                    Destroy(c);
                Fit(model.transform, config.packetFootprint);
                if (config.packetContentsUv.z > 0f)
                    ContentsSurface(model, config.packetContentsUv, contents);
            }
            else
            {
                GuidedProps.Part(PrimitiveType.Cube, "Model", transform, new Vector3(0f, BoxHeight * 0.5f, 0f), new Vector3(BoxDepth, BoxHeight, BoxWidth),
                    props.Lit("PacketPaper", new Color(0.9f, 0.88f, 0.8f)));
            }

            if (!printed)
                PrintedBand(product, props);

            var bed = product.bedMaterial != null ? product.bedMaterial : props.Lit("Bed" + product.id, Look.Average * 0.75f, 0.25f);
            BedMaterial = bed;
            if (contents.Count >= 3)
                BuildFill(contents, bed);
            else
                BuildFallbackFill(bed, props);

            if (granules != null && granules.Supported && config.granuleMaterial != null)
            {
                PlaceHeap(contents, config.packetHeapGranules, (uint)product.id.GetHashCode());
                granules.Register(this);
            }
            else
            {
                LegacyHeap(props, granuleMaterial, product);
            }

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, Height * 0.5f, 0f);
            box.size = new Vector3(Depth, Height, Width);
            var grip = new GameObject("Grip").transform;
            grip.SetParent(transform, false);
            // Held by the side like a bag: the top points up, the label towards the player.
            grip.SetLocalPositionAndRotation(new Vector3(0f, Height * 0.55f, 0f), Quaternion.Euler(0f, -90f, 0f));
            Grabbable = TreatmentGrabbable.Attach(gameObject, product.label + " sack", grip, floorY);
        }

        /// <summary>Scales the model uniformly so its larger footprint side is <paramref name="footprint"/>, base at y = 0, centred.</summary>
        void Fit(Transform model, float footprint)
        {
            var bounds = LocalBounds(model);
            var scale = footprint / Mathf.Max(1e-4f, Mathf.Max(bounds.size.x, bounds.size.z));
            model.localScale *= scale;
            bounds = LocalBounds(model);
            model.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bounds = LocalBounds(model);
            Height = bounds.size.y;
            Depth = bounds.size.x;
            Width = bounds.size.z;
            fillCentre = new Vector3(0f, Height * 0.9f, 0f);
            fillRadius = Mathf.Min(Depth, Width) * 0.35f;
        }

        Bounds LocalBounds(Transform model)
        {
            var first = true;
            var bounds = new Bounds();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                var b = mesh.bounds;
                for (var c = 0; c < 8; c++)
                {
                    var p = transform.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z)));
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            }

            return bounds;
        }

        /// <summary>The model's contents surface: triangles whose UVs lie in the contents circle, in packet space (3 points each).</summary>
        void ContentsSurface(GameObject model, Vector4 uvCircle, List<Vector3> triangles)
        {
            var centre = new Vector2(uvCircle.x, uvCircle.y);
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                    continue;
                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                var indices = mesh.triangles;
                if (uvs.Length != vertices.Length)
                    continue;
                for (var i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    // By the triangle's centre: the fill reaches the liner without a ragged gap.
                    if (((uvs[a] + uvs[b] + uvs[c]) / 3f - centre).magnitude > uvCircle.z)
                        continue;
                    triangles.Add(transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[a])));
                    triangles.Add(transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[b])));
                    triangles.Add(transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[c])));
                }
            }

            if (triangles.Count < 3)
                return;
            var sum = Vector3.zero;
            foreach (var p in triangles)
                sum += p;
            fillCentre = sum / triangles.Count;
            fillRadius = 0f;
            foreach (var p in triangles)
                fillRadius = Mathf.Max(fillRadius, new Vector2(p.x - fillCentre.x, p.z - fillCentre.z).magnitude);
        }

        /// <summary>The granule bed: the contents surface, lifted a hair, textured with the product's tiling bed.</summary>
        void BuildFill(List<Vector3> triangles, Material bed)
        {
            var go = new GameObject("Fill", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            fill = go.transform;
            var mesh = new Mesh { name = "SackFill" };
            var vertices = new List<Vector3>(triangles.Count);
            var uvs = new List<Vector2>(triangles.Count);
            var indices = new List<int>(triangles.Count);
            for (var i = 0; i < triangles.Count; i++)
            {
                var p = triangles[i] + Vector3.up * BedLift;
                vertices.Add(p);
                uvs.Add(new Vector2(p.x, p.z) / BedTile);
                indices.Add(i);
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            // Facing up (the source winding may face either way).
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            if (normals.Length > 0 && Average(normals).y < 0f)
            {
                for (var i = 0; i < indices.Count; i += 3)
                    (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateNormals();
            }

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = bed;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            owned = mesh;
        }

        Mesh owned;

        static Vector3 Average(Vector3[] values)
        {
            var sum = Vector3.zero;
            foreach (var v in values)
                sum += v;
            return sum / Mathf.Max(1, values.Length);
        }

        /// <summary>No contents surface in the model: a shallow mound of bed inside the top.</summary>
        void BuildFallbackFill(Material bed, GuidedProps props)
        {
            var go = GuidedProps.Part(PrimitiveType.Sphere, "Fill", transform, fillCentre, new Vector3(Depth * 0.72f, 0.02f, Width * 0.66f), bed);
            fill = go.transform;
        }

        void OnDestroy()
        {
            GranuleRenderer.Active?.Unregister(this);
            if (owned != null)
                Destroy(owned);
        }

        /// <summary>The 3D granules on the fill's surface, spaced so they do not interpenetrate much (packet space).</summary>
        void PlaceHeap(List<Vector3> triangles, int count, uint key)
        {
            heapPositions.Clear();
            heapRotations.Clear();
            heapColours.Clear();
            heapShape.Clear();
            heapTaken.Clear();
            scooped = 0;
            var random = new System.Random((int)(key & 0x7FFFFFFF));
            float Next() => (float)random.NextDouble();
            var areas = new List<float>();
            var total = 0f;
            for (var i = 0; i + 2 < triangles.Count; i += 3)
            {
                total += Vector3.Cross(triangles[i + 1] - triangles[i], triangles[i + 2] - triangles[i]).magnitude * 0.5f;
                areas.Add(total);
            }

            var cell = 0.004f;
            var grid = new Dictionary<long, List<int>>();
            long Key(Vector3 p) => ((long)Mathf.FloorToInt(p.x / cell) << 32) ^ (uint)Mathf.FloorToInt(p.z / cell);
            for (var n = 0; n < count; n++)
            {
                Look.Sample(GranuleMeshes.Hash(key + (uint)n * 0x9E3779B1u), out var colour, out var variant, out var radius, out var rotation, out var seed);
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    Vector3 point;
                    if (areas.Count > 0)
                    {
                        var pick = Next() * total;
                        var t = areas.BinarySearch(pick);
                        if (t < 0)
                            t = ~t;
                        t = Mathf.Clamp(t, 0, areas.Count - 1) * 3;
                        float u = Next(), v = Next();
                        if (u + v > 1f)
                        {
                            u = 1f - u;
                            v = 1f - v;
                        }

                        point = triangles[t] + (triangles[t + 1] - triangles[t]) * u + (triangles[t + 2] - triangles[t]) * v;
                        // Keep clear of the rolled rim.
                        if (new Vector2(point.x - fillCentre.x, point.z - fillCentre.z).magnitude > fillRadius * 0.93f)
                            continue;
                    }
                    else
                    {
                        var a = Next() * Mathf.PI * 2f;
                        var r = Mathf.Sqrt(Next());
                        point = fillCentre + new Vector3(Mathf.Cos(a) * r * Depth * 0.33f, 0.008f * (1f - r * r), Mathf.Sin(a) * r * Width * 0.3f);
                    }

                    var crowded = false;
                    var cx = Mathf.FloorToInt(point.x / cell);
                    var cz = Mathf.FloorToInt(point.z / cell);
                    for (var dx = -1; dx <= 1 && !crowded; dx++)
                    for (var dz = -1; dz <= 1 && !crowded; dz++)
                    {
                        if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var near))
                            continue;
                        foreach (var other in near)
                        {
                            var minimum = (heapShape[other].x + radius) * 0.85f;
                            if ((heapPositions[other] - point).sqrMagnitude < minimum * minimum)
                            {
                                crowded = true;
                                break;
                            }
                        }
                    }

                    if (crowded)
                        continue;
                    var index = heapPositions.Count;
                    heapPositions.Add(point + Vector3.up * (BedLift + radius * 0.4f));
                    heapRotations.Add(rotation);
                    heapColours.Add(colour);
                    heapShape.Add(new Vector3(radius, variant, seed));
                    heapTaken.Add(false);
                    var k = Key(point);
                    if (!grid.TryGetValue(k, out var list))
                        grid[k] = list = new List<int>();
                    list.Add(index);
                    break;
                }
            }
        }

        public void CollectGranules(GranuleRenderer sink)
        {
            if (!isActiveAndEnabled || heapPositions.Count == 0)
                return;
            var matrix = transform.localToWorldMatrix;
            var rotation = transform.rotation;
            var scale = transform.lossyScale.y;
            for (var i = 0; i < heapPositions.Count; i++)
            {
                if (heapTaken[i])
                    continue;
                var shape = heapShape[i];
                var position = matrix.MultiplyPoint3x4(heapPositions[i]);
                var radius = shape.x * scale;
                sink.Add(position, rotation * heapRotations[i], radius, heapColours[i], shape.y, Look.Speckle, shape.z, position.y - radius * 1.1f);
            }
        }

        /// <summary>
        /// A scoop was taken at <paramref name="bowl"/> (world): the top granules where the bowl dipped are gone, leaving
        /// a hollow in the heap (the bed below shows; the granule count never runs out).
        /// </summary>
        public void TakeScoop(Vector3 bowl)
        {
            var p = transform.InverseTransformPoint(bowl);
            var centre = new Vector2(p.x, p.z);
            for (var i = 0; i < heapPositions.Count; i++)
            {
                if (heapTaken[i])
                    continue;
                var d = (new Vector2(heapPositions[i].x, heapPositions[i].z) - centre).magnitude;
                // A soft-edged hollow: always inside 70 % of the radius, thinning out towards its rim.
                var keep = Mathf.InverseLerp(hollowRadius * 0.7f, hollowRadius, d);
                if (d < hollowRadius && GranuleMeshes.Hash01(i, scooped + 7) > keep)
                {
                    heapTaken[i] = true;
                    scooped++;
                }
            }
        }

        /// <summary>Full again (step reset, new case).</summary>
        public void Refill()
        {
            for (var i = 0; i < heapTaken.Count; i++)
                heapTaken[i] = false;
            scooped = 0;
        }

        /// <summary>Whether <paramref name="world"/> is inside the open sack (the scoop's bowl dips in from the top).</summary>
        public bool MouthContains(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            var bottom = Mathf.Min(Height * 0.45f, FillLevel - 0.1f);
            return p.y > bottom && p.y < Height + 0.06f && Mathf.Abs(p.x) < Depth * 0.5f && Mathf.Abs(p.z) < Width * 0.5f;
        }

        /// <summary>Fallback print (no generated sack material): the colour band and the label on the front (-X) face.</summary>
        void PrintedBand(GuidedTreatmentConfig.Product product, GuidedProps props)
        {
            var y = Height * 0.45f;
            var band = GuidedProps.Part(PrimitiveType.Quad, "Band", transform, new Vector3(-Depth * 0.5f - 0.004f, y, 0f), new Vector3(Width * 0.86f, Height * 0.4f, 1f),
                props.Lit("Band" + product.id, product.bandColor, 0.2f), Quaternion.Euler(0f, 90f, 0f));
            band.name = "Band";
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.SetLocalPositionAndRotation(new Vector3(-Depth * 0.5f - 0.006f, y, 0f), Quaternion.Euler(0f, 90f, 0f));
            var label = labelGo.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;
            var split = product.label.IndexOf(' ');
            var big = split > 0 ? product.label.Substring(0, split) : product.label;
            var small = split > 0 ? product.label.Substring(split + 1) : "";
            label.text = $"<b>{big}</b>\n<size=45%>{small}</size>";
            label.fontSize = 20f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(Width * 0.86f, Height * 0.4f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 0.05f;
            label.fontSizeMax = 20f;
        }

        /// <summary>No instanced granules (no granule material or no structured-buffer support): small octahedra on the fill.</summary>
        void LegacyHeap(GuidedProps props, Material granuleMaterial, GuidedTreatmentConfig.Product product)
        {
            var heap = new GameObject("Granules", typeof(MeshFilter), typeof(MeshRenderer));
            heap.transform.SetParent(fill != null ? fill : transform, false);
            var mesh = props.Own(new Mesh { name = "PacketGranules" });
            var points = new List<Vector3>();
            var random = new System.Random(product.id.GetHashCode());
            for (var i = 0; i < 60; i++)
            {
                var a = (float)random.NextDouble() * Mathf.PI * 2f;
                var r = Mathf.Sqrt((float)random.NextDouble());
                points.Add(fillCentre + new Vector3(Mathf.Cos(a) * r * fillRadius * 0.8f, 0.003f, Mathf.Sin(a) * r * fillRadius * 0.8f));
            }

            GuidedProps.GranuleMesh(mesh, points, 0.0025f);
            heap.GetComponent<MeshFilter>().sharedMesh = mesh;
            var heapRenderer = heap.GetComponent<MeshRenderer>();
            heapRenderer.sharedMaterial = GranuleTint(props, granuleMaterial, product);
            heapRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>The product's granule colour (a tinted copy of the chamber's granule material; the legacy path).</summary>
        public static Material GranuleTint(GuidedProps props, Material granuleMaterial, GuidedTreatmentConfig.Product product)
        {
            var material = props.Copy(granuleMaterial, "Granules" + product.id);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", GranuleLook.For(product).Average);
            return material;
        }
    }
}
