using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// The glass treatment chamber, resized to each specimen by <see cref="Apply"/>: plinth, corner posts, top and bottom
    /// rails, glass sides, back and ceiling, a front glass door (with its handle on the right stile, hinged on the left: in
    /// Play mode <see cref="ChamberDoor"/> swings it open and closed for the user) above an open service hatch, a cyan status bar on the top-front rail, a glowing ceiling light panel with a real light, and the soil tray
    /// on its cyan-ringed pedestal: a dark tray whose front is a clear window onto the soil layers and the roots. A bench
    /// chamber also gets the lab bench it stands on. Frame, glass, handle and status-bar sizes never change; only lengths
    /// follow the layout. Chamber space: origin at the interior's floor-plan centre on the hall floor, front towards -Z.
    /// </summary>
    public sealed class TreatmentChamber : MonoBehaviour
    {
        const float TrayWall = 0.018f, TrayLip = 0.014f, RootLayer = 0.022f;

        [Header("Materials")]
        [SerializeField] Material frameMaterial;
        [SerializeField] Material glassMaterial;
        [SerializeField] Material accentMaterial;
        [SerializeField] Material lightPanelMaterial;
        [SerializeField] Material trayMaterial;
        [SerializeField] Material trayWindowMaterial;
        [SerializeField] Material topsoilMaterial;
        [SerializeField] Material subsoilMaterial;
        [SerializeField] Material deepSoilMaterial;
        [SerializeField] Material rootMaterial;
        [SerializeField] Material floorPlateMaterial;
        [SerializeField] Material benchMaterial;
        [SerializeField] Material pedestalMaterial;
        [SerializeField] Material wetMaterial;

        [SerializeField] Material sealMaterial;
        [SerializeField] Material shadowMaterial;
        [SerializeField] Material rootHazeMaterial;

        [SerializeField] Transform parts;
        [SerializeField] Transform specimenAnchor;
        [SerializeField] Light ceilingLight;

        ChamberLayout layout;
        bool hasLayout;
        Mesh rootMesh;
        readonly Dictionary<string, Transform> cache = new Dictionary<string, Transform>();

        public ChamberLayout Layout => layout;
        public bool HasLayout => hasLayout;

        /// <summary>The stem pivot on the soil surface (children placed here stand in the tray).</summary>
        public Transform SpecimenAnchor
        {
            get
            {
                EnsureParts();
                return specimenAnchor;
            }
        }

        /// <summary>The exterior box in chamber space (including the lab bench of a bench chamber).</summary>
        public Bounds ExteriorBounds => new Bounds(new Vector3(0f, layout.Exterior.y * 0.5f, 0f), layout.Exterior);

        /// <summary>World-space soil surface height.</summary>
        public float SoilWorldY => transform.TransformPoint(layout.SpecimenAnchor).y;

        internal void Bind(Material frame, Material glass, Material accent, Material lightPanel, Material tray, Material trayWindow, Material topsoil,
            Material subsoil, Material deepSoil, Material root, Material floorPlate, Material bench, Material pedestal, Material wet)
        {
            frameMaterial = frame; glassMaterial = glass; accentMaterial = accent; lightPanelMaterial = lightPanel; trayMaterial = tray;
            trayWindowMaterial = trayWindow; topsoilMaterial = topsoil; subsoilMaterial = subsoil; deepSoilMaterial = deepSoil; rootMaterial = root;
            floorPlateMaterial = floorPlate; benchMaterial = bench; pedestalMaterial = pedestal; wetMaterial = wet;
        }

        /// <summary>Addendum 4 materials: rubber door seals, the soft contact shadows and the soil haze that embeds the roots.</summary>
        internal void BindDetail(Material seal, Material shadow, Material rootHaze)
        {
            sealMaterial = seal;
            shadowMaterial = shadow;
            rootHazeMaterial = rootHaze;
        }

        Material runtimeGlass;
        ChamberGlassPhysics glassPhysics;

        /// <summary>The glass the panes use (the realistic runtime copy in Play mode).</summary>
        public Material GlassMaterial => glassMaterial;
        /// <summary>The cyan accent glow (status bar, ring light).</summary>
        public Material AccentMaterial => accentMaterial;
        /// <summary>The colliders behind the glass, the soil and the tray (Play mode; null before the first layout).</summary>
        public ChamberGlassPhysics GlassPhysics => glassPhysics;

        void Awake()
        {
            if (!Application.isPlaying || glassMaterial == null)
                return;
            // Quest-friendly realistic glass for this session; the material asset keeps its saved look.
            runtimeGlass = ChamberGlassPhysics.MakeRealisticGlass(glassMaterial);
            if (runtimeGlass != null)
                glassMaterial = runtimeGlass;
        }

        void OnDestroy()
        {
            if (runtimeGlass != null)
                Destroy(runtimeGlass);
            if (rootMesh == null)
                return;
            if (Application.isPlaying)
                Destroy(rootMesh);
            else
                DestroyImmediate(rootMesh);
        }

        // ------------------------------------------------------------------ parts

        /// <summary>Creates the parts once (they are kept in the saved scene); later calls only find them.</summary>
        public void EnsureParts()
        {
            if (parts == null)
            {
                var existing = transform.Find("Parts");
                parts = existing != null ? existing : new GameObject("Parts").transform;
                parts.SetParent(transform, false);
            }

            if (specimenAnchor == null)
            {
                var existing = transform.Find("SpecimenAnchor");
                specimenAnchor = existing != null ? existing : new GameObject("SpecimenAnchor").transform;
                specimenAnchor.SetParent(transform, false);
            }

            if (ceilingLight == null)
            {
                var existing = parts.Find("CeilingLight");
                var go = existing != null ? existing.gameObject : new GameObject("CeilingLight", typeof(Light));
                go.transform.SetParent(parts, false);
                ceilingLight = go.GetComponent<Light>();
                ceilingLight.type = LightType.Spot;
                ceilingLight.color = new Color(1f, 0.97f, 0.9f);
                ceilingLight.shadows = LightShadows.None;
                ceilingLight.spotAngle = 130f;
                ceilingLight.innerSpotAngle = 70f;
            }
        }

        Transform Part(string name, Material material, PrimitiveType type = PrimitiveType.Cube)
        {
            if (cache.TryGetValue(name, out var cached) && cached != null)
                return cached;
            EnsureParts();
            var found = parts.Find(name);
            if (found == null)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying)
                        Destroy(collider);
                    else
                        DestroyImmediate(collider);
                }

                go.transform.SetParent(parts, false);
                found = go.transform;
            }

            var renderer = found.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (material != null)
                    renderer.sharedMaterial = material;
                var transparent = material != null && (material == glassMaterial || material == trayWindowMaterial || material == wetMaterial);
                renderer.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }

            cache[name] = found;
            return found;
        }

        static void Box(Transform part, Vector3 centre, Vector3 size, bool visible = true)
        {
            part.gameObject.SetActive(visible);
            part.localPosition = centre;
            part.localRotation = Quaternion.identity;
            part.localScale = new Vector3(Mathf.Max(size.x, 1e-4f), Mathf.Max(size.y, 1e-4f), Mathf.Max(size.z, 1e-4f));
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Moves and rescales every part for <paramref name="fit"/>. Safe in edit mode (the builder saves a default).</summary>
        public void Apply(ChamberLayout fit)
        {
            EnsureParts();
            layout = fit;
            hasLayout = true;
            const float f = ChamberFit.FrameThickness;
            const float g = ChamberFit.GlassThickness;
            var i = fit.Interior;
            var y0 = fit.FloorHeight;
            var top = y0 + i.y;
            var hx = i.x * 0.5f;
            var hz = i.z * 0.5f;
            var bench = fit.Kind == ChamberKind.Bench;

            // Lab bench (bench chambers only) and the plinth the chamber stands on.
            var benchSize = new Vector3(i.x + 2f * f + 2f * ChamberFit.BenchOverhang, 0.045f, i.z + 2f * f + 2f * ChamberFit.BenchOverhang);
            Box(Part("BenchTop", benchMaterial), new Vector3(0f, ChamberFit.BenchTop - 0.0225f, 0f), benchSize, bench);
            Box(Part("BenchBody", pedestalMaterial), new Vector3(0f, (ChamberFit.BenchTop - 0.045f) * 0.5f, 0.02f),
                new Vector3(benchSize.x - 0.08f, ChamberFit.BenchTop - 0.045f, benchSize.z - 0.12f), bench);
            var plinth = bench ? ChamberFit.BenchPlinth : ChamberFit.FloorPlinth;
            Box(Part("Plinth", frameMaterial), new Vector3(0f, y0 - plinth * 0.5f, 0f), new Vector3(i.x + 2f * f, plinth, i.z + 2f * f));
            Box(Part("FloorPlate", floorPlateMaterial), new Vector3(0f, y0 + 0.004f, 0f), new Vector3(i.x, 0.008f, i.z));

            // Frame: four corner posts and the top and bottom rails (constant section).
            var px = hx + f * 0.5f;
            var pz = hz + f * 0.5f;
            var corner = 0;
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                Box(Part("Post" + corner++, frameMaterial), new Vector3(sx * px, y0 + (i.y + f) * 0.5f, sz * pz), new Vector3(f, i.y + f, f));
            foreach (var (name, y) in new[] { ("Top", top + f * 0.5f), ("Bottom", y0 + f * 0.5f) })
            {
                Box(Part(name + "RailFront", frameMaterial), new Vector3(0f, y, -pz), new Vector3(i.x, f, f));
                Box(Part(name + "RailBack", frameMaterial), new Vector3(0f, y, pz), new Vector3(i.x, f, f));
                Box(Part(name + "RailLeft", frameMaterial), new Vector3(-px, y, 0f), new Vector3(f, f, i.z));
                Box(Part(name + "RailRight", frameMaterial), new Vector3(px, y, 0f), new Vector3(f, f, i.z));
            }

            // Glass walls and ceiling in the frame planes.
            Box(Part("GlassLeft", glassMaterial), new Vector3(-px, y0 + i.y * 0.5f, 0f), new Vector3(g, i.y, i.z));
            Box(Part("GlassRight", glassMaterial), new Vector3(px, y0 + i.y * 0.5f, 0f), new Vector3(g, i.y, i.z));
            Box(Part("GlassBack", glassMaterial), new Vector3(0f, y0 + i.y * 0.5f, pz), new Vector3(i.x, i.y, g));
            Box(Part("GlassTop", glassMaterial), new Vector3(0f, top + f * 0.5f, 0f), new Vector3(i.x, g, i.z));

            // Front: a glass door from the hatch rail to the top rail; below it the service hatch is open.
            var doorBottom = y0 + fit.HatchTop;
            Box(Part("Door", glassMaterial), new Vector3(0f, (doorBottom + top) * 0.5f, -pz), new Vector3(i.x, top - doorBottom, g));
            Box(Part("HatchRail", frameMaterial), new Vector3(0f, doorBottom, -pz), new Vector3(i.x, f * 0.8f, f));
            // Door stile on the hinge-free (right) side carries the handle.
            Box(Part("DoorStile", frameMaterial), new Vector3(hx - 0.02f, (doorBottom + top) * 0.5f, -pz - f * 0.5f), new Vector3(0.03f, top - doorBottom, 0.012f));
            var handleY = Mathf.Clamp(1.15f, doorBottom + ChamberFit.HandleLength * 0.5f + 0.03f, top - ChamberFit.HandleLength * 0.5f - 0.03f);
            if (top - doorBottom < ChamberFit.HandleLength + 0.06f)
                handleY = (doorBottom + top) * 0.5f;
            var handleX = hx - 0.06f;
            var handleZ = -pz - f * 0.5f - 0.04f;
            Box(Part("Handle", frameMaterial, PrimitiveType.Cylinder), new Vector3(handleX, handleY, handleZ), new Vector3(0.022f, ChamberFit.HandleLength * 0.5f, 0.022f));
            Box(Part("HandleMountTop", frameMaterial), new Vector3(handleX, handleY + ChamberFit.HandleLength * 0.42f, handleZ + 0.022f), new Vector3(0.018f, 0.018f, 0.04f));
            Box(Part("HandleMountBottom", frameMaterial), new Vector3(handleX, handleY - ChamberFit.HandleLength * 0.42f, handleZ + 0.022f), new Vector3(0.018f, 0.018f, 0.04f));

            // Cyan status bar centred on the top-front rail (fixed size).
            Box(Part("StatusBar", accentMaterial), new Vector3(0f, top + f * 0.5f, -pz - f * 0.5f - 0.004f), new Vector3(0.26f, 0.022f, 0.008f));

            // Ceiling light panel and its light.
            Box(Part("LightPanel", lightPanelMaterial), new Vector3(0f, top - 0.012f, 0f), new Vector3(i.x * 0.7f, 0.012f, i.z * 0.6f));
            ceilingLight.transform.localPosition = new Vector3(0f, top - 0.04f, 0f);
            ceilingLight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ceilingLight.range = i.y * 1.6f + 0.6f;
            ceilingLight.intensity = Mathf.Lerp(0.9f, 2.2f, Mathf.InverseLerp(0.6f, 4.5f, i.y));

            ApplyTray(fit);
            ApplyHardware(fit, doorBottom, top);
            ApplyFront();
            specimenAnchor.localPosition = fit.SpecimenAnchor;
            specimenAnchor.localRotation = Quaternion.identity;
            // The door parts now stand closed: remember those poses (the swing turns them about the hinge line), fit the
            // colliders to the closed door, then put the door back at its current swing.
            RecordDoor(hx, pz, doorBottom, top, handleX, handleY, handleZ);
            FitGlassPhysics();
            ApplyDoorSwing();
            EnsureDoor();
        }

        /// <summary>Play mode: the solid glass, soil and tray colliders follow the new layout (edit mode keeps no colliders).</summary>
        void FitGlassPhysics()
        {
            if (!Application.isPlaying)
                return;
            if (glassPhysics == null)
            {
                glassPhysics = GetComponent<ChamberGlassPhysics>();
                if (glassPhysics == null)
                    glassPhysics = gameObject.AddComponent<ChamberGlassPhysics>();
                glassPhysics.SetDropSource(glassMaterial);
            }

            glassPhysics.Fit(parts, layout, frontOpen);
        }

        /// <summary>World XZ rectangle inside the four glass walls (their inner faces), for the granules.</summary>
        public void GlassInnerArea(out Vector2 min, out Vector2 max)
        {
            var hx = layout.Interior.x * 0.5f + ChamberFit.FrameThickness * 0.5f - ChamberFit.GlassThickness * 0.5f;
            var hz = layout.Interior.z * 0.5f + ChamberFit.FrameThickness * 0.5f - ChamberFit.GlassThickness * 0.5f;
            var a = transform.TransformPoint(new Vector3(-hx, 0f, -hz));
            var b = transform.TransformPoint(new Vector3(hx, 0f, hz));
            min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z));
            max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        static readonly string[] FrontParts =
            {
                "Door", "DoorStile", "Handle", "HandleMountTop", "HandleMountBottom", "HatchRail", "Post0", "Post1", "TopRailFront", "BottomRailFront", "StatusBar",
                "HingeTop", "HingeBottom", "SealLeft", "SealRight", "SealBottom",
            };
        bool frontOpen;

        /// <summary>True while the front glass door and its hatch rail are retracted (hands-on stations).</summary>
        public bool FrontOpen => frontOpen;

        /// <summary>
        /// Retracts (true) or closes the front glass door: while a hands-on station is active nothing (no glass, no rail)
        /// stands between the player and the plant.
        /// </summary>
        public void SetFrontOpen(bool open)
        {
            frontOpen = open;
            if (hasLayout)
                ApplyFront();
            if (glassPhysics != null)
                glassPhysics.SetFrontOpen(open);
        }

        void ApplyFront()
        {
            foreach (var name in FrontParts)
                Part(name, null).gameObject.SetActive(!frontOpen);
        }

        // ------------------------------------------------------------------ hinged door

        /// <summary>The parts that swing with the door leaf: glass, stile, handle and its mounts, and the leaf's rubber seals.</summary>
        static readonly string[] DoorParts =
            { "Door", "DoorStile", "Handle", "HandleMountTop", "HandleMountBottom", "SealLeft", "SealRight", "SealBottom" };
        readonly Dictionary<string, (Vector3 position, Quaternion rotation)> doorClosed = new Dictionary<string, (Vector3, Quaternion)>();
        Vector3 doorHinge, doorEdge, doorHandle;
        float doorBottomY, doorTopY;
        float doorAngle;
        ChamberDoor door;

        /// <summary>The user-operated hinged door (Play mode; null before the first layout).</summary>
        public ChamberDoor Door => door;

        /// <summary>Current swing of the door leaf in degrees about its left hinge line (0 = closed, positive = outwards).</summary>
        public float DoorAngle => doorAngle;

        /// <summary>World point on the vertical hinge line at the door's mid height (the left hinges' axis, in front of the post).</summary>
        public Vector3 DoorHingeWorld => PartsSpace(doorHinge, true);

        /// <summary>World point of the closed door's free (right, latch) edge on its front face, at mid height.</summary>
        public Vector3 DoorEdgeWorld => PartsSpace(doorEdge, true);

        /// <summary>World centre of the handle where it is when the door is closed.</summary>
        public Vector3 DoorHandleClosedWorld => PartsSpace(doorHandle, true);

        /// <summary>Outward normal of the door front (towards the user, away from the chamber interior), world space.</summary>
        public Vector3 DoorOutwardWorld => PartsSpace(Vector3.back, false);

        /// <summary>World heights of the door leaf's bottom and top edges.</summary>
        public Vector2 DoorHeightRangeWorld => new Vector2(PartsSpace(new Vector3(0f, doorBottomY, 0f), true).y, PartsSpace(new Vector3(0f, doorTopY, 0f), true).y);

        /// <summary>The handle part (it carries the press-to-toggle interactable in Play mode).</summary>
        public Transform DoorHandle => Part("Handle", null);

        Vector3 PartsSpace(Vector3 local, bool point)
        {
            var space = parts != null ? parts : transform;
            return point ? space.TransformPoint(local) : space.TransformDirection(local);
        }

        /// <summary>Remembers the closed poses of the door leaf parts and the hinge line (called by <see cref="Apply"/> after the parts are placed).</summary>
        void RecordDoor(float hx, float pz, float doorBottom, float top, float handleX, float handleY, float handleZ)
        {
            const float f = ChamberFit.FrameThickness;
            var midY = (doorBottom + top) * 0.5f;
            // The hinge axis runs through the HingeTop/HingeBottom knuckles (see ApplyHardware), just in front of the left post.
            doorHinge = new Vector3(-hx + 0.006f, midY, -pz - f * 0.5f - 0.008f);
            doorEdge = new Vector3(hx, midY, -pz - ChamberFit.GlassThickness * 0.5f);
            doorHandle = new Vector3(handleX, handleY, handleZ);
            doorBottomY = doorBottom;
            doorTopY = top;
            doorClosed.Clear();
            foreach (var name in DoorParts)
            {
                var part = Part(name, null);
                doorClosed[name] = (part.localPosition, part.localRotation);
            }
        }

        /// <summary>
        /// Swings the door leaf to <paramref name="degrees"/> about its vertical hinge line (0 closed, positive outwards towards
        /// the user). The parts stay where they are in the hierarchy (name lookups, the saved scene and the collider fit are
        /// untouched): each is turned about the hinge from its recorded closed pose, exactly as a hinge pivot would.
        /// </summary>
        public void SetDoorSwing(float degrees)
        {
            if (Mathf.Approximately(degrees, doorAngle))
                return;
            doorAngle = degrees;
            if (hasLayout)
                ApplyDoorSwing();
        }

        void ApplyDoorSwing()
        {
            if (doorClosed.Count == 0)
                return;
            var swing = Quaternion.Euler(0f, doorAngle, 0f);
            foreach (var name in DoorParts)
            {
                if (!doorClosed.TryGetValue(name, out var pose))
                    continue;
                var part = Part(name, null);
                part.localPosition = doorHinge + swing * (pose.position - doorHinge);
                part.localRotation = swing * pose.rotation;
            }
        }

        /// <summary>Play mode: adds the door's proximity prompt and input (once).</summary>
        void EnsureDoor()
        {
            if (!Application.isPlaying || door != null)
                return;
            door = GetComponent<ChamberDoor>();
            if (door == null)
                door = gameObject.AddComponent<ChamberDoor>();
            door.Bind(this);
        }

        // ------------------------------------------------------------------ hardware (Addendum 4)

        static Mesh bevelX, bevelY, bevelZ;

        /// <summary>
        /// A unit box whose four edges along <paramref name="axis"/> are chamfered (an octagonal prism). Scaled to the constant
        /// frame section across and to the length along the axis, the 4 mm bevel stays the same at every chamber size.
        /// </summary>
        static Mesh BevelBox(int axis)
        {
            const float c = 0.09f; // 4 mm on a 45 mm section
            var outline = new[]
            {
                new Vector2(0.5f - c, -0.5f), new Vector2(0.5f, -0.5f + c), new Vector2(0.5f, 0.5f - c), new Vector2(0.5f - c, 0.5f),
                new Vector2(-0.5f + c, 0.5f), new Vector2(-0.5f, 0.5f - c), new Vector2(-0.5f, -0.5f + c), new Vector2(-0.5f + c, -0.5f),
            };
            Vector3 P(Vector2 uv, float along) =>
                axis == 0 ? new Vector3(along, uv.x, uv.y) : axis == 1 ? new Vector3(uv.x, along, uv.y) : new Vector3(uv.x, uv.y, along);
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                var edge = b - a;
                var n2 = new Vector2(edge.y, -edge.x).normalized;
                var normal = P(n2, 0f).normalized;
                var start = vertices.Count;
                vertices.AddRange(new[] { P(a, -0.5f), P(b, -0.5f), P(b, 0.5f), P(a, 0.5f) });
                for (var k = 0; k < 4; k++)
                    normals.Add(normal);
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }

            foreach (var along in new[] { -0.5f, 0.5f })
            {
                var centre = vertices.Count;
                var normal = P(Vector2.zero, along).normalized;
                vertices.Add(P(Vector2.zero, along));
                normals.Add(normal);
                foreach (var point in outline)
                {
                    vertices.Add(P(point, along));
                    normals.Add(normal);
                }

                for (var i = 0; i < outline.Length; i++)
                {
                    var a = centre + 1 + i;
                    var b = centre + 1 + (i + 1) % outline.Length;
                    triangles.AddRange(along > 0f ? new[] { centre, a, b } : new[] { centre, b, a });
                }
            }

            var mesh = new Mesh { name = "BevelBox" + "XYZ"[axis], hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // Winding is not guaranteed for every face orientation: build both sides of the frame cheaply.
            return mesh;
        }

        static Mesh Bevel(int axis)
        {
            switch (axis)
            {
                case 0: return bevelX != null ? bevelX : bevelX = BevelBox(0);
                case 1: return bevelY != null ? bevelY : bevelY = BevelBox(1);
                default: return bevelZ != null ? bevelZ : bevelZ = BevelBox(2);
            }
        }

        void UseBevel(string name, int axis)
        {
            var filter = Part(name, null).GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = Bevel(axis);
        }

        Transform Flat(string name, Material material, Vector3 centre, Vector2 size)
        {
            var part = Part(name, material, PrimitiveType.Quad);
            part.gameObject.SetActive(material != null);
            part.localPosition = centre;
            part.localRotation = Quaternion.Euler(90f, 0f, 0f);
            part.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return part;
        }

        /// <summary>Bevelled steel posts and rails, door hinges and rubber seals, and the soft contact shadows.</summary>
        void ApplyHardware(ChamberLayout fit, float doorBottom, float top)
        {
            for (var i = 0; i < 4; i++)
                UseBevel("Post" + i, 1);
            foreach (var name in new[] { "Top", "Bottom" })
            {
                UseBevel(name + "RailFront", 0);
                UseBevel(name + "RailBack", 0);
                UseBevel(name + "RailLeft", 2);
                UseBevel(name + "RailRight", 2);
            }

            UseBevel("HatchRail", 0);
            const float f = ChamberFit.FrameThickness;
            var hx = fit.Interior.x * 0.5f;
            var pz = fit.Interior.z * 0.5f + f * 0.5f;
            var doorHeight = top - doorBottom;
            // Hinges on the left (hinge) side, rubber seals along the door edges: constant sizes.
            foreach (var (name, t) in new[] { ("HingeTop", 0.8f), ("HingeBottom", 0.2f) })
            {
                var hinge = Part(name, frameMaterial, PrimitiveType.Cylinder);
                Box(hinge, new Vector3(-hx + 0.006f, doorBottom + doorHeight * t, -pz - f * 0.5f - 0.008f), new Vector3(0.018f, 0.035f, 0.018f));
            }

            var seal = sealMaterial != null ? sealMaterial : frameMaterial;
            Box(Part("SealLeft", seal), new Vector3(-hx + 0.004f, (doorBottom + top) * 0.5f, -pz - 0.004f), new Vector3(0.008f, doorHeight, 0.006f));
            Box(Part("SealRight", seal), new Vector3(hx - 0.004f, (doorBottom + top) * 0.5f, -pz - 0.004f), new Vector3(0.008f, doorHeight, 0.006f));
            Box(Part("SealBottom", seal), new Vector3(0f, doorBottom + 0.004f, -pz - 0.004f), new Vector3(fit.Interior.x, 0.008f, 0.006f));

            // Contact shadows: under the chamber on the hall floor, and under the pedestal or tray on the chamber floor.
            Flat("ShadowChamber", shadowMaterial, new Vector3(0f, 0.003f, 0f), new Vector2(fit.Exterior.x + 0.35f, fit.Exterior.z + 0.35f));
            Flat("ShadowTray", shadowMaterial, new Vector3(fit.PlantPivot.x, fit.FloorHeight + 0.0095f, fit.PlantPivot.y),
                new Vector2(fit.TraySize.x * 1.35f, fit.TraySize.y * 1.45f));
            ApplyRootHaze(fit);
        }

        /// <summary>
        /// A thin layer of translucent soil inside the root layer, so the deeper roots read as embedded in the soil (partly
        /// occluded and faded by depth) while the front ones stay crisp against the window.
        /// </summary>
        void ApplyRootHaze(ChamberLayout fit)
        {
            var haze = Part("RootHaze", rootHazeMaterial);
            haze.gameObject.SetActive(rootHazeMaterial != null);
            var c = fit.PlantPivot;
            var front = c.y - fit.TraySize.y * 0.5f + 0.006f;
            var soilY = fit.FloorHeight + fit.SoilSurface;
            var bottom = fit.FloorHeight + fit.PedestalHeight + TrayWall;
            Box(haze, new Vector3(c.x, (soilY + bottom) * 0.5f, front + RootLayer * 0.45f), new Vector3(fit.TraySize.x - 2f * TrayWall - 0.002f, soilY - bottom - 0.004f, 0.001f),
                rootHazeMaterial != null);
            haze.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>World corners of the tray's inner soil area (XZ min and max) and the window plane, for the tools.</summary>
        public void SoilArea(out Vector2 min, out Vector2 max)
        {
            var c = layout.SpecimenAnchor;
            var hx = layout.TraySize.x * 0.5f - TrayWall;
            var hz = layout.TraySize.y * 0.5f - TrayWall;
            var a = transform.TransformPoint(new Vector3(c.x - hx, c.y, c.z - hz));
            var b = transform.TransformPoint(new Vector3(c.x + hx, c.y, c.z + hz));
            min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z));
            max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
        }

        /// <summary>The tray window's centre in world space (the root view).</summary>
        public Vector3 WindowCentre
        {
            get
            {
                var c = layout.SpecimenAnchor;
                return transform.TransformPoint(new Vector3(c.x, c.y - layout.TrayHeight * 0.5f, c.z - layout.TraySize.y * 0.5f));
            }
        }

        void ApplyTray(ChamberLayout fit)
        {
            var c = new Vector3(fit.PlantPivot.x, fit.FloorHeight, fit.PlantPivot.y);
            var tx = fit.TraySize.x;
            var tz = fit.TraySize.y;
            var th = fit.TrayHeight;
            var soilY = fit.FloorHeight + fit.SoilSurface;
            var trayBottom = fit.FloorHeight + fit.PedestalHeight;

            // Pedestal (tall chambers) and the cyan ring light round it, as in the reference.
            var hasPedestal = fit.PedestalHeight > 0.02f;
            Box(Part("Pedestal", pedestalMaterial), c + Vector3.up * (fit.PedestalHeight * 0.5f), new Vector3(tx * 0.92f, fit.PedestalHeight, tz * 0.92f), hasPedestal);
            var ringY = hasPedestal ? trayBottom - 0.035f : fit.FloorHeight + 0.012f;
            Box(Part("RingLight", accentMaterial), new Vector3(c.x, ringY, c.z), new Vector3(tx * 0.92f + 0.024f, 0.022f, tz * 0.92f + 0.024f));

            // Dark tray: bottom, sides and back; the front is a clear window between two thin rims.
            var wallHeight = th + TrayLip;
            Box(Part("TrayBottom", trayMaterial), new Vector3(c.x, trayBottom + TrayWall * 0.5f, c.z), new Vector3(tx, TrayWall, tz));
            Box(Part("TrayLeft", trayMaterial), new Vector3(c.x - tx * 0.5f + TrayWall * 0.5f, trayBottom + wallHeight * 0.5f, c.z), new Vector3(TrayWall, wallHeight, tz));
            Box(Part("TrayRight", trayMaterial), new Vector3(c.x + tx * 0.5f - TrayWall * 0.5f, trayBottom + wallHeight * 0.5f, c.z), new Vector3(TrayWall, wallHeight, tz));
            Box(Part("TrayBack", trayMaterial), new Vector3(c.x, trayBottom + wallHeight * 0.5f, c.z + tz * 0.5f - TrayWall * 0.5f), new Vector3(tx, wallHeight, TrayWall));
            var front = c.z - tz * 0.5f;
            Box(Part("TrayRimTop", trayMaterial), new Vector3(c.x, trayBottom + wallHeight - 0.009f, front + 0.006f), new Vector3(tx, 0.018f, 0.012f));
            Box(Part("TrayRimBottom", trayMaterial), new Vector3(c.x, trayBottom + TrayWall + 0.009f, front + 0.006f), new Vector3(tx, 0.018f, 0.012f));
            Box(Part("TrayWindow", trayWindowMaterial), new Vector3(c.x, trayBottom + wallHeight * 0.5f, front + 0.003f), new Vector3(tx - 0.004f, wallHeight - 0.004f, 0.005f));

            // Soil profile behind the root layer: dark topsoil over lighter subsoil and deep soil; a thin cap covers the layer.
            var innerX = tx - 2f * TrayWall;
            var innerFront = front + 0.006f;
            var soilFront = innerFront + RootLayer;
            var soilDepthZ = Mathf.Max(0.02f, c.z + tz * 0.5f - TrayWall - soilFront);
            var soilBottom = trayBottom + TrayWall;
            var depth = soilY - soilBottom;
            var zMid = soilFront + soilDepthZ * 0.5f;
            Box(Part("Topsoil", topsoilMaterial), new Vector3(c.x, soilY - depth * 0.19f, zMid), new Vector3(innerX, depth * 0.38f, soilDepthZ));
            Box(Part("Subsoil", subsoilMaterial), new Vector3(c.x, soilY - depth * 0.38f - depth * 0.17f, zMid), new Vector3(innerX, depth * 0.34f, soilDepthZ));
            Box(Part("DeepSoil", deepSoilMaterial), new Vector3(c.x, soilBottom + depth * 0.14f, zMid), new Vector3(innerX, depth * 0.28f, soilDepthZ));
            Box(Part("SoilCap", topsoilMaterial), new Vector3(c.x, soilY - 0.003f, (innerFront + c.z + tz * 0.5f - TrayWall) * 0.5f),
                new Vector3(innerX, 0.006f, c.z + tz * 0.5f - TrayWall - innerFront));

            // Water marks (hidden until the watering wand uses them).
            Box(Part("WetPatch", wetMaterial, PrimitiveType.Cylinder), new Vector3(c.x, soilY + 0.0015f, c.z), new Vector3(0.1f, 0.0008f, 0.1f), false);
            Box(Part("WetFront", wetMaterial), new Vector3(c.x, soilY - 0.02f, innerFront + 0.0015f), new Vector3(0.1f, 0.04f, 0.002f), false);
        }

        /// <summary>Builds (or rebuilds) the roots for a specimen: deterministic per seed, scaled to the tray and plant.</summary>
        public void BuildRoots(int seed, float plantHeight)
        {
            var roots = Part("Roots", rootMaterial);
            var filter = roots.GetComponent<MeshFilter>();
            if (rootMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(rootMesh);
                else
                    DestroyImmediate(rootMesh);
            }

            // Mesh space has its origin at the stem on the soil surface; the window's inner face is half a tray in front.
            var front = -layout.TraySize.y * 0.5f + 0.006f;
            rootMesh = ProceduralRoots.Build(new ProceduralRoots.Settings
            {
                HalfWidth = layout.TraySize.x * 0.5f - TrayWall,
                Depth = layout.TrayHeight - TrayWall,
                Front = front + 0.001f,
                Layer = RootLayer - 0.004f,
                Vigour = Mathf.InverseLerp(0.3f, 3.5f, plantHeight),
                Seed = seed,
            }, "Roots_" + seed);
            filter.sharedMesh = rootMesh;
            roots.gameObject.SetActive(true);
            roots.localPosition = layout.SpecimenAnchor;
            roots.localRotation = Quaternion.identity;
            roots.localScale = Vector3.one;
            roots.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        public Transform WetPatch => Part("WetPatch", wetMaterial, PrimitiveType.Cylinder);
        public Transform WetFront => Part("WetFront", wetMaterial);

        /// <summary>Z (chamber space) of the soil's front face behind the tray window.</summary>
        public float TrayFrontZ => layout.PlantPivot.y - layout.TraySize.y * 0.5f + 0.006f;

        public const float TrayWallThickness = TrayWall;
    }
}
