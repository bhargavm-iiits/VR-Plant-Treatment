using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The grain scoop (the extracted scoop model at 0.25 m: handle along -X, bowl along +X opening up). Dipping its bowl
    /// into an open packet's mouth for a moment fills it (<see cref="FillRequested"/> decides whether that packet is
    /// allowed) and a heap of granules shows in the bowl; tilting the full scoop past <see cref="PourTilt"/> pours the heap
    /// out of its lip over <see cref="PourSeconds"/> (<see cref="Emit"/> hands each burst of granules to the guide, which
    /// lets them fall and settle and judges where they landed). It is a <see cref="TreatmentGrabbable"/>.
    /// </summary>
    public sealed class FertiliserScoop : MonoBehaviour, IGranuleSource
    {
        public const float PourTilt = 70f, PourSeconds = 0.5f, DipSeconds = 0.25f;
        /// <summary>Bowl centre, lip and handle grip in the scoop's local space (model at 0.25 m).</summary>
        public static readonly Vector3 BowlLocal = new Vector3(0.05f, -0.012f, 0f), LipLocal = new Vector3(0.115f, -0.004f, 0f),
            GripLocal = new Vector3(-0.085f, 0f, 0f);

        readonly List<FertiliserPacket> packets = new List<FertiliserPacket>();
        GameObject heap;
        Material heapMaterial;
        GuidedProps props;
        Material granuleMaterial;
        float dip;
        FertiliserPacket dipping;
        float pourLeft;
        int toEmit;
        float emitCarry;
        int granulesPerScoop = 28;
        uint seed = 0x9E3779B9u;
        readonly Dictionary<string, Material> heapMaterials = new Dictionary<string, Material>();
        /// <summary>Instanced heap (with a <see cref="GranuleRenderer"/>): granules on the dome, in heap space.</summary>
        readonly List<Vector3> heapPositions = new List<Vector3>();
        readonly List<Quaternion> heapRotations = new List<Quaternion>();
        readonly List<Vector4> heapColours = new List<Vector4>();
        readonly List<Vector3> heapShape = new List<Vector3>();
        GranuleRenderer granuleRenderer;
        GranuleLook heapLook;
        int heapGranules = 230;
        float heapShown = 1f;
        /// <summary>The dome under the 3D granules: base centre (scoop space), semi-axes x/z and height.</summary>
        static readonly Vector3 DomeCentre = BowlLocal + new Vector3(-0.012f, -0.004f, 0f);
        const float DomeX = 0.046f, DomeZ = 0.028f, DomeHeight = 0.021f;

        /// <summary>3D granules on the full scoop's heap now.</summary>
        public int HeapGranules => Full && heap.activeSelf ? Mathf.RoundToInt(heapPositions.Count * heapShown) : 0;

        public TreatmentGrabbable Grabbable { get; private set; }
        public bool Full { get; private set; }
        public bool Pouring => pourLeft > 0f;
        public string ProductId { get; private set; }
        public Vector3 Bowl => transform.TransformPoint(BowlLocal);
        public Vector3 Lip => transform.TransformPoint(LipLocal);
        /// <summary>How far the bowl's opening is tipped from straight up (degrees).</summary>
        public float Tilt => Vector3.Angle(transform.up, Vector3.up);

        /// <summary>The bowl dipped into a packet: return true to fill from it.</summary>
        public Func<FertiliserPacket, bool> FillRequested;
        /// <summary>A pour started at the lip (world).</summary>
        public event Action<Vector3> PourStarted;
        /// <summary>Granules leave the lip now: (lip, velocity, count).</summary>
        public event Action<Vector3, Vector3, int> Emit;
        public event Action PourEnded;
        public event Action<FertiliserPacket> Filled;

        public static FertiliserScoop Build(Transform parent, GuidedTreatmentConfig config, GuidedProps props, Material granules, float floorY,
            GranuleRenderer granuleDrawer = null)
        {
            var go = new GameObject("GrainScoop");
            go.transform.SetParent(parent, false);
            var scoop = go.AddComponent<FertiliserScoop>();
            scoop.Create(config, props, granules, floorY, granuleDrawer);
            return scoop;
        }

        void Create(GuidedTreatmentConfig config, GuidedProps owner, Material granules, float floorY, GranuleRenderer granuleDrawer)
        {
            props = owner;
            if (granuleDrawer != null && granuleDrawer.Supported && config.granuleMaterial != null)
                granuleRenderer = granuleDrawer;
            heapGranules = config.scoopHeapGranules;
            granuleMaterial = granules;
            granulesPerScoop = Mathf.Max(4, config.granulesPerScoop);
            if (config.scoopModel != null)
            {
                var model = Instantiate(config.scoopModel, transform, false);
                model.name = "Model";
                foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.sharedMaterial = config.scoopMaterial != null ? config.scoopMaterial : props.Lit("ScoopSteel", new Color(0.75f, 0.77f, 0.8f), 0.8f, 1f);
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                foreach (var c in model.GetComponentsInChildren<Collider>(true))
                    Destroy(c);
            }
            else
            {
                var steel = props.Lit("ScoopSteel", new Color(0.75f, 0.77f, 0.8f), 0.8f, 1f);
                GuidedProps.Part(PrimitiveType.Cylinder, "Handle", transform, new Vector3(-0.07f, 0f, 0f), new Vector3(0.022f, 0.055f, 0.022f), steel,
                    Quaternion.Euler(0f, 0f, 90f));
                GuidedProps.Part(PrimitiveType.Sphere, "Bowl", transform, BowlLocal, new Vector3(0.13f, 0.05f, 0.07f), steel);
            }

            heap = new GameObject("Heap", typeof(MeshFilter), typeof(MeshRenderer));
            heap.transform.SetParent(transform, false);
            // The heap scales towards its base as it pours: pivot at the dome's base.
            heap.transform.localPosition = DomeCentre;
            Mesh mesh;
            if (granuleRenderer != null)
            {
                mesh = props.Own(DomeMesh());
                granuleRenderer.Register(this);
            }
            else
            {
                mesh = props.Own(new Mesh { name = "ScoopHeap" });
                var points = new List<Vector3>();
                var random = new System.Random(7);
                for (var i = 0; i < 40; i++)
                {
                    var x = Mathf.Lerp(-0.035f, 0.045f, (float)random.NextDouble());
                    var z = Mathf.Lerp(-0.026f, 0.026f, (float)random.NextDouble());
                    // A rounded heap: highest in the middle of the bowl.
                    var rise = 0.012f * (1f - Mathf.Clamp01(Mathf.Abs(x - 0.005f) / 0.045f)) * (1f - Mathf.Clamp01(Mathf.Abs(z) / 0.03f));
                    points.Add(BowlLocal - DomeCentre + new Vector3(x, -0.004f + rise + (float)random.NextDouble() * 0.004f, z));
                }

                GuidedProps.GranuleMesh(mesh, points, 0.005f);
            }

            heap.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = heap.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            heapMaterial = granules;
            renderer.sharedMaterial = heapMaterial;
            heap.SetActive(false);

            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(-0.005f, -0.01f, 0f);
            box.size = new Vector3(0.25f, 0.04f, 0.075f);
            var grip = new GameObject("Grip").transform;
            grip.SetParent(transform, false);
            // Held like a ladle: the handle in the fist, the bowl pointing forward, its opening up.
            grip.SetLocalPositionAndRotation(GripLocal, Quaternion.LookRotation(Vector3.right, Vector3.up));
            Grabbable = TreatmentGrabbable.Attach(gameObject, "Grain scoop", grip, floorY);
        }

        public void SetPackets(IEnumerable<FertiliserPacket> all)
        {
            packets.Clear();
            packets.AddRange(all);
        }

        /// <summary>Empties the scoop at once (step reset, case change).</summary>
        public void Empty()
        {
            Full = false;
            ProductId = null;
            pourLeft = 0f;
            toEmit = 0;
            heap.SetActive(false);
            heap.transform.localScale = Vector3.one;
        }

        /// <summary>Fills the scoop from <paramref name="packet"/> (verification may call it directly).</summary>
        public void Fill(FertiliserPacket packet)
        {
            if (packet == null)
                return;
            Full = true;
            ProductId = packet.ProductId;
            Material material;
            if (granuleRenderer != null && packet.BedMaterial != null)
            {
                material = packet.BedMaterial;
                if (heapLook != packet.Look)
                {
                    heapLook = packet.Look;
                    PlaceHeap((uint)packet.ProductId.GetHashCode() ^ 0x5C0095u);
                }
            }
            else if (!heapMaterials.TryGetValue(packet.ProductId, out material))
            {
                material = props.Copy(granuleMaterial, "Heap" + packet.ProductId);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", packet.Look != null ? packet.Look.Average : Color.Lerp(new Color(0.93f, 0.92f, 0.88f), packet.Colour, 0.35f));
                heapMaterials[packet.ProductId] = material;
            }

            heapShown = 1f;
            packet.TakeScoop(Bowl);
            heap.GetComponent<MeshRenderer>().sharedMaterial = material;
            heap.transform.localScale = Vector3.one;
            heap.SetActive(true);
            Grabbable?.Haptic(0.2f, 0.05f);
            Filled?.Invoke(packet);
        }

        void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (Pouring)
            {
                PourStep(deltaTime);
                return;
            }

            if (!Full)
            {
                var bowl = Bowl;
                FertiliserPacket inside = null;
                foreach (var packet in packets)
                    if (packet != null && packet.isActiveAndEnabled && packet.MouthContains(bowl))
                        inside = packet;
                if (inside != dipping)
                {
                    dipping = inside;
                    dip = 0f;
                }

                if (dipping != null)
                {
                    dip += deltaTime;
                    if (dip >= DipSeconds)
                    {
                        var allowed = FillRequested == null || FillRequested(dipping);
                        if (allowed)
                            Fill(dipping);
                        // A refused dip waits for the bowl to leave that packet before asking again.
                        dip = allowed ? 0f : float.NegativeInfinity;
                    }
                }

                return;
            }

            if (Tilt >= PourTilt)
                BeginPour();
        }

        /// <summary>Starts pouring the full scoop (tilting does this; verification may call it).</summary>
        public void BeginPour()
        {
            if (!Full || Pouring)
                return;
            pourLeft = PourSeconds;
            toEmit = granulesPerScoop;
            emitCarry = 0f;
            PourStarted?.Invoke(Lip);
        }

        void PourStep(float deltaTime)
        {
            pourLeft -= deltaTime;
            emitCarry += granulesPerScoop * deltaTime / PourSeconds;
            var count = Mathf.Min(toEmit, Mathf.FloorToInt(emitCarry));
            if (pourLeft <= 0f)
                count = toEmit;
            if (count > 0)
            {
                emitCarry -= count;
                toEmit -= count;
                var lip = Lip;
                // Granules slide off the lip in the direction the bowl points, slowly.
                var along = transform.right;
                along.y = Mathf.Min(along.y, 0f);
                var velocity = along.normalized * 0.25f + new Vector3(NextRandom() - 0.5f, 0f, NextRandom() - 0.5f) * 0.08f;
                Emit?.Invoke(lip, velocity, count);
            }

            heapShown = Mathf.Clamp01(toEmit / (float)granulesPerScoop);
            heap.transform.localScale = new Vector3(1f, heapShown, 1f);
            if (toEmit > 0 && pourLeft > 0f)
                return;
            pourLeft = 0f;
            Full = false;
            ProductId = null;
            heap.SetActive(false);
            PourEnded?.Invoke();
        }

        /// <summary>An elliptical dome of granule bed filling the bowl (heap space: base centre at the origin).</summary>
        static Mesh DomeMesh()
        {
            const int rings = 7, segments = 28;
            var mesh = new Mesh { name = "ScoopHeapDome" };
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            vertices.Add(new Vector3(0f, DomeHeight, 0f));
            uvs.Add(Vector2.zero);
            for (var r = 1; r <= rings; r++)
            {
                var rho = r / (float)rings;
                for (var s = 0; s < segments; s++)
                {
                    var a = s / (float)segments * Mathf.PI * 2f;
                    var lump = 1f + 0.06f * Mathf.Sin(a * 5f + r) * rho;
                    var p = new Vector3(Mathf.Cos(a) * DomeX * rho * lump, DomeHeight * Mathf.Pow(Mathf.Max(0f, 1f - rho * rho), 0.7f), Mathf.Sin(a) * DomeZ * rho * lump);
                    vertices.Add(p);
                    // Unrolled over the dome (radius + drop from the top), so the bed is not stretched down its flanks.
                    var flat = new Vector2(p.x, p.z);
                    var arc = flat.magnitude + (DomeHeight - p.y);
                    uvs.Add(flat.normalized * arc / 0.045f);
                }
            }

            for (var s = 0; s < segments; s++)
            {
                triangles.Add(0);
                triangles.Add(1 + (s + 1) % segments);
                triangles.Add(1 + s);
            }

            for (var r = 1; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                var a = 1 + (r - 1) * segments + s;
                var b = 1 + (r - 1) * segments + (s + 1) % segments;
                var c = 1 + r * segments + s;
                var d = 1 + r * segments + (s + 1) % segments;
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            if (mesh.normals[0].y < 0f)
            {
                for (var i = 0; i < triangles.Count; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
            }

            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static float DomeY(float x, float z)
        {
            var rho2 = x * x / (DomeX * DomeX) + z * z / (DomeZ * DomeZ);
            return DomeHeight * Mathf.Pow(Mathf.Max(0f, 1f - rho2), 0.7f);
        }

        /// <summary>3D granules over the dome (heap space), sorted bottom-up so a partly poured heap keeps its lower ones.</summary>
        void PlaceHeap(uint key)
        {
            heapPositions.Clear();
            heapRotations.Clear();
            heapColours.Clear();
            heapShape.Clear();
            if (heapLook == null)
                return;
            var random = new System.Random((int)(key & 0x7FFFFFFF));
            const float cell = 0.004f;
            var grid = new Dictionary<long, List<int>>();
            var placed = new List<(Vector3 p, Quaternion r, Vector4 c, Vector3 s)>();
            for (var n = 0; n < heapGranules; n++)
            {
                heapLook.Sample(GranuleMeshes.Hash(key + (uint)n * 0x9E3779B1u), out var colour, out var variant, out var radius, out var rotation, out var seed);
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    var a = (float)random.NextDouble() * Mathf.PI * 2f;
                    var rho = Mathf.Sqrt((float)random.NextDouble()) * 0.97f;
                    var x = Mathf.Cos(a) * DomeX * rho;
                    var z = Mathf.Sin(a) * DomeZ * rho;
                    var point = new Vector3(x, DomeY(x, z), z);
                    var cx = Mathf.FloorToInt(x / cell);
                    var cz = Mathf.FloorToInt(z / cell);
                    var crowded = false;
                    for (var dx = -1; dx <= 1 && !crowded; dx++)
                    for (var dz = -1; dz <= 1 && !crowded; dz++)
                    {
                        if (!grid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var near))
                            continue;
                        foreach (var other in near)
                        {
                            var minimum = (placed[other].s.x + radius) * 0.85f;
                            if ((placed[other].p - point).sqrMagnitude < minimum * minimum)
                            {
                                crowded = true;
                                break;
                            }
                        }
                    }

                    if (crowded)
                        continue;
                    var k = ((long)cx << 32) ^ (uint)cz;
                    if (!grid.TryGetValue(k, out var list))
                        grid[k] = list = new List<int>();
                    list.Add(placed.Count);
                    placed.Add((point, rotation, colour, new Vector3(radius, variant, seed)));
                    break;
                }
            }

            // Lowest first: pouring removes the top ones.
            placed.Sort((i, j) => i.p.y.CompareTo(j.p.y));
            foreach (var g in placed)
            {
                heapPositions.Add(g.p + Vector3.up * g.s.x * 0.4f);
                heapRotations.Add(g.r);
                heapColours.Add(g.c);
                heapShape.Add(g.s);
            }
        }

        public void CollectGranules(GranuleRenderer sink)
        {
            if (!Full || heap == null || !heap.activeInHierarchy || heapLook == null)
                return;
            var shown = Mathf.RoundToInt(heapPositions.Count * heapShown);
            var matrix = transform.localToWorldMatrix;
            var rotation = transform.rotation;
            var scale = transform.lossyScale.x;
            for (var i = 0; i < shown; i++)
            {
                var shape = heapShape[i];
                // Heap space -> scoop space (the heap's height shrinks as it pours, the granules ride on it).
                var local = DomeCentre + new Vector3(heapPositions[i].x, heapPositions[i].y * heapShown, heapPositions[i].z);
                var position = matrix.MultiplyPoint3x4(local);
                var radius = shape.x * scale;
                sink.Add(position, rotation * heapRotations[i], radius, heapColours[i], shape.y, heapLook.Speckle, shape.z, position.y - radius * 1.1f);
            }
        }

        void OnDestroy() => GranuleRenderer.Active?.Unregister(this);

        float NextRandom()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
