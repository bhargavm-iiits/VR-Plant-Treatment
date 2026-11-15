using BTP.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// The treatment lab interior shared by every treatment room (the "vineyard treatment lab" concept): muted
    /// olive-grey walls, an exposed warm timber ceiling on dark steel beams with small black pendant spotlights, a wide
    /// low panoramic window along the far wall over a run of matte grey cabinets, a pale beige stone floor, black steel
    /// and timber shelving on the left wall, a grapevine trunk cross-section and a grape tray on the counter, the
    /// light-grey workbench with pruning shears and a metal container, and the grey specimen pot with its cyan ring.
    /// Room space: viewer at the spawn (0, 0, -1.35) facing +Z; walls at x = ±3.6, z = -3.0 / 4.2.
    /// </summary>
    public static partial class TreatmentAssetBuilder
    {
        const string ShearsGlb = "Assets/Lab_Treatment_Assets/pruning_shears.glb";
        const float RoomHalf = 3.6f, RoomHeight = 3.2f, RoomCentreZ = 0.6f;
        const float SillHeight = 0.95f, WindowTop = 2.2f;
        const float CounterHeight = 0.94f, CabinetDepth = 0.6f, CabinetLeft = -3.1f, CabinetRight = 2.0f;

        // Bench in the left foreground (also the tool rack's home); its top is 0.94 m.
        internal static readonly Vector3 BenchCentre = new Vector3(-0.75f, 0f, -0.69f);
        internal const float BenchYaw = -49f, BenchLength = 1.55f, BenchDepth = 0.42f, BenchTop = 0.94f;

        static Material LabWall => Mat("LabWall", new Color(0.4f, 0.42f, 0.33f), 0.1f);
        static Material LabSteel => Mat("LabBeamSteel", new Color(0.13f, 0.14f, 0.15f), 0.35f);
        static Material LabCabinet => Mat("LabCabinet", new Color(0.5f, 0.52f, 0.53f), 0.25f);
        static Material LabCounter => Mat("LabCounter", new Color(0.8f, 0.81f, 0.8f), 0.4f);
        static Material LabBench => Mat("LabBench", new Color(0.74f, 0.76f, 0.78f), 0.45f);
        static Material LabPot => Mat("LabPot", new Color(0.56f, 0.58f, 0.6f), 0.35f);
        static Material LabSeam => Mat("LabSeam", new Color(0.24f, 0.25f, 0.26f), 0.2f);
        static Material ToolRedMat => Mat("ToolRed", new Color(0.75f, 0.12f, 0.1f), 0.4f);
        static Material ToolMetalMat => Mat("ToolMetal", new Color(0.7f, 0.72f, 0.75f), 0.6f);

        static Material LabFloor()
        {
            var floor = MaterialFactory.Solid($"{Materials}/LabFloorStone.mat", new Color(1f, 0.93f, 0.8f), 0.3f, ProceduralTextures.FloorTiles);
            floor.SetTextureScale("_BaseMap", new Vector2(5f, 5f));
            return floor;
        }

        static Material LabTimber()
        {
            var timber = MaterialFactory.Solid($"{Materials}/LabTimber.mat", new Color(1.0f, 0.86f, 0.68f), 0.15f, ProceduralTextures.Wood);
            timber.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            return timber;
        }

        static void RingObject(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Vector3 euler)
        {
            var ring = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(parent, false);
            ring.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            ring.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ring.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Walls, floor, timber ceiling, steel beams, the panoramic windows and the pendant spotlights.</summary>
        static void LabShell(Transform root)
        {
            // Nearly clear, low-gloss window glass, so the vineyard reads through it instead of a white sky reflection.
            var glass = MaterialFactory.Lit($"{Materials}/WindowGlass.mat", new Color(0.85f, 0.93f, 1f, 0.05f), 0f, 0.2f, transparent: true, doubleSided: true);
            var frame = Mat("WindowFrame", new Color(0.16f, 0.17f, 0.18f), 0.4f);
            var wall = LabWall;
            const float span = 2f * RoomHalf;

            var floorObject = Part(PrimitiveType.Cube, "Floor", root, new Vector3(0f, -0.02f, RoomCentreZ), new Vector3(span, 0.04f, span), LabFloor(), default, true);
            XRSceneWiring.MakeTeleportArea(floorObject);

            // Exposed timber ceiling: boards, joists along the room, three bolted steel beams across it.
            var timber = LabTimber();
            Part(PrimitiveType.Cube, "Ceiling", root, new Vector3(0f, RoomHeight, RoomCentreZ), new Vector3(span, 0.06f, span), timber);
            for (var x = -2.4f; x <= 2.41f; x += 1.2f)
                Part(PrimitiveType.Cube, "Joist", root, new Vector3(x, RoomHeight - 0.1f, RoomCentreZ), new Vector3(0.1f, 0.14f, span), timber);
            foreach (var z in new[] { -1.4f, 0.6f, 2.6f })
            {
                Part(PrimitiveType.Cube, "SteelBeam", root, new Vector3(0f, RoomHeight - 0.2f, z), new Vector3(span, 0.24f, 0.16f), LabSteel);
                for (var x = -3f; x <= 3.01f; x += 1.2f)
                    Part(PrimitiveType.Cylinder, "Bolt", root, new Vector3(x, RoomHeight - 0.2f, z - 0.085f), new Vector3(0.025f, 0.006f, 0.025f), frame,
                        new Vector3(90f, 0f, 0f));
            }

            // Solid wall behind the viewer and on the left (shelving side).
            Part(PrimitiveType.Cube, "WallBack", root, new Vector3(0f, RoomHeight / 2f, RoomCentreZ - RoomHalf), new Vector3(span, RoomHeight, 0.1f), wall, default, true);
            Part(PrimitiveType.Cube, "WallLeft", root, new Vector3(-RoomHalf, RoomHeight / 2f, RoomCentreZ), new Vector3(span, RoomHeight, 0.1f), wall,
                new Vector3(0f, 90f, 0f), true);

            // Far and right walls: low wall, a wide low panoramic window band, header up to the ceiling.
            foreach (var (x, z, yaw) in new[] { (0f, RoomCentreZ + RoomHalf, 0f), (RoomHalf, RoomCentreZ, 90f) })
            {
                var rotation = new Vector3(0f, yaw, 0f);
                var along = Quaternion.Euler(rotation) * Vector3.right;
                const float windowHeight = WindowTop - SillHeight;
                Part(PrimitiveType.Cube, "WallLow", root, new Vector3(x, SillHeight / 2f, z), new Vector3(span, SillHeight, 0.1f), wall, rotation, true);
                Part(PrimitiveType.Cube, "WallHeader", root, new Vector3(x, (WindowTop + RoomHeight) / 2f, z), new Vector3(span, RoomHeight - WindowTop, 0.1f), wall, rotation);
                Part(PrimitiveType.Cube, "Window", root, new Vector3(x, SillHeight + windowHeight / 2f, z), new Vector3(span, windowHeight, 0.02f), glass, rotation);
                Part(PrimitiveType.Cube, "FrameBottom", root, new Vector3(x, SillHeight, z), new Vector3(span, 0.05f, 0.12f), frame, rotation);
                Part(PrimitiveType.Cube, "FrameTop", root, new Vector3(x, WindowTop, z), new Vector3(span, 0.05f, 0.12f), frame, rotation);
                for (var i = -1; i <= 1; i++)
                    Part(PrimitiveType.Cube, "Mullion", root, new Vector3(x, SillHeight + windowHeight / 2f, z) + along * (i * span / 3.2f),
                        new Vector3(0.05f, windowHeight, 0.1f), frame, rotation);
            }

            // Small black pendant spotlights hanging from the beams; two warm point lights do the lighting.
            var shade = Mat("PendantShade", new Color(0.07f, 0.07f, 0.08f), 0.5f);
            var bulb = Glow("PendantBulb", new Color(1f, 0.86f, 0.62f), 2.2f);
            foreach (var z in new[] { 0.6f, 2.6f })
            foreach (var x in new[] { -1.8f, 0f, 1.8f })
            {
                const float top = RoomHeight - 0.32f;
                Part(PrimitiveType.Cylinder, "PendantCord", root, new Vector3(x, top - 0.12f, z), new Vector3(0.008f, 0.12f, 0.008f), shade);
                Part(PrimitiveType.Cylinder, "PendantShade", root, new Vector3(x, top - 0.3f, z), new Vector3(0.09f, 0.06f, 0.09f), shade);
                Part(PrimitiveType.Cylinder, "PendantBulb", root, new Vector3(x, top - 0.362f, z), new Vector3(0.07f, 0.004f, 0.07f), bulb);
            }

            foreach (var position in new[] { new Vector3(-1.2f, 2.6f, 0.3f), new Vector3(1.2f, 2.6f, 1.8f) })
            {
                var light = new GameObject("RoomLight", typeof(Light)).GetComponent<Light>();
                light.transform.SetParent(root, false);
                light.transform.localPosition = position;
                light.type = LightType.Point;
                light.range = 6.5f;
                light.intensity = 1.5f;
                light.color = new Color(1f, 0.9f, 0.76f);
                light.shadows = LightShadows.None;
            }
        }

        /// <summary>The grey specimen pot with soil and its cyan ring: the leaf specimen stands on top of it.</summary>
        static void SpecimenPot(Transform root, Vector3 position)
        {
            const float radius = 0.21f, height = 0.86f;
            var cyan = Glow("Marker", UIPalette.Cyan, 2.2f);
            Part(PrimitiveType.Cylinder, "SpecimenPot", root, position + Vector3.up * (height / 2f), new Vector3(radius * 2f, height / 2f, radius * 2f), LabPot, default, true);
            Part(PrimitiveType.Cylinder, "PotSoil", root, position + Vector3.up * (height + 0.002f), new Vector3(radius * 1.86f, 0.004f, radius * 1.86f), SoilMat);
            // The cyan ring: a glowing band round the pot just under the rim, and a thin ring on the rim.
            Part(PrimitiveType.Cylinder, "PotBand", root, position + Vector3.up * (height - 0.035f), new Vector3(radius * 2f + 0.012f, 0.009f, radius * 2f + 0.012f), cyan);
            RingObject("PotRing", root, ProceduralMeshes.Ring("TreatmentPotRing", radius - 0.012f, radius), cyan, position + Vector3.up * (height + 0.004f), Vector3.zero);
            Part(PrimitiveType.Cylinder, "HolderClip", root, position + Vector3.up * 0.9f, new Vector3(0.025f, 0.04f, 0.025f), Dark);
        }

        /// <summary>A lab table in the cabinet style (grey body, pale top), for the room's lesson stations.</summary>
        internal static void LabTable(Transform root, string name, Vector3 centre, Vector3 size, float yaw)
        {
            var table = Group(name, root);
            table.localPosition = new Vector3(centre.x, 0f, centre.z);
            table.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Part(PrimitiveType.Cube, "Body", table, new Vector3(0f, (size.y - 0.04f) / 2f, 0f), new Vector3(size.x - 0.05f, size.y - 0.04f, size.z - 0.05f), LabCabinet, default, true);
            Part(PrimitiveType.Cube, "Top", table, new Vector3(0f, size.y - 0.02f, 0f), new Vector3(size.x, 0.04f, size.z), LabCounter, default, true);
        }

        /// <summary>The light-grey workbench in the left foreground with the pruning shears and a metal container.</summary>
        static void Workbench(Transform root)
        {
            var bench = Group("Workbench", root);
            bench.localPosition = BenchCentre;
            bench.localRotation = Quaternion.Euler(0f, BenchYaw, 0f);
            Part(PrimitiveType.Cube, "ToolBench", bench, new Vector3(0f, (BenchTop - 0.05f) / 2f, 0f), new Vector3(BenchLength - 0.06f, BenchTop - 0.05f, BenchDepth - 0.04f),
                LabCabinet, default, true);
            Part(PrimitiveType.Cube, "BenchTop", bench, new Vector3(0f, BenchTop - 0.025f, 0f), new Vector3(BenchLength, 0.05f, BenchDepth), LabBench, default, true);

            // Metal container (back-left end) and the red-handled pruning shears lying near the front edge.
            Part(PrimitiveType.Cylinder, "Container", bench, new Vector3(-0.58f, BenchTop + 0.13f, 0.05f), new Vector3(0.17f, 0.13f, 0.17f), Steel);
            Part(PrimitiveType.Cylinder, "ContainerLid", bench, new Vector3(-0.58f, BenchTop + 0.265f, 0.05f), new Vector3(0.175f, 0.006f, 0.175f), ToolMetalMat);
            var shears = Group("PruningShears", bench);
            shears.localPosition = new Vector3(-0.22f, BenchTop + 0.002f, -0.1f);
            shears.localRotation = Quaternion.Euler(0f, 20f, 0f);
            if (!GlbModel(shears, ShearsGlb, 0.21f, "Helix"))
                Part(PrimitiveType.Cube, "Handle", shears, new Vector3(0f, 0.01f, 0f), new Vector3(0.18f, 0.02f, 0.03f), ToolRedMat);
        }

        /// <summary>
        /// A copy of a whole supplied GLB (all its meshes, in their layout) laid flat and scaled so its longest side is
        /// <paramref name="length"/> metres, resting on the parent's origin. Meshes whose name starts with
        /// <paramref name="skip"/> are left out (e.g. a dense spring invisible at arm's length); handle meshes are tinted red.
        /// </summary>
        static bool GlbModel(Transform parent, string glbPath, float length, string skip)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (glb == null)
                return false;
            var copy = (GameObject)Object.Instantiate(glb, parent, false);
            copy.name = "Model";
            foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
                if (skip != null && filter.name.StartsWith(skip))
                    Object.DestroyImmediate(filter.gameObject);
            foreach (var renderer in copy.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                if (renderer.name.StartsWith("Hand"))
                    renderer.sharedMaterial = ToolRedMat;
            }

            copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            var bounds = LocalBounds(parent, copy);
            if (bounds.size == Vector3.zero)
            {
                Object.DestroyImmediate(copy);
                return false;
            }

            // Lay it flat: its thinnest axis becomes vertical.
            if (bounds.size.x < bounds.size.y && bounds.size.x < bounds.size.z)
                copy.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (bounds.size.z < bounds.size.y && bounds.size.z < bounds.size.x)
                copy.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bounds = LocalBounds(parent, copy);
            copy.transform.localScale = Vector3.one * (length / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
            bounds = LocalBounds(parent, copy);
            copy.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return true;
        }

        /// <summary>Bounds of a model's meshes in <paramref name="space"/>'s local coordinates.</summary>
        static Bounds LocalBounds(Transform space, GameObject model)
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = space.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            }

            return bounds;
        }

        /// <summary>Cabinets under the window, the trunk cross-section and grape tray, and the left-wall shelving.</summary>
        static void LabFurnishings(Transform root)
        {
            var furniture = Group("LabFurniture", root);

            // Matte grey cabinets along the far wall, under the window, with a pale counter top.
            const float wallZ = RoomCentreZ + RoomHalf - 0.05f;
            const float length = CabinetRight - CabinetLeft;
            const float centreX = (CabinetLeft + CabinetRight) / 2f;
            const float front = wallZ - CabinetDepth;
            Part(PrimitiveType.Cube, "Cabinets", furniture, new Vector3(centreX, (CounterHeight - 0.04f + 0.05f) / 2f, wallZ - CabinetDepth / 2f),
                new Vector3(length, CounterHeight - 0.09f, CabinetDepth), LabCabinet, default, true);
            Part(PrimitiveType.Cube, "Plinth", furniture, new Vector3(centreX, 0.025f, wallZ - CabinetDepth / 2f + 0.04f), new Vector3(length, 0.05f, CabinetDepth - 0.08f), LabSeam);
            Part(PrimitiveType.Cube, "Counter", furniture, new Vector3(centreX, CounterHeight - 0.02f, wallZ - CabinetDepth / 2f - 0.01f),
                new Vector3(length + 0.04f, 0.04f, CabinetDepth + 0.04f), LabCounter, default, true);
            var doors = Mathf.RoundToInt(length / 0.6f);
            var doorWidth = length / doors;
            for (var i = 0; i < doors; i++)
            {
                var x = CabinetLeft + doorWidth * (i + 0.5f);
                if (i > 0)
                    Part(PrimitiveType.Cube, "DoorSeam", furniture, new Vector3(CabinetLeft + doorWidth * i, (CounterHeight + 0.05f) / 2f, front - 0.002f),
                        new Vector3(0.008f, CounterHeight - 0.13f, 0.004f), LabSeam);
                Part(PrimitiveType.Cube, "DrawerSeam", furniture, new Vector3(x, CounterHeight - 0.2f, front - 0.002f), new Vector3(doorWidth - 0.03f, 0.006f, 0.004f), LabSeam);
                Part(PrimitiveType.Cube, "Handle", furniture, new Vector3(x, CounterHeight - 0.13f, front - 0.012f), new Vector3(0.16f, 0.014f, 0.016f), ToolMetalMat);
            }

            // Upright grapevine trunk cross-section on a small black stand, and a shallow tray of green and purple grapes.
            var bark = Mat("TrunkBark", new Color(0.3f, 0.21f, 0.14f), 0.1f);
            var heartwood = Mat("TrunkFace", new Color(0.82f, 0.62f, 0.4f), 0.15f);
            var growthRings = Mat("TrunkRings", new Color(0.55f, 0.36f, 0.2f), 0.1f);
            var trunk = new Vector3(1.0f, CounterHeight, wallZ - 0.32f);
            Part(PrimitiveType.Cube, "TrunkStand", furniture, trunk + new Vector3(0f, 0.012f, 0f), new Vector3(0.2f, 0.024f, 0.1f), Dark);
            Part(PrimitiveType.Cylinder, "TrunkSlice", furniture, trunk + new Vector3(0f, 0.15f, 0f), new Vector3(0.25f, 0.035f, 0.25f), bark, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, "TrunkFace", furniture, trunk + new Vector3(0f, 0.15f, -0.036f), new Vector3(0.225f, 0.002f, 0.225f), heartwood, new Vector3(90f, 0f, 0f));
            foreach (var r in new[] { 0.08f, 0.055f, 0.03f })
                RingObject("TrunkRing", furniture, ProceduralMeshes.Ring($"TrunkRing_{r * 1000f:0}", r, r + 0.004f), growthRings,
                    trunk + new Vector3(0f, 0.15f, -0.039f), new Vector3(-90f, 0f, 0f));

            var tray = new Vector3(1.62f, CounterHeight, wallZ - 0.3f);
            Part(PrimitiveType.Cube, "GrapeTray", furniture, tray + new Vector3(0f, 0.015f, 0f), new Vector3(0.36f, 0.03f, 0.22f), White);
            var purple = Mat("GrapePurple", new Color(0.3f, 0.12f, 0.35f), 0.5f);
            var green = Mat("GrapeGreen", new Color(0.62f, 0.74f, 0.3f), 0.5f);
            for (var cluster = 0; cluster < 2; cluster++)
            {
                var centre = tray + new Vector3(cluster == 0 ? -0.08f : 0.08f, 0.03f, 0f);
                for (var i = 0; i < 10; i++)
                {
                    var layer = i < 6 ? 0 : i < 9 ? 1 : 2;
                    var angle = i * 2.4f;
                    var spread = layer == 0 ? 0.035f : layer == 1 ? 0.02f : 0f;
                    var p = centre + new Vector3(Mathf.Cos(angle) * spread, 0.014f + layer * 0.02f, Mathf.Sin(angle) * spread * 1.4f);
                    Part(PrimitiveType.Sphere, "Grape", furniture, p, Vector3.one * 0.026f, cluster == 0 ? purple : green);
                }
            }

            // Black steel and timber shelving on the left wall: flasks, grey storage boxes, a trailing plant.
            var shelf = Group("Shelving", furniture);
            shelf.localPosition = new Vector3(-RoomHalf + 0.25f, 0f, 1.75f);
            shelf.localRotation = Quaternion.Euler(0f, 90f, 0f);
            const float shelfWidth = 1.4f, shelfDepth = 0.38f;
            var timber = LabTimber();
            foreach (var x in new[] { -shelfWidth / 2f, shelfWidth / 2f })
            foreach (var z in new[] { -shelfDepth / 2f, shelfDepth / 2f })
                Part(PrimitiveType.Cube, "Post", shelf, new Vector3(x, 1.05f, z), new Vector3(0.03f, 2.1f, 0.03f), LabSteel);
            foreach (var y in new[] { 0.42f, 0.98f, 1.52f, 2.04f })
                Part(PrimitiveType.Cube, "Shelf", shelf, new Vector3(0f, y, 0f), new Vector3(shelfWidth + 0.04f, 0.04f, shelfDepth), timber, default, true);

            var flaskGlass = Clear("FlaskGlass", new Color(0.85f, 0.95f, 1f, 0.3f));
            for (var i = 0; i < 3; i++)
            {
                var x = -0.45f + i * 0.13f;
                Part(PrimitiveType.Cylinder, "Flask", shelf, new Vector3(x, 1.54f + 0.07f, 0f), new Vector3(0.075f, 0.07f, 0.075f), flaskGlass);
                Part(PrimitiveType.Cylinder, "FlaskNeck", shelf, new Vector3(x, 1.54f + 0.17f, 0f), new Vector3(0.025f, 0.03f, 0.025f), flaskGlass);
            }

            var box = Mat("StorageBox", new Color(0.62f, 0.64f, 0.66f), 0.2f);
            foreach (var x in new[] { -0.35f, 0.1f })
            {
                Part(PrimitiveType.Cube, "StorageBox", shelf, new Vector3(x, 1.0f + 0.08f, 0f), new Vector3(0.36f, 0.16f, 0.3f), box);
                Part(PrimitiveType.Cube, "BoxLid", shelf, new Vector3(x, 1.0f + 0.165f, 0f), new Vector3(0.37f, 0.012f, 0.31f), LabCabinet);
            }

            Part(PrimitiveType.Cylinder, "Canister", shelf, new Vector3(0.4f, 0.44f + 0.11f, 0f), new Vector3(0.16f, 0.11f, 0.16f), Steel);
            Part(PrimitiveType.Cube, "StorageBox", shelf, new Vector3(-0.3f, 0.44f + 0.09f, 0f), new Vector3(0.4f, 0.18f, 0.3f), box);

            Part(PrimitiveType.Cylinder, "PlantPot", shelf, new Vector3(0.35f, 2.06f + 0.06f, 0f), new Vector3(0.16f, 0.06f, 0.16f), White);
            for (var i = 0; i < 6; i++)
                Part(PrimitiveType.Sphere, "Foliage", shelf, new Vector3(0.35f + Mathf.Cos(i) * 0.09f, 2.22f + (i % 2) * 0.05f, Mathf.Sin(i) * 0.08f), Vector3.one * 0.13f, Green);
            for (var strand = 0; strand < 3; strand++)
            for (var i = 0; i < 6; i++)
                Part(PrimitiveType.Sphere, "Trailing", shelf, new Vector3(0.28f + strand * 0.08f, 2.0f - i * 0.11f, -shelfDepth / 2f - 0.02f),
                    new Vector3(0.06f, 0.07f, 0.04f), Green);
        }
    }
}
