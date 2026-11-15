using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BTP.Editor
{
    /// <summary>
    /// The Farm's surroundings, after the Greenhouse Park reference: the asset's white greenhouse frame, stretched over
    /// the whole fenced field and clad in a translucent cover (UV-stabilised polyethylene film by default, or glass;
    /// see <see cref="CoverOption"/>) with an open doorway in each gable at the spawn and far ends, bush belts and clumps
    /// around it, a grass ground from the asset's Ground texture, an asphalt road with a grass verge south of the fence,
    /// a closed ring of low hazy hills, and hazy pale-blue daylight. Everything here is decorative: static, no colliders
    /// (except the ground, as before), no teleport areas. Models and textures come from Tools/blender_greenhouse.py.
    /// Shared by FarmSceneBuilder.Build and the in-place update step below.
    /// </summary>
    public static class GreenhouseParkBuilder
    {
        const string ModelFolder = ProjectPaths.Root + "/Models/Environment";
        const string TextureFolder = ProjectPaths.Root + "/Textures/Environment/Greenhouse";
        const string MaterialFolder = ProjectPaths.EnvironmentMaterials + "/Greenhouse";
        const string GreenhouseModel = ModelFolder + "/Greenhouse.fbx";
        const string BushModel = ModelFolder + "/GreenhouseBush.fbx";
        const string BushLayout = ModelFolder + "/GreenhouseBushLayout.json";

        // Texture caps: PC (BC7) and Android/Quest (ASTC).
        const int PcMaxSize = 2048, AndroidMaxSize = 1024;

        /// <summary>
        /// The greenhouse frame's footprint: the fenced field plus a margin, so its columns stand outside the fence.
        /// The ends sit 1 m beyond the fence; the sides 2.5 m, so the side walls stand well clear of the outermost
        /// orchard and corn rows. The frame mesh (16 m wide, 32.2 m long, 6.8 m tall
        /// natively) is stretched to the footprint from its own bounds; Y keeps a fixed scale that gives the tallest
        /// trees (3.8 m) headroom under the roof trusses.
        /// </summary>
        const float FrameSideMargin = 2.5f, FrameEndMargin = 1f, FrameHeightScale = 1.25f;
        internal const float FrameMinX = FarmSceneBuilder.FenceMinX - FrameSideMargin, FrameMaxX = FarmSceneBuilder.FenceMaxX + FrameSideMargin;
        internal const float FrameMinZ = FarmSceneBuilder.FenceMinZ - FrameEndMargin, FrameMaxZ = FarmSceneBuilder.FenceMaxZ + FrameEndMargin;

        /// <summary>What the greenhouse frame is clad in.</summary>
        public enum CoverMaterial
        {
            /// <summary>Milky, slightly blue-green UV-stabilised polyethylene film (alpha 0.36) with faint wrinkles and seams.</summary>
            Polyethylene,
            /// <summary>Clear, slightly blue glass (alpha 0.15), smooth with sky reflections.</summary>
            Glass,
        }

        /// <summary>
        /// The cover option. To switch, change this constant and re-run BTP/Setup/8 - Update Farm Surroundings (or
        /// rebuild the Farm). Both materials (Materials/Environment/Greenhouse/GH_CoverFilm and GH_CoverGlass) are
        /// always kept up to date, so the Cover renderer's material can also simply be swapped by hand.
        /// </summary>
        public const CoverMaterial CoverOption = CoverMaterial.Polyethylene;

        /// <summary>
        /// The cover skin stands this far outside the frame's members (above the rafters, outside the wall and
        /// gable posts), so the white frame shows through it and nothing z-fights.
        /// </summary>
        const float CoverOffset = 0.025f;

        /// <summary>The open doorway (width, height) left in each gable end's cover, centred on the aisle.</summary>
        static readonly Vector2 CoverDoor = new Vector2(4f, 3.2f);

        /// <summary>The film texture repeats every 4 m (the cover's UVs are in metres).</summary>
        const float FilmTile = 4f;

        /// <summary>Section of the slim white jambs and header framing each doorway.</summary>
        const float DoorFrameSize = 0.1f;

        /// <summary>Bush canopies stay at least this far outside the frame's footprint.</summary>
        const float FrameBushClearance = 1.5f;

        /// <summary>
        /// Where the asset's bush belts (GreenhouseBushLayout.json) are anchored: 3.5 m beyond the frame's far gable,
        /// where they stood relative to the original (smaller) field.
        /// </summary>
        static readonly Vector3 BushLayoutOrigin = new Vector3(0f, 0f, FrameMaxZ + 3.5f);

        // The asset's grass crop repeats every 8.75 m (400 px of the 2048 px texture).
        const float GrassTile = 8.75f, GroundSize = 600f;

        const float RoadZ = -20f, RoadWidth = 6.5f, RoadLength = 480f, VergeWidth = 2.6f;
        const float HillInner = 150f, HillOuter = 270f, HillMin = 6f, HillMax = 22f;

        /// <summary>Canopy radius of a scale-1 bush clump (its mesh is about 7.8 x 6.7 m across, 8 m tall).</summary>
        const float BushRadius = 3.7f;

        /// <summary>Scene objects this step replaces (the old scenery and any earlier surroundings).</summary>
        static readonly string[] Replaced = { "WaterTower", "Mountains", "Boulders", "BackgroundTrees", "Surroundings" };

        sealed class Materials
        {
            public Material Grass, Bush, WhiteMetal, Road, Verge, Hills, CoverFilm, CoverGlass;
        }

        [MenuItem("BTP/Setup/8 - Update Farm Surroundings (Greenhouse Park)", priority = 8)]
        public static void UpdateFarmScene()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before updating the saved Farm.");
            var scene = SceneManager.GetSceneByPath(ProjectPaths.FarmScene);
            if (!scene.isLoaded)
            {
                if (SceneManager.GetActiveScene().isDirty)
                    throw new System.InvalidOperationException("Save the open scene first; this step opens the Farm scene.");
                scene = EditorSceneManager.OpenScene(ProjectPaths.FarmScene, OpenSceneMode.Single);
            }

            Build(Root(scene, "Environment"));
            ConfigureLighting(Root(scene, "Lighting"));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[BTP] Updated the surroundings of {ProjectPaths.FarmScene} (greenhouse park, road, hills).");
        }

        /// <summary>Builds (or rebuilds) the ground and everything outside the fence under <paramref name="environment"/>.</summary>
        public static void Build(Transform environment)
        {
            foreach (var name in Replaced)
            {
                for (var child = environment.Find(name); child != null; child = environment.Find(name))
                    Object.DestroyImmediate(child.gameObject);
            }

            var m = CreateMaterials();
            BuildGround(environment, m);

            var root = EnvironmentKit.Group("Surroundings", environment).transform;
            BuildGreenhouse(root, m);
            BuildBushes(root, m);
            BuildRoad(root, m);
            EnvironmentKit.MeshObject("Hills", root, ProceduralMeshes.HillRing("FarmHills", HillInner, HillOuter, HillMin, HillMax, 11),
                m.Hills, Vector3.zero, Quaternion.identity, Vector3.one);
        }

        /// <summary>Hazy, pale-blue daylight (replaces the Sun under <paramref name="lighting"/>).</summary>
        public static void ConfigureLighting(Transform lighting)
        {
            for (var i = lighting.childCount - 1; i >= 0; i--)
            {
                if (lighting.GetChild(i).GetComponent<Light>() != null)
                    Object.DestroyImmediate(lighting.GetChild(i).gameObject);
            }

            EnvironmentKit.ConfigureLighting(lighting,
                sunColor: new Color(1f, 0.95f, 0.86f), sunIntensity: 1.3f, sunEuler: new Vector3(40f, -35f, 0f),
                skyTint: new Color(0.70f, 0.72f, 0.76f),
                ambientSky: new Color(0.62f, 0.70f, 0.82f), ambientEquator: new Color(0.62f, 0.65f, 0.60f),
                ambientGround: new Color(0.35f, 0.33f, 0.26f),
                fogColor: new Color(0.75f, 0.80f, 0.86f), fogStart: 60f, fogEnd: 450f,
                skyboxPath: ProjectPaths.EnvironmentMaterials + "/FarmSky.mat");

            // A greyer tint and a thinner atmosphere give a pale, hazy blue without the yellow horizon band.
            var sky = RenderSettings.skybox;
            sky.SetFloat("_AtmosphereThickness", 0.95f);
            sky.SetFloat("_Exposure", 1.4f);
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetColor("_GroundColor", new Color(0.55f, 0.60f, 0.62f));
            EditorUtility.SetDirty(sky);
        }

        // ------------------------------------------------------------------ materials and textures

        enum Kind { Albedo, Cutout, Normal, Mask }

        static Texture2D Texture(string name, Kind kind)
        {
            var path = $"{TextureFolder}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException($"Missing {path}; run Tools/blender_greenhouse.py.");

            // Re-import only when the settings differ (a full re-import of the 2048 maps takes a while).
            var android = importer.GetPlatformTextureSettings("Android");
            var type = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            var configured = importer.textureType == type && importer.maxTextureSize == PcMaxSize && android.overridden &&
                             android.maxTextureSize == AndroidMaxSize && importer.sRGBTexture == (kind == Kind.Albedo || kind == Kind.Cutout) &&
                             importer.alphaIsTransparency == (kind == Kind.Cutout) &&
                             importer.alphaSource == (kind == Kind.Cutout || kind == Kind.Mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None);
            if (!configured)
            {
                switch (kind)
                {
                    case Kind.Albedo: TextureSettings.ConfigureAlbedo(path, PcMaxSize, AndroidMaxSize); break;
                    case Kind.Cutout: TextureSettings.ConfigureCutout(path, PcMaxSize, AndroidMaxSize); break;
                    // The blender step already converted the asset's DirectX normals to OpenGL (green flipped).
                    case Kind.Normal: TextureSettings.ConfigureNormal(path, PcMaxSize, AndroidMaxSize); break;
                    case Kind.Mask: TextureSettings.ConfigureMask(path, PcMaxSize, AndroidMaxSize); break;
                }
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Materials CreateMaterials()
        {
            string M(string name) => $"{MaterialFolder}/{name}.mat";
            var m = new Materials
            {
                Grass = MaterialFactory.SimpleLit(M("GH_Grass"), Texture("GH_Grass_Albedo", Kind.Albedo), Texture("GH_Grass_Normal", Kind.Normal), false),
                Bush = MaterialFactory.SimpleLit(M("GH_Bush"), Texture("GH_Bush_Albedo", Kind.Cutout), Texture("GH_Bush_Normal", Kind.Normal), true),
                WhiteMetal = MaterialFactory.Lit(M("GH_WhiteMetal"), new Color(0.90f, 0.91f, 0.92f), 0.35f, 0.55f),
                Road = MaterialFactory.Solid(M("Road"), Color.white, 0.12f, ProceduralTextures.Road),
                Verge = MaterialFactory.Solid(M("Verge"), Color.white, 0.03f, ProceduralTextures.Verge),
                Hills = MaterialFactory.Solid(M("Hills"), Color.white, 0f, ProceduralTextures.Hills),
                // The cover: transparent, double-sided, no shadows received, and queued just ahead of the other
                // transparents (Transparent-50). From inside, the skin is behind everything else in the field, so the
                // selection rings, teleport pads, cards and labels then blend over it rather than vanish behind it.
                CoverFilm = MaterialFactory.Lit(M("GH_CoverFilm"), new Color(0.92f, 0.97f, 0.95f, 0.36f), 0f, 0.3f,
                    ProceduralTextures.GreenhouseFilm, transparent: true, doubleSided: true, receiveShadows: false, queueOffset: -50),
                CoverGlass = MaterialFactory.Lit(M("GH_CoverGlass"), new Color(0.82f, 0.91f, 0.96f, 0.15f), 0f, 0.95f,
                    transparent: true, doubleSided: true, receiveShadows: false, queueOffset: -50),
            };
            m.Grass.SetFloat("_Smoothness", 0.05f);
            // A slightly lower clip keeps the leaf cards' canopy dense, as in the reference belts.
            m.Bush.SetFloat("_Cutoff", 0.4f);
            m.Verge.SetTextureScale("_BaseMap", new Vector2(RoadLength / 4f, 1f));
            m.CoverFilm.SetTextureScale("_BaseMap", Vector2.one / FilmTile);
            return m;
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// The 600 m ground as one quad whose UVs repeat the asset's grass every 8.75 m. Keeps a collider, like the plane
        /// it replaces (not a teleport area).
        /// </summary>
        static void BuildGround(Transform environment, Materials m)
        {
            var index = 0;
            var old = environment.Find("Ground");
            if (old != null)
            {
                index = old.GetSiblingIndex();
                Object.DestroyImmediate(old.gameObject);
            }

            var tiles = GroundSize / GrassTile;
            var uv = new Rect(-tiles * 0.5f, -tiles * 0.5f, tiles, tiles);
            var mesh = ProceduralMeshes.GroundQuad("FarmGround", new Vector2(GroundSize, GroundSize), uv);
            var ground = EnvironmentKit.MeshObject("Ground", environment, mesh, m.Grass, Vector3.zero, Quaternion.identity, Vector3.one);
            ground.GetComponent<MeshRenderer>().receiveShadows = true;
            // A thin box rather than a mesh collider: PhysX warns about (and is less stable with) 600 m triangles.
            var collider = ground.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, -0.05f, 0f);
            collider.size = new Vector3(GroundSize, 0.1f, GroundSize);
            ground.transform.SetSiblingIndex(index);
        }

        // ------------------------------------------------------------------ greenhouse

        /// <summary>
        /// The asset's white frame alone (no glass, floor, plants or yard), stretched over the field: its X/Z bounds
        /// are fitted to the frame footprint and its base sits on the ground.
        /// </summary>
        static void BuildGreenhouse(Transform root, Materials m)
        {
            var frameMesh = FrameShell(LoadMeshes(GreenhouseModel)["Greenhouse_Frame"]);
            var park = EnvironmentKit.Group("GreenhousePark", root).transform;
            var (position, scale) = FitFrame(frameMesh.bounds);
            var frame = EnvironmentKit.MeshObject("Frame", park, frameMesh, m.WhiteMetal, position, Quaternion.identity, scale, true);
            BuildCover(park, frameMesh, frame.transform, m);
        }

        /// <summary>
        /// The cover skin over the fitted frame: one mesh, one material, no collider (it never blocks XR rays,
        /// teleporting or mouse picking), no shadows cast or received, so the field stays sunlit. Its cross-section is
        /// sampled from the frame mesh itself (<see cref="CoverOutline"/>); it runs the frame's full length, and each
        /// gable is closed except for a <see cref="CoverDoor"/> doorway on the aisle, framed by slim white jambs.
        /// </summary>
        static void BuildCover(Transform park, Mesh frameMesh, Transform frame, Materials m)
        {
            var toPark = Matrix4x4.TRS(frame.localPosition, frame.localRotation, frame.localScale);
            var vertices = frameMesh.vertices;
            var bounds = new Bounds(toPark.MultiplyPoint3x4(vertices[0]), Vector3.zero);
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = toPark.MultiplyPoint3x4(vertices[i]);
                bounds.Encapsulate(vertices[i]);
            }

            var outline = CoverOutline(vertices, frameMesh.triangles, bounds);
            var mesh = ProceduralMeshes.GreenhouseCover("GreenhouseCover", outline, bounds.min.z - CoverOffset,
                bounds.max.z + CoverOffset, CoverDoor);
            var cover = EnvironmentKit.MeshObject("Cover", park, mesh, CoverOption == CoverMaterial.Glass ? m.CoverGlass : m.CoverFilm,
                Vector3.zero, Quaternion.identity, Vector3.one);
            var renderer = cover.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Never an occluder (it is see-through); a single mesh gains nothing from static batching.
            GameObjectUtility.SetStaticEditorFlags(cover, StaticEditorFlags.OccludeeStatic);

            // Jambs and a header just inside each gable's skin, so the film's doorway edges sit on a frame.
            var half = CoverDoor.x * 0.5f;
            const float s = DoorFrameSize;
            var boxes = new List<Bounds>();
            foreach (var z in new[] { bounds.min.z + s * 0.5f, bounds.max.z - s * 0.5f })
            {
                foreach (var side in new[] { -1f, 1f })
                    boxes.Add(new Bounds(new Vector3(side * (half + s * 0.5f), (CoverDoor.y + s) * 0.5f, z), new Vector3(s, CoverDoor.y + s, s)));
                boxes.Add(new Bounds(new Vector3(0f, CoverDoor.y + s * 0.5f, z), new Vector3(CoverDoor.x + 2f * s, s, s)));
            }

            EnvironmentKit.MeshObject("DoorFrames", park, ProceduralMeshes.Boxes("GreenhouseDoorFrames", boxes.ToArray()), m.WhiteMetal,
                Vector3.zero, Quaternion.identity, Vector3.one, true);
        }

        /// <summary>
        /// The cover's cross-section (X, Y in park space), taken from the fitted frame mesh rather than assumed: the
        /// frame's upper outline seen end-on (the highest point of any member at each X, sampled every 10 cm across its
        /// width, which follows the roof's pitch or curve, the ridge and the eaves), reduced to its corners, raised by
        /// <see cref="CoverOffset"/> plus any reduction error and widened by <see cref="CoverOffset"/> past each wall.
        /// </summary>
        static Vector2[] CoverOutline(Vector3[] vertices, int[] triangles, Bounds bounds)
        {
            const float step = 0.1f, tolerance = 0.01f;
            var count = Mathf.CeilToInt(bounds.size.x / step) + 1;
            float x0 = bounds.min.x + 1e-3f, x1 = bounds.max.x - 1e-3f;
            var dx = (x1 - x0) / (count - 1);
            var tops = new float[count];
            for (var i = 0; i < count; i++)
                tops[i] = float.NegativeInfinity;

            // The highest point of a triangle over a given X lies on one of its edges.
            for (var t = 0; t < triangles.Length; t += 3)
            {
                for (var e = 0; e < 3; e++)
                {
                    Vector3 p = vertices[triangles[t + e]], q = vertices[triangles[t + (e + 1) % 3]];
                    float lo = Mathf.Min(p.x, q.x), hi = Mathf.Max(p.x, q.x);
                    var first = Mathf.Max(0, Mathf.CeilToInt((lo - x0) / dx));
                    var last = Mathf.Min(count - 1, Mathf.FloorToInt((hi - x0) / dx));
                    for (var i = first; i <= last; i++)
                    {
                        var x = x0 + i * dx;
                        var y = hi - lo < 1e-6f ? Mathf.Max(p.y, q.y) : Mathf.Lerp(p.y, q.y, (x - p.x) / (q.x - p.x));
                        tops[i] = Mathf.Max(tops[i], y);
                    }
                }
            }

            var samples = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                if (float.IsNegativeInfinity(tops[i]))
                    throw new System.InvalidOperationException($"The greenhouse frame has no member above x = {x0 + i * dx:F2}.");
                samples[i] = new Vector2(x0 + i * dx, tops[i]);
            }

            // Douglas-Peucker: keep the samples that stray more than the tolerance from the line between kept ones.
            var keep = new bool[count];
            keep[0] = keep[count - 1] = true;
            var spans = new Stack<(int a, int b)>();
            spans.Push((0, count - 1));
            while (spans.Count > 0)
            {
                var (a, b) = spans.Pop();
                var worst = -1;
                var worstDistance = tolerance;
                for (var i = a + 1; i < b; i++)
                {
                    var along = Mathf.InverseLerp(samples[a].x, samples[b].x, samples[i].x);
                    var distance = Mathf.Abs(samples[i].y - Mathf.Lerp(samples[a].y, samples[b].y, along));
                    if (distance > worstDistance)
                    {
                        worst = i;
                        worstDistance = distance;
                    }
                }

                if (worst < 0)
                    continue;
                keep[worst] = true;
                spans.Push((a, worst));
                spans.Push((worst, b));
            }

            var corners = new List<Vector2>();
            for (var i = 0; i < count; i++)
            {
                if (keep[i])
                    corners.Add(samples[i]);
            }

            // Raise the reduced outline so that it clears every sample by at least the offset.
            var deficit = 0f;
            for (int i = 0, c = 0; i < count; i++)
            {
                while (c < corners.Count - 2 && samples[i].x > corners[c + 1].x)
                    c++;
                var along = Mathf.InverseLerp(corners[c].x, corners[c + 1].x, samples[i].x);
                deficit = Mathf.Max(deficit, samples[i].y - Mathf.Lerp(corners[c].y, corners[c + 1].y, along));
            }

            var outline = new Vector2[corners.Count];
            for (var i = 0; i < corners.Count; i++)
                outline[i] = corners[i] + Vector2.up * (CoverOffset + deficit);
            outline[0].x = bounds.min.x - CoverOffset;
            outline[outline.Length - 1].x = bounds.max.x + CoverOffset;
            return outline;
        }

        /// <summary>
        /// The frame mesh minus the parts that would stand inside the field once stretched over it: the white potting
        /// benches (pieces no higher than 1.5 m inside the side walls) and the three pairs of free-standing roof posts
        /// down the middle (they would land in the rows and the aisle). Walls, gable ends and roof trusses stay.
        /// Saved as Models/Generated/GreenhouseFrameShell.asset (rebuilt in place, same GUID).
        /// </summary>
        static Mesh FrameShell(Mesh source)
        {
            var vertices = source.vertices;
            var triangles = source.triangles;

            // Connected pieces: triangles joined through shared corners (welded by position, as the FBX splits normals).
            var parent = new int[vertices.Length];
            for (var i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Find(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }
            void Union(int a, int b) => parent[Find(a)] = Find(b);
            var byPosition = new Dictionary<Vector3Int, int>();
            for (var i = 0; i < vertices.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vertices[i] * 1000f);
                if (byPosition.TryGetValue(key, out var other))
                    Union(i, other);
                else
                    byPosition[key] = i;
            }

            for (var i = 0; i < triangles.Length; i += 3)
            {
                Union(triangles[i], triangles[i + 1]);
                Union(triangles[i], triangles[i + 2]);
            }

            var pieces = new Dictionary<int, Bounds>();
            foreach (var index in triangles)
            {
                var piece = Find(index);
                if (pieces.TryGetValue(piece, out var b))
                {
                    b.Encapsulate(vertices[index]);
                    pieces[piece] = b;
                }
                else
                {
                    pieces[piece] = new Bounds(vertices[index], Vector3.zero);
                }
            }

            var full = source.bounds;
            bool Interior(Bounds b)
            {
                var halfWidth = full.extents.x;
                var outerX = Mathf.Max(Mathf.Abs(b.min.x - full.center.x), Mathf.Abs(b.max.x - full.center.x));
                var insideEnds = b.min.z > full.min.z + 0.5f && b.max.z < full.max.z - 0.5f;
                var bench = b.max.y - full.min.y < 1.5f && outerX < halfWidth - 0.25f;
                var post = b.min.y - full.min.y < 0.1f && b.size.x < 0.3f && b.size.z < 0.3f && outerX < halfWidth - 2f;
                return insideEnds && (bench || post);
            }

            var kept = new List<int>(triangles.Length);
            for (var i = 0; i < triangles.Length; i += 3)
            {
                if (!Interior(pieces[Find(triangles[i])]))
                    kept.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
            }

            // Compact the vertices to the ones still referenced.
            var remap = new Dictionary<int, int>();
            var normals = source.normals;
            var uvs = source.uv;
            var tangents = source.tangents;
            var outVertices = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outUvs = new List<Vector2>();
            var outTangents = new List<Vector4>();
            for (var i = 0; i < kept.Count; i++)
            {
                if (!remap.TryGetValue(kept[i], out var mapped))
                {
                    mapped = remap[kept[i]] = outVertices.Count;
                    outVertices.Add(vertices[kept[i]]);
                    if (normals.Length > 0) outNormals.Add(normals[kept[i]]);
                    if (uvs.Length > 0) outUvs.Add(uvs[kept[i]]);
                    if (tangents.Length > 0) outTangents.Add(tangents[kept[i]]);
                }

                kept[i] = mapped;
            }

            const string name = "GreenhouseFrameShell";
            var mesh = new Mesh { name = name };
            mesh.SetVertices(outVertices);
            if (outNormals.Count > 0) mesh.SetNormals(outNormals);
            if (outUvs.Count > 0) mesh.SetUVs(0, outUvs);
            if (outTangents.Count > 0) mesh.SetTangents(outTangents);
            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();

            var path = $"{ProjectPaths.Meshes}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ProjectPaths.EnsureFolder(ProjectPaths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>The frame's local position and scale that map its mesh <paramref name="bounds"/> onto the footprint.</summary>
        internal static (Vector3 position, Vector3 scale) FitFrame(Bounds bounds)
        {
            var scale = new Vector3((FrameMaxX - FrameMinX) / bounds.size.x, FrameHeightScale, (FrameMaxZ - FrameMinZ) / bounds.size.z);
            var position = new Vector3(
                (FrameMinX + FrameMaxX) * 0.5f - bounds.center.x * scale.x,
                -bounds.min.y * scale.y,
                (FrameMinZ + FrameMaxZ) * 0.5f - bounds.center.z * scale.z);
            return (position, scale);
        }

        static Dictionary<string, Mesh> LoadMeshes(string path)
        {
            var meshes = new Dictionary<string, Mesh>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is Mesh mesh)
                    meshes[mesh.name] = mesh;
            }

            if (meshes.Count == 0)
                throw new System.IO.FileNotFoundException($"No meshes in {path}; run Tools/blender_greenhouse.py.");
            ConfigureModel(path);
            return meshes;
        }

        /// <summary>Static props: no animation, rig or embedded materials (the builder assigns its own).</summary>
        static void ConfigureModel(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!importer.importAnimation && importer.animationType == ModelImporterAnimationType.None &&
                importer.materialImportMode == ModelImporterMaterialImportMode.None)
                return;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        static void AddLods(GameObject host, params (GameObject renderer, float screenHeight)[] levels)
        {
            var lods = new LOD[levels.Length];
            for (var i = 0; i < levels.Length; i++)
                lods[i] = new LOD(levels[i].screenHeight, new Renderer[] { levels[i].renderer.GetComponent<Renderer>() });
            var group = host.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        // ------------------------------------------------------------------ bushes

        [System.Serializable] sealed class LayoutFile { public LayoutBush[] bushes; }
        [System.Serializable] sealed class LayoutBush { public string name; public float[] position; public float yaw; public float scale; }

        /// <summary>
        /// The asset's bush belts (from GreenhouseBushLayout.json, anchored where its greenhouse stood beyond the far
        /// fence), minus any whose canopy would come near the frame, then seeded clumps around the field on every side.
        /// The view south from spawn (road and hills) is kept open.
        /// </summary>
        static void BuildBushes(Transform root, Materials m)
        {
            var meshes = LoadMeshes(BushModel);
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(BushLayout);
            if (json == null)
                throw new System.IO.FileNotFoundException($"Missing {BushLayout}; run Tools/blender_greenhouse.py.");
            var layout = JsonUtility.FromJson<LayoutFile>(json.text);

            var placed = new List<(Vector2 position, float radius)>();
            var belts = EnvironmentKit.Group("Bushes", root).transform;
            var count = 0;
            void Place(Vector3 position, float yaw, float scale)
            {
                var bush = EnvironmentKit.Group($"Bush_{count++:00}", belts);
                bush.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                bush.transform.localScale = Vector3.one * scale;
                var near = BushRenderer("LOD0", bush.transform, meshes["Bush_LOD0"], m.Bush);
                var far = BushRenderer("LOD1", bush.transform, meshes["Bush_LOD1"], m.Bush);
                // Full clump within about 28 m (LOD bias 1), the 380-triangle card version beyond, culled when tiny.
                AddLods(bush, (near, 0.16f), (far, 0.008f));
                placed.Add((new Vector2(position.x, position.z), BushRadius * scale));
            }

            foreach (var b in layout.bushes)
            {
                var position = BushLayoutOrigin + new Vector3(b.position[0], b.position[1], b.position[2]);
                if (Allowed(position, BushRadius * b.scale, placed, 0f))
                    Place(position, b.yaw, b.scale);
            }

            var random = new System.Random(2026);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            // (x range, z range, clumps): denser belts beyond the far end and beside the field, then sparser clumps
            // further out. Relative to the frame, so they follow the field when it grows.
            var regions = new[]
            {
                (FrameMinX - 16f, FrameMinX + 1.5f, FrameMaxZ - 2f, FrameMaxZ + 44f, 14), (FrameMaxX - 1.5f, FrameMaxX + 16f, FrameMaxZ - 2f, FrameMaxZ + 44f, 14),
                (FrameMinX - 21f, FrameMinX - 1.5f, FrameMinZ - 2f, FrameMaxZ + 1f, 14), (FrameMaxX + 1.5f, FrameMaxX + 21f, FrameMinZ - 2f, FrameMaxZ + 1f, 14),
                (-70f, 70f, -58f, -27f, 10),
                (-90f, FrameMinX - 21f, -40f, FrameMaxZ + 70f, 7), (FrameMaxX + 21f, 90f, -40f, FrameMaxZ + 70f, 7),
                (-60f, 60f, FrameMaxZ + 40f, FrameMaxZ + 80f, 7),
            };
            foreach (var (xMin, xMax, zMin, zMax, target) in regions)
            {
                var added = 0;
                for (var attempt = 0; attempt < target * 60 && added < target; attempt++)
                {
                    var scale = Range(0.8f, 1.55f);
                    var position = new Vector3(Range(xMin, xMax), -0.23f, Range(zMin, zMax));
                    if (!Allowed(position, BushRadius * scale, placed, 0.25f))
                        continue;
                    Place(position, Range(0f, 360f), scale);
                    added++;
                }
            }
        }

        static GameObject BushRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            // Not batching-static: dozens of copies of one small mesh draw cheaply through the SRP Batcher,
            // without the memory of static-batched copies on Quest.
            var go = EnvironmentKit.MeshObject(name, parent, mesh, material, Vector3.zero, Quaternion.identity, Vector3.one, true, false);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
            return go;
        }

        /// <summary>Whether a clump of canopy <paramref name="radius"/> fits at <paramref name="position"/>.</summary>
        static bool Allowed(Vector3 position, float radius, List<(Vector2 position, float radius)> placed, float overlap)
        {
            float x = position.x, z = position.z;
            // Fully outside the greenhouse frame, with a margin so no canopy touches its walls or eaves.
            const float clearance = FrameBushClearance;
            if (x > FrameMinX - radius - clearance && x < FrameMaxX + radius + clearance &&
                z > FrameMinZ - radius - clearance && z < FrameMaxZ + radius + clearance)
                return false;
            // Off the road and its verges.
            if (Mathf.Abs(z - RoadZ) < RoadWidth * 0.5f + VergeWidth + radius)
                return false;
            // Behind the spawn, keep the view over the road to the hills open.
            if (z < FarmSceneBuilder.FenceMinZ && Mathf.Abs(x) < FarmSceneBuilder.FenceMaxX + 2f + radius)
                return false;
            foreach (var (other, otherRadius) in placed)
            {
                if (Vector2.Distance(new Vector2(x, z), other) < (radius + otherRadius) * (1f - overlap) * 0.8f)
                    return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ road

        static void BuildRoad(Transform root, Materials m)
        {
            var road = EnvironmentKit.Group("Road", root).transform;
            road.localPosition = new Vector3(0f, 0f, RoadZ);
            // One texture repeat per road width along the road, so the asphalt grain stays square.
            EnvironmentKit.MeshObject("Asphalt", road, ProceduralMeshes.GroundQuad("FarmRoad", new Vector2(RoadLength, RoadWidth),
                new Rect(0f, 0f, RoadLength / RoadWidth, 1f)), m.Road, Vector3.up * 0.02f, Quaternion.identity, Vector3.one);
            var verge = ProceduralMeshes.GroundQuad("FarmVerge", new Vector2(RoadLength, VergeWidth), new Rect(0f, 0f, 1f, 1f));
            foreach (var side in new[] { -1f, 1f })
                EnvironmentKit.MeshObject(side > 0 ? "Verge_North" : "Verge_South", road, verge, m.Verge,
                    new Vector3(0f, 0.012f, side * (RoadWidth + VergeWidth) * 0.5f), Quaternion.identity, Vector3.one);
        }

        static Transform Root(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root.transform;
            }

            throw new System.InvalidOperationException($"{scene.path} has no '{name}' root; rebuild it with BTP/Setup/4 - Build Farm Scene.");
        }
    }
}
