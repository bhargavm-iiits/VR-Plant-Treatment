using System.Collections.Generic;
using System.Linq;
using BTP.Farm;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// The Farm drip irrigation, modelled on an Indian polyhouse system, under one "Irrigation" root:
    ///   head unit right of the entrance: a black ribbed poly water tank on a concrete plinth; its outlet runs through
    ///   a screen filter, a blue PVC lever ball valve (lever along the pipe = open) and a pressure gauge, then drops to
    ///   the ground as the grey PVC main line (63 mm) along the right edge of the central aisle;
    ///   a 50 mm PVC header across each band ahead of its rows, with tees, elbows, couplings every 3 m and end caps;
    ///   a 16 mm black LDPE lateral along each of the 24 rows from a take-off connector on the header, lying slightly
    ///   wavy on the bed / ridge / strip, with an inline emitter at each plant and a folded end; micro-sprinklers on
    ///   stakes at the trees;
    ///   wet-soil patches spreading from every emitter, and shallow muddy standing water per row (low spots and edges
    ///   first, then the whole bed / furrow / basin);
    ///   a grey metal control box on a post with an LCD status line, a run lamp and one green START/STOP push button.
    /// One merged mesh per band for PVC, laterals, pools and wet soil. No colliders on pipes, water or wet soil, so they
    /// never block selection or teleport rays. FarmIrrigationController drives everything through shader globals,
    /// the valve lever, the gauge needle, the push button and the lamp.
    /// </summary>
    static class FarmIrrigationBuilder
    {
        public const string RootName = "Irrigation";
        const string MeshFolder = ProjectPaths.Meshes + "/Irrigation";

        static readonly Vector3 SpawnHead = new Vector3(0f, 1.6f, -6.5f);
        /// <summary>The control box: right of the entrance, beside the tank.</summary>
        internal static readonly Vector3 PanelPosition = new Vector3(3.05f, 0f, -4.4f);
        static readonly Vector3 TankPosition = new Vector3(4.3f, 0f, -3.5f);
        const float PlinthHeight = 0.3f, TankRadius = 0.55f;

        // Field soil top (FarmSceneBuilder: FieldSoil box at 0.002, 0.02 thick).
        const float Ground = 0.012f;
        /// <summary>The main line runs along +Z just right of the 2.4 m central aisle, clear of every bed and post.</summary>
        const float MainX = 1.45f, MainZ = -3.5f;
        /// <summary>Height of the head-unit line (tank outlet, filter, valve, gauge).</summary>
        const float HeadY = 0.42f;
        const float DropX = 2.7f, ValveX = 3.1f, FilterX = 3.42f, GaugeX = 2.9f;
        const float MainRadius = 0.0315f, HeaderRadius = 0.025f, LateralRadius = 0.008f;
        const float HeaderSetback = 0.2f;
        const float CouplingSpacing = 3f;

        [MenuItem("BTP/Setup/4b - Add Farm Irrigation (current Farm scene)", priority = 5)]
        public static void AddToFarmScene()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before adding the irrigation.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ProjectPaths.FarmScene)
            {
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new System.InvalidOperationException($"Save or discard the changes in {SceneManager.GetSceneAt(i).path} first.");
                }

                scene = EditorSceneManager.OpenScene(ProjectPaths.FarmScene, OpenSceneMode.Single);
            }

            foreach (var root in scene.GetRootGameObjects().Where(r => r.name == RootName).ToList())
                Object.DestroyImmediate(root);

            string M(string name) => $"{ProjectPaths.EnvironmentMaterials}/Farm_{name}.mat";
            var soil = AssetDatabase.LoadAssetAtPath<Material>(M("Soil"));
            var wood = AssetDatabase.LoadAssetAtPath<Material>(M("Wood"));
            var darkWood = AssetDatabase.LoadAssetAtPath<Material>(M("DarkWood"));
            if (soil == null || wood == null || darkWood == null)
                throw new System.InvalidOperationException("Farm materials missing; run BTP/Setup/4 - Build Farm Scene.");

            var controller = Build(soil, wood, darkWood);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Added irrigation to {ProjectPaths.FarmScene}: {controller.Rows.Count} rows, {controller.DripPoints.Count} emitters, " +
                      $"network {controller.NetworkLength:0.0} m.");
        }

        /// <summary>Builds the irrigation in the active scene (after the disease rows) and returns its controller.</summary>
        public static FarmIrrigationController Build(Material soil, Material wood, Material darkWood)
        {
            var root = new GameObject(RootName);
            var mats = CreateMaterials();
            RemoveOldDripLines();
            DeleteStaleMeshes();

            var layout = FarmDiseaseRows.Layout();
            var bands = layout.Select(r => r.ZStart).Distinct().OrderBy(z => z).ToList();
            var lastHeaderZ = bands.Max() - HeaderSetback;

            // ---- head unit line and main: tank wall -> filter -> valve -> gauge tee -> drop -> along the aisle.
            var pvc = new MeshData();
            var groundMainY = Ground + MainRadius;
            var tankWall = new Vector3(TankPosition.x - TankRadius + 0.01f, HeadY, MainZ);
            var dropTop = new Vector3(DropX, HeadY, MainZ);
            var dropBottom = new Vector3(DropX, groundMainY, MainZ);
            var corner = new Vector3(MainX, groundMainY, MainZ);
            var mainEnd = new Vector3(MainX, groundMainY, lastHeaderZ);
            var d = 0f;
            pvc.Pipe(tankWall, dropTop, MainRadius, ref d, CouplingSpacing);
            pvc.Elbow(dropTop, Vector3.right, Vector3.down, MainRadius);
            pvc.Pipe(dropTop, dropBottom, MainRadius, ref d, 0f);
            pvc.Elbow(dropBottom, Vector3.up, Vector3.left, MainRadius);
            pvc.Pipe(dropBottom, corner, MainRadius, ref d, CouplingSpacing);
            pvc.Elbow(corner, Vector3.right, Vector3.forward, MainRadius);
            var cornerDistance = d;
            pvc.Pipe(corner, mainEnd, MainRadius, ref d, CouplingSpacing);
            pvc.EndCap(mainEnd, Vector3.forward, MainRadius);
            // Tank connector and the gauge tee on the head line.
            pvc.Fitting(new Vector3(tankWall.x - 0.02f, HeadY, MainZ), Vector3.right, MainRadius * 1.35f, 0.05f);
            pvc.Fitting(new Vector3(GaugeX, HeadY, MainZ), Vector3.right, MainRadius * 1.25f, 0.09f);
            pvc.Fitting(new Vector3(GaugeX, HeadY + 0.04f, MainZ), Vector3.up, MainRadius * 0.9f, 0.05f);
            float MainDistance(float z) => cornerDistance + (z - MainZ);
            var networkLength = MainDistance(lastHeaderZ);

            var rows = new List<FarmIrrigationController.Row>();
            var drips = new List<FarmIrrigationController.DripPoint>();
            var pipeRenderers = new List<Renderer>();
            var poolRenderers = new List<Renderer>();
            var pipes = EnvironmentKit.Group("Pipes", root.transform).transform;
            var water = EnvironmentKit.Group("Water", root.transform).transform;

            for (var b = 0; b < bands.Count; b++)
            {
                var bandRows = layout.Where(r => Mathf.Approximately(r.ZStart, bands[b])).ToList();
                var headerZ = bands[b] - HeaderSetback;
                var junction = MainDistance(headerZ);
                var header = b == 0 ? pvc : new MeshData();
                var laterals = new MeshData();
                var pool = new MeshData();
                var wet = new MeshData();
                var headerY = Ground + HeaderRadius;

                // Header: a tee on the main, then either way to the outermost lateral.
                var lateralXs = bandRows.Select(r => r.X + LateralOffset(r.Kind)).ToList();
                var minX = lateralXs.Min() - 0.15f;
                var maxX = lateralXs.Max() + 0.15f;
                var tee = new Vector3(MainX, headerY, headerZ);
                header.Fitting(new Vector3(MainX, groundMainY, headerZ), Vector3.forward, MainRadius * 1.25f, 0.16f);
                foreach (var end in new[] { minX, maxX })
                {
                    if (Mathf.Abs(end - MainX) < 0.2f)
                        continue;
                    var dir = end < MainX ? Vector3.left : Vector3.right;
                    var start = tee + dir * (MainRadius * 1.3f);
                    var tip = new Vector3(end, headerY, headerZ);
                    var hd = junction;
                    header.Fitting(start + dir * 0.03f, dir, HeaderRadius * 1.25f, 0.06f);
                    header.Pipe(start, tip, HeaderRadius, ref hd, CouplingSpacing);
                    header.EndCap(tip, dir, HeaderRadius);
                    networkLength = Mathf.Max(networkLength, hd);
                }

                foreach (var row in bandRows)
                {
                    var data = BuildRow(row, laterals, pool, wet, drips, junction, headerZ, headerY, ref networkLength);
                    rows.Add(data);
                }

                if (b > 0)
                    pipeRenderers.Add(AddMesh($"PVC_Band{b + 1}", pipes, header, mats.Pvc));
                pipeRenderers.Add(AddMesh($"Laterals_Band{b + 1}", pipes, laterals, mats.Ldpe));
                poolRenderers.Add(AddMesh($"Pools_Band{b + 1}", water, pool, mats.Water, 0.08f));
                AddMesh($"WetSoil_Band{b + 1}", water, wet, mats.WetSoil);
            }

            pipeRenderers.Insert(0, AddMesh("PVC_Main", pipes, pvc, mats.Pvc));

            var (valveLever, gaugeNeedle) = BuildHeadUnit(root.transform, mats);
            var (dripSystem, mistSystem) = BuildParticles(root.transform, mats);
            var (button, label, status, pushCap, lamp) = BuildControlBox(root.transform, mats);

            var controller = root.AddComponent<FarmIrrigationController>();
            controller.Bind(button, label, status, null, dripSystem, pipeRenderers, poolRenderers, rows, drips, networkLength);
            controller.BindHardware(valveLever, gaugeNeedle, pushCap, lamp, mistSystem);
            return controller;
        }

        // ------------------------------------------------------------------ rows

        static float LateralOffset(FarmDiseaseRows.Kind kind) => kind switch
        {
            FarmDiseaseRows.Kind.Bed => 0.25f,       // beside the plants, clear of the stakes
            FarmDiseaseRows.Kind.Ridge => 0.12f,
            FarmDiseaseRows.Kind.Pergola => 0.25f,
            _ => 0.5f,                               // trees: along the strip, sprinklers towards the trunks
        };

        /// <summary>Height of the surface the lateral lies on.</summary>
        static float SurfaceTop(FarmDiseaseRows.Kind kind) => kind switch
        {
            FarmDiseaseRows.Kind.Bed => FarmDiseaseRows.BedHeight,
            FarmDiseaseRows.Kind.Ridge => FarmDiseaseRows.RidgeHeight,
            FarmDiseaseRows.Kind.Pergola => 0.14f,   // mulch top on the pergola ridge
            _ => 0.03f,                              // tree strip top
        };

        /// <summary>Half width of the surface the wet patches lie on (bed, ridge, mulch, strip).</summary>
        static float SurfaceHalfWidth(FarmDiseaseRows.Kind kind) => kind switch
        {
            FarmDiseaseRows.Kind.Bed => FarmDiseaseRows.BedWidth * 0.5f - 0.02f,
            FarmDiseaseRows.Kind.Ridge => 0.33f,
            FarmDiseaseRows.Kind.Pergola => 0.44f,
            _ => 0.88f,
        };

        static FarmIrrigationController.Row BuildRow(FarmDiseaseRows.Row row, MeshData laterals, MeshData pool, MeshData wet,
            List<FarmIrrigationController.DripPoint> drips, float junction, float headerZ, float headerY, ref float networkLength)
        {
            var seed = (row.X * 3.7f + row.ZStart) % 6.28f;
            var xb = row.X + LateralOffset(row.Kind);
            float Wave(float z) => xb + 0.018f * Mathf.Sin(z * 1.9f + seed) + 0.007f * Mathf.Sin(z * 5.3f + seed * 2f);
            var top = SurfaceTop(row.Kind);
            var lowY = Ground + LateralRadius;
            var topY = top + LateralRadius;

            // Take-off connector on the header, then the lateral along the ground to the bed and up over its edge.
            var d = junction + Mathf.Abs(xb - MainX);
            var takeOff = new Vector3(xb, headerY, headerZ + HeaderRadius + 0.012f);
            laterals.Fitting(takeOff, Vector3.forward, LateralRadius * 1.35f, 0.03f, 6);
            var path = new List<Vector3> { takeOff + new Vector3(0f, 0f, 0.012f) };
            var front = row.ZStart - 0.05f;
            if (topY - lowY > 0.03f)
            {
                path.Add(new Vector3(xb, lowY, front - 0.12f));
                path.Add(new Vector3(xb, Mathf.Lerp(lowY, topY, 0.65f), front - 0.025f));
                path.Add(new Vector3(Wave(front + 0.04f), topY, front + 0.04f));
            }
            else
            {
                path.Add(new Vector3(xb, Mathf.Max(lowY, topY), front + 0.04f));
            }

            for (var i = 1; i < path.Count; i++)
                laterals.Pipe(path[i - 1], path[i], LateralRadius, ref d, 0f, 6);
            var arrival = d;

            // Along the row, slightly wavy, with an inline emitter at each plant and a folded end.
            var startZ = path[path.Count - 1].z;
            var endZ = row.ZEnd - 0.15f;
            var previous = path[path.Count - 1];
            for (var z = startZ + 0.5f; ; z += 0.5f)
            {
                var last = z >= endZ;
                var point = new Vector3(Wave(Mathf.Min(z, endZ)), topY, Mathf.Min(z, endZ));
                laterals.Pipe(previous, point, LateralRadius, ref d, 0f, 6);
                previous = point;
                if (last)
                    break;
            }

            var fold = previous + new Vector3(0f, LateralRadius * 2f, 0.03f);
            laterals.Pipe(previous, fold, LateralRadius, ref d, 0f, 6);
            laterals.Pipe(fold, fold + new Vector3(0f, 0f, -0.09f), LateralRadius, ref d, 0f, 6);
            laterals.Fitting(previous + new Vector3(0f, LateralRadius, -0.06f), Vector3.forward, LateralRadius * 2.4f, 0.012f, 6);
            networkLength = Mathf.Max(networkLength, d);

            // Emitters: one per plant, two per pergola module, a sprinkler per tree.
            var emitterZs = new List<float>();
            if (row.Kind == FarmDiseaseRows.Kind.Pergola)
            {
                for (var i = 0; i < row.Count; i++)
                {
                    emitterZs.Add(row.PlantPosition(i).z - 0.8f);
                    emitterZs.Add(row.PlantPosition(i).z + 0.8f);
                }
            }
            else
            {
                for (var i = 0; i < row.Count; i++)
                    emitterZs.Add(row.PlantPosition(i).z);
            }

            var tree = row.Kind == FarmDiseaseRows.Kind.Tree;
            var emitterX = tree ? xb - 0.12f : xb;
            foreach (var z in emitterZs)
            {
                var x = Wave(z);
                if (tree)
                {
                    // Micro-sprinkler on a thin stake beside the lateral, fed by a short micro-tube.
                    var stakeBase = new Vector3(emitterX, top, z);
                    laterals.Fitting(stakeBase + Vector3.up * 0.13f, Vector3.up, 0.004f, 0.26f, 6);
                    laterals.Fitting(stakeBase + Vector3.up * 0.27f, Vector3.up, 0.011f, 0.025f, 6);
                    var dd = 0f;
                    laterals.Pipe(new Vector3(x, topY, z), stakeBase + new Vector3(0.006f, 0.24f, 0.004f), 0.0025f, ref dd, 0f, 4);
                    drips.Add(new FarmIrrigationController.DripPoint
                    {
                        position = stakeBase + Vector3.up * 0.29f,
                        velocity = Vector3.up,
                        distance = arrival + (z - row.ZStart),
                        spray = true,
                    });
                }
                else
                {
                    laterals.Fitting(new Vector3(x, topY, z), Vector3.forward, LateralRadius * 1.4f, 0.035f, 6);
                    drips.Add(new FarmIrrigationController.DripPoint
                    {
                        position = new Vector3(x - LateralRadius * 0.6f, topY - LateralRadius * 0.4f, z),
                        velocity = new Vector3(0f, -0.02f, 0f),
                        distance = arrival + (z - row.ZStart),
                        spray = false,
                    });
                }
            }

            // Wet soil spreading from the emitters, on the bed / ridge / mulch / strip surface.
            var spacing = emitterZs.Count > 1 ? emitterZs[1] - emitterZs[0] : 1f;
            var (radius, stretch) = row.Kind switch
            {
                FarmDiseaseRows.Kind.Bed => (0.45f, 1.6f),
                FarmDiseaseRows.Kind.Ridge => (0.3f, 1.3f),
                FarmDiseaseRows.Kind.Pergola => (0.42f, 1.3f),
                _ => (0.75f, 1f),
            };
            var halfWidth = SurfaceHalfWidth(row.Kind);
            var minX = Mathf.Max(row.X - halfWidth, emitterX - radius * 1.2f);
            var maxX = Mathf.Min(row.X + halfWidth, emitterX + radius * 1.2f);
            var minZ = Mathf.Max(row.ZStart, emitterZs[0] - radius * stretch * 1.2f);
            var maxZ = Mathf.Min(row.ZEnd, emitterZs[emitterZs.Count - 1] + radius * stretch * 1.2f);
            wet.WetQuad(top + 0.002f, minX, maxX, minZ, maxZ, emitterX, emitterZs[0], arrival + (emitterZs[0] - row.ZStart), spacing,
                radius, emitterZs.Count, stretch);

            var data = AddPool(pool, row, arrival);
            data.id = $"{row.PlantId}_{row.DiseaseId}";
            data.branchLength = d - (junction + Mathf.Abs(xb - MainX));
            return data;
        }

        /// <summary>The row's standing water: thin over the bed top, or in the furrows / basin at ground level.</summary>
        static FarmIrrigationController.Row AddPool(MeshData pool, FarmDiseaseRows.Row row, float arrival)
        {
            var centreZ = (row.ZStart + row.ZEnd) * 0.5f;
            float width, length, baseY, depth, shore;
            switch (row.Kind)
            {
                case FarmDiseaseRows.Kind.Bed:
                    width = FarmDiseaseRows.BedWidth - 0.03f; length = row.Length + 0.07f;
                    baseY = FarmDiseaseRows.BedHeight - 0.004f; depth = 0.013f; shore = 0.04f;
                    break;
                case FarmDiseaseRows.Kind.Ridge:
                    // Flooded furrows either side of the corn ridge (0.08 m high).
                    width = 1.7f; length = row.Length + 0.1f; baseY = -0.002f; depth = 0.045f; shore = 0.12f;
                    break;
                case FarmDiseaseRows.Kind.Pergola:
                    // Flooded furrows either side of the mulched vine ridge (0.14 m), inside the pergola posts.
                    width = 2.4f; length = row.Length; baseY = -0.002f; depth = 0.05f; shore = 0.15f;
                    break;
                default:
                    // A shallow basin along the tree strip (top 0.03 m).
                    width = 1.7f; length = row.Length; baseY = 0.026f; depth = 0.03f; shore = 0.15f;
                    break;
            }

            pool.Pool(new Vector3(row.X, baseY, centreZ), new Vector2(width, length), shore, arrival, depth);
            return new FarmIrrigationController.Row
            {
                arrival = arrival,
                poolCentre = new Vector3(row.X, baseY + depth, centreZ),
                poolSize = new Vector2(width, length),
            };
        }

        // ------------------------------------------------------------------ materials

        sealed class Mats
        {
            public Material Pvc, Ldpe, Tank, Concrete, Filter, Valve, Steel, Brass, PanelGrey, ButtonGreen, Black;
            public Material Water, WetSoil, Drop, Mist, Lamp, GaugeFace;
        }

        static Material Plastic(string name, Color color, float smoothness, float dust, float dustHeight)
        {
            var material = MaterialFactory.GetOrCreate($"{ProjectPaths.EnvironmentMaterials}/Farm_Irrigation{name}.mat", "BTP/Irrigation Pipe");
            material.SetColor("_BaseColor", color);
            material.SetColor("_DustColor", new Color(0.47f, 0.39f, 0.30f));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_DustAmount", dust);
            material.SetFloat("_DustHeight", dustHeight);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mats CreateMaterials()
        {
            string M(string name) => $"{ProjectPaths.EnvironmentMaterials}/Farm_Irrigation{name}.mat";
            var water = MaterialFactory.GetOrCreate(M("Water"), "BTP/Irrigation Water");
            water.SetColor("_BaseColor", new Color(0.36f, 0.29f, 0.20f, 0.22f));
            water.SetColor("_DeepColor", new Color(0.12f, 0.09f, 0.06f, 0.8f));
            water.SetFloat("_Smoothness", 0.95f);
            water.enableInstancing = true;
            EditorUtility.SetDirty(water);

            var wetSoil = MaterialFactory.GetOrCreate(M("WetSoil"), "BTP/Irrigation Wet Soil");
            wetSoil.SetColor("_BaseColor", new Color(0.07f, 0.05f, 0.035f, 0.62f));
            wetSoil.SetFloat("_Smoothness", 0.6f);
            wetSoil.SetFloat("_WetSpan", 40f);
            wetSoil.enableInstancing = true;
            EditorUtility.SetDirty(wetSoil);

            var drop = MaterialFactory.Glow(M("Drop"), new Color(0.80f, 0.84f, 0.86f, 0.55f), 1f);
            drop.SetTexture("_BaseMap", DropTexture());
            var mist = MaterialFactory.Glow(M("Mist"), new Color(0.88f, 0.91f, 0.93f, 0.28f), 1f);
            mist.SetTexture("_BaseMap", DropTexture());
            EditorUtility.SetDirty(drop);
            EditorUtility.SetDirty(mist);

            var gaugeFace = MaterialFactory.Solid(M("GaugeFace"), Color.white, 0.5f, GaugeTexture());

            return new Mats
            {
                Pvc = Plastic("PVC", new Color(0.66f, 0.67f, 0.65f), 0.22f, 0.65f, 0.16f),
                Ldpe = Plastic("LDPE", new Color(0.035f, 0.035f, 0.035f), 0.32f, 0.45f, 0.3f),
                Tank = Plastic("Tank", new Color(0.065f, 0.07f, 0.065f), 0.28f, 0.7f, 0.95f),
                Concrete = Plastic("Concrete", new Color(0.53f, 0.51f, 0.47f), 0.08f, 0.5f, 0.35f),
                Filter = Plastic("Filter", new Color(0.09f, 0.09f, 0.09f), 0.3f, 0.4f, 0.6f),
                Valve = Plastic("Valve", new Color(0.14f, 0.23f, 0.38f), 0.35f, 0.3f, 0.6f),
                Steel = Plastic("Steel", new Color(0.58f, 0.59f, 0.6f), 0.5f, 0.35f, 0.3f),
                Brass = Plastic("Brass", new Color(0.55f, 0.45f, 0.26f), 0.5f, 0.2f, 0.3f),
                PanelGrey = Plastic("Panel", new Color(0.70f, 0.71f, 0.69f), 0.3f, 0.25f, 0.9f),
                ButtonGreen = Plastic("Button", new Color(0.10f, 0.42f, 0.17f), 0.55f, 0f, 0.1f),
                Black = Plastic("Rubber", new Color(0.05f, 0.05f, 0.05f), 0.2f, 0f, 0.1f),
                Water = water,
                WetSoil = wetSoil,
                Drop = drop,
                Mist = mist,
                Lamp = MaterialFactory.Glow(M("Lamp"), new Color(0.06f, 0.1f, 0.06f, 1f), 1.4f),
                GaugeFace = gaugeFace,
            };
        }

        /// <summary>A 32 px soft round drop (alpha) for the drip and mist particles.</summary>
        static Texture2D DropTexture()
        {
            var path = $"{MeshFolder}/IrrigationDrop.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
                return texture;
            ProjectPaths.EnsureFolder(MeshFolder);
            const int size = 32;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "IrrigationDrop", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size * 2f - 1f;
                var dy = (y + 0.5f) / size * 2f - 1f;
                var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, a * 1.6f) * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        /// <summary>Pressure gauge dial, 0-4 bar over 270 degrees clockwise from lower left, red above 3.2 bar.</summary>
        static Texture2D GaugeTexture()
        {
            var path = $"{MeshFolder}/IrrigationGaugeFace.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            ProjectPaths.EnsureFolder(MeshFolder);
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "IrrigationGaugeFace", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                var r = Mathf.Sqrt(u * u + v * v);
                // Clockwise angle from straight up, -180..180.
                var angle = Mathf.Atan2(u, v) * Mathf.Rad2Deg;
                Color c = new Color(0.93f, 0.92f, 0.88f);
                if (r > 0.9f)
                    c = new Color(0.12f, 0.12f, 0.12f);
                else if (Mathf.Abs(angle) <= 135f)
                {
                    var t = (angle + 135f) / 270f;          // 0..1 = 0..4 bar
                    var minor = Mathf.Abs(t * 20f - Mathf.Round(t * 20f)) < 0.06f;
                    var major = Mathf.Abs(t * 4f - Mathf.Round(t * 4f)) < 0.012f;
                    if (r > 0.62f && r < 0.84f && major)
                        c = Color.black;
                    else if (r > 0.72f && r < 0.84f && minor)
                        c = new Color(0.15f, 0.15f, 0.15f);
                    else if (r > 0.80f && r < 0.86f)
                        c = t > 0.8f ? new Color(0.75f, 0.12f, 0.1f) : new Color(0.2f, 0.2f, 0.2f);
                }

                if (r < 0.07f)
                    c = new Color(0.1f, 0.1f, 0.1f);
                pixels[y * size + x] = c;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        // ------------------------------------------------------------------ head unit

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material,
            Quaternion? rotation = null, bool shadows = true)
        {
            var go = EnvironmentKit.Primitive(type, name, parent, localPosition, scale, material, false, rotation ?? Quaternion.identity, shadows);
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            return go;
        }

        static GameObject Cyl(string name, Transform parent, Vector3 centre, Vector3 axis, float radius, float length, Material material, bool shadows = false) =>
            Part(PrimitiveType.Cylinder, name, parent, centre, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material,
                Quaternion.FromToRotation(Vector3.up, axis), shadows);

        /// <summary>Tank, plinth, filter, ball valve and pressure gauge; returns the valve lever pivot and gauge needle pivot.</summary>
        static (Transform lever, Transform needle) BuildHeadUnit(Transform root, Mats m)
        {
            var unit = EnvironmentKit.Group("HeadUnit", root).transform;

            // Concrete plinth and the ribbed poly tank (Sintex style), with its lid.
            Part(PrimitiveType.Cube, "Plinth", unit, new Vector3(TankPosition.x, PlinthHeight * 0.5f, TankPosition.z),
                new Vector3(1.4f, PlinthHeight, 1.4f), m.Concrete);
            var tankMesh = SaveMesh("IrrigationTank", TankProfile());
            var tank = EnvironmentKit.MeshObject("Tank", unit, tankMesh, m.Tank, new Vector3(TankPosition.x, PlinthHeight, TankPosition.z),
                Quaternion.identity, Vector3.one, true, false);
            tank.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            // Collider so the tank stops the teleport ray like the other solid objects.
            var tankCollider = tank.AddComponent<CapsuleCollider>();
            tankCollider.radius = TankRadius;
            tankCollider.height = 1.5f;
            tankCollider.center = new Vector3(0f, 0.75f, 0f);

            // Screen filter (horizontal body, grey end cap, clamp ring, drain plug).
            Cyl("FilterBody", unit, new Vector3(FilterX, HeadY, MainZ), Vector3.right, 0.062f, 0.2f, m.Filter, true);
            Cyl("FilterCap", unit, new Vector3(FilterX - 0.115f, HeadY, MainZ), Vector3.right, 0.066f, 0.035f, m.Pvc);
            Cyl("FilterClamp", unit, new Vector3(FilterX - 0.09f, HeadY, MainZ), Vector3.right, 0.068f, 0.014f, m.Steel);
            Cyl("FilterDrain", unit, new Vector3(FilterX + 0.02f, HeadY - 0.07f, MainZ), Vector3.up, 0.012f, 0.03f, m.Pvc);

            // Lever ball valve: blue body, sockets, stem and a black lever on a pivot (along the pipe = open).
            Cyl("ValveBody", unit, new Vector3(ValveX, HeadY, MainZ), Vector3.right, 0.045f, 0.075f, m.Valve, true);
            Cyl("ValveSocketA", unit, new Vector3(ValveX - 0.06f, HeadY, MainZ), Vector3.right, 0.038f, 0.05f, m.Valve);
            Cyl("ValveSocketB", unit, new Vector3(ValveX + 0.06f, HeadY, MainZ), Vector3.right, 0.038f, 0.05f, m.Valve);
            Cyl("ValveBonnet", unit, new Vector3(ValveX, HeadY + 0.045f, MainZ), Vector3.up, 0.022f, 0.025f, m.Valve);
            var pivot = new GameObject("ValveLever").transform;
            pivot.SetParent(unit, false);
            pivot.localPosition = new Vector3(ValveX, HeadY + 0.062f, MainZ);
            pivot.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Part(PrimitiveType.Cube, "Lever", pivot, new Vector3(-0.055f, 0.004f, 0f), new Vector3(0.13f, 0.01f, 0.024f), m.Black);
            Cyl("LeverHub", pivot, Vector3.zero, Vector3.up, 0.015f, 0.014f, m.Black);

            // Pressure gauge on a brass nipple above the tee, its dial turned towards the entrance.
            Cyl("GaugeNipple", unit, new Vector3(GaugeX, HeadY + 0.09f, MainZ), Vector3.up, 0.008f, 0.07f, m.Brass);
            var gauge = new GameObject("PressureGauge").transform;
            gauge.SetParent(unit, false);
            var gaugeCentre = new Vector3(GaugeX, HeadY + 0.17f, MainZ);
            gauge.SetLocalPositionAndRotation(gaugeCentre, Quaternion.LookRotation(
                Vector3.ProjectOnPlane(gaugeCentre - SpawnHead, Vector3.up).normalized, Vector3.up));
            Cyl("Case", gauge, Vector3.zero, Vector3.forward, 0.05f, 0.035f, m.Steel, true);
            Part(PrimitiveType.Quad, "Dial", gauge, new Vector3(0f, 0f, -0.0181f), Vector3.one * 0.092f, m.GaugeFace, null, false);
            var needle = new GameObject("Needle").transform;
            needle.SetParent(gauge, false);
            needle.localPosition = new Vector3(0f, 0f, -0.0195f);
            needle.localRotation = Quaternion.Euler(0f, 0f, -135f);
            Part(PrimitiveType.Cube, "NeedleBar", needle, new Vector3(0f, 0.016f, 0f), new Vector3(0.003f, 0.04f, 0.0015f), m.Black, null, false);
            return (pivot, needle);
        }

        /// <summary>Ribbed poly water tank (about 1000 L): a lathe profile with horizontal ribs, domed shoulder and lid.</summary>
        static MeshData TankProfile()
        {
            var profile = new List<Vector2> { new Vector2(0.50f, 0f), new Vector2(0.545f, 0.03f), new Vector2(0.55f, 0.08f) };
            for (var k = 0; k < 6; k++)
            {
                var y = 0.12f + k * 0.17f;
                profile.Add(new Vector2(0.55f, y));
                profile.Add(new Vector2(0.568f, y + 0.025f));
                profile.Add(new Vector2(0.568f, y + 0.075f));
                profile.Add(new Vector2(0.55f, y + 0.10f));
            }

            profile.AddRange(new[]
            {
                new Vector2(0.55f, 1.18f), new Vector2(0.53f, 1.25f), new Vector2(0.46f, 1.33f), new Vector2(0.33f, 1.38f),
                new Vector2(0.25f, 1.39f), new Vector2(0.25f, 1.40f), new Vector2(0.26f, 1.40f), new Vector2(0.26f, 1.46f),
                new Vector2(0.20f, 1.48f), new Vector2(0.0f, 1.49f),
            });
            var mesh = new MeshData();
            mesh.Lathe(profile, 36);
            return mesh;
        }

        // ------------------------------------------------------------------ particles

        static (ParticleSystem drips, ParticleSystem mist) BuildParticles(Transform root, Mats m)
        {
            ParticleSystem System(string name, Material material, float gravity, int max, AnimationCurve size)
            {
                var go = new GameObject(name, typeof(ParticleSystem));
                go.transform.SetParent(root, false);
                var ps = go.GetComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.duration = 5f;
                main.startLifetime = 0.5f;
                main.startSpeed = 0f;
                main.startSize = 0.01f;
                main.gravityModifier = gravity;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = max;
                var emission = ps.emission;
                emission.enabled = false;
                var shape = ps.shape;
                shape.enabled = false;
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, size);
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return ps;
            }

            // Drops swell at the emitter and slide off slowly; the mist arcs low and fades.
            var drips = System("Drips", m.Drop, 0.12f, 600, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));
            var mist = System("Mist", m.Mist, 0.9f, 900, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));
            return (drips, mist);
        }

        // ------------------------------------------------------------------ control box

        /// <summary>
        /// Grey metal control box on a galvanised post: name plate, LCD status, run lamp and a green START/STOP push
        /// button. The 3D cap is pressed through an invisible UI button over it (XR ray, poke and mouse).
        /// </summary>
        static (Button button, TMP_Text label, TMP_Text status, Transform cap, Renderer lamp) BuildControlBox(Transform root, Mats m)
        {
            var panel = EnvironmentKit.Group("ControlBox", root).transform;
            panel.position = PanelPosition;
            panel.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(PanelPosition + Vector3.up * 1.25f - SpawnHead, Vector3.up).normalized, Vector3.up);

            var post = Cyl("Post", panel, new Vector3(0f, 0.6f, 0.1f), Vector3.up, 0.03f, 1.2f, m.Steel, true);
            post.AddComponent<CapsuleCollider>();
            Part(PrimitiveType.Cube, "Bracket", panel, new Vector3(0f, 1.25f, 0.08f), new Vector3(0.06f, 0.3f, 0.04f), m.Steel);
            Cyl("Conduit", panel, new Vector3(0.09f, 0.52f, 0.02f), Vector3.up, 0.012f, 1.04f, m.Pvc);

            var box = EnvironmentKit.Group("Box", panel).transform;
            box.localPosition = new Vector3(0f, 1.25f, 0f);
            Part(PrimitiveType.Cube, "Enclosure", box, Vector3.zero, new Vector3(0.30f, 0.40f, 0.14f), m.PanelGrey, null, true);
            Part(PrimitiveType.Cube, "Door", box, new Vector3(0f, -0.005f, -0.0715f), new Vector3(0.28f, 0.37f, 0.003f), m.PanelGrey);
            Part(PrimitiveType.Cube, "RainHood", box, new Vector3(0f, 0.207f, -0.015f), new Vector3(0.33f, 0.014f, 0.19f), m.PanelGrey, Quaternion.Euler(-6f, 0f, 0f));
            foreach (var y in new[] { 0.12f, -0.12f })
                Cyl("Hinge", box, new Vector3(-0.15f, y, -0.072f), Vector3.up, 0.006f, 0.04f, m.Steel);
            Cyl("Lock", box, new Vector3(0.125f, 0f, -0.074f), Vector3.forward, 0.008f, 0.006f, m.Black);
            Part(PrimitiveType.Cube, "NamePlate", box, new Vector3(0f, 0.148f, -0.0735f), new Vector3(0.22f, 0.04f, 0.002f), m.Black);
            Part(PrimitiveType.Cube, "LcdBezel", box, new Vector3(0f, 0.075f, -0.0738f), new Vector3(0.2f, 0.06f, 0.0026f), m.Black);
            var lcd = MaterialFactory.Unlit($"{ProjectPaths.EnvironmentMaterials}/Farm_IrrigationLcd.mat", new Color(0.13f, 0.19f, 0.12f));
            Part(PrimitiveType.Quad, "LcdGlass", box, new Vector3(0f, 0.075f, -0.0753f), new Vector3(0.18f, 0.045f, 1f), lcd, null, false);

            // Run lamp (left) and the push button (right), each in a black bezel.
            var lampPos = new Vector3(-0.075f, -0.035f, -0.074f);
            Cyl("LampBezel", box, lampPos, Vector3.forward, 0.024f, 0.008f, m.Black);
            var lamp = Part(PrimitiveType.Sphere, "Lamp", box, lampPos + new Vector3(0f, 0f, -0.004f), new Vector3(0.03f, 0.03f, 0.02f), m.Lamp, null, false)
                .GetComponent<Renderer>();
            var buttonPos = new Vector3(0.045f, -0.04f, -0.0725f);
            Cyl("ButtonBezel", box, buttonPos + new Vector3(0f, 0f, -0.006f), Vector3.forward, 0.045f, 0.012f, m.Black);
            var cap = new GameObject("PushButton").transform;
            cap.SetParent(box, false);
            cap.localPosition = buttonPos + new Vector3(0f, 0f, -0.012f);
            Cyl("Plunger", cap, new Vector3(0f, 0f, -0.006f), Vector3.forward, 0.03f, 0.014f, m.ButtonGreen, true);
            Cyl("Cap", cap, new Vector3(0f, 0f, -0.015f), Vector3.forward, 0.036f, 0.008f, m.ButtonGreen, true);

            // Canvas on the door: plate text, LCD text, labels and the invisible hit area over the push button.
            var canvas = UIFactory.WorldCanvas("PanelUI", box, new Vector2(300f, 400f), new Vector3(0f, 0f, -0.0762f), Quaternion.identity);
            var rect = (RectTransform)canvas.transform;
            canvas.gameObject.AddComponent<FarmConsolePressGuard>();
            var plate = UIFactory.Text("Plate", rect, "IRRIGATION CONTROL", 17f, new Color(0.86f, 0.86f, 0.82f), TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Place(plate.rectTransform, 40f, 33f, 220f, 38f);
            var status = UIFactory.Text("Lcd", rect, "OFF", 22f, new Color(0.62f, 0.86f, 0.52f), TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            status.textWrappingMode = TextWrappingModes.NoWrap;
            UIFactory.Place(status.rectTransform, 64f, 106f, 172f, 40f);
            UIFactory.Place(UIFactory.Text("RunLabel", rect, "RUN", 14f, new Color(0.15f, 0.15f, 0.15f), TextAlignmentOptions.Center, FontStyles.Bold).rectTransform,
                40f, 270f, 70f, 22f);
            var label = UIFactory.Text("ButtonLabel", rect, "PRESS TO START", 14f, new Color(0.15f, 0.15f, 0.15f), TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Place(label.rectTransform, 120f, 290f, 150f, 22f);
            UIFactory.Place(UIFactory.Text("Warning", rect, "230 V  •  PUMP / VALVE", 11f, new Color(0.25f, 0.25f, 0.25f), TextAlignmentOptions.Center).rectTransform,
                40f, 345f, 220f, 20f);

            var hit = new GameObject("StartStop", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            hit.transform.SetParent(rect, false);
            var hitImage = hit.GetComponent<Image>();
            hitImage.color = new Color(1f, 1f, 1f, 0f);
            hit.targetGraphic = hitImage;
            hit.transition = Selectable.Transition.None;
            hit.navigation = new Navigation { mode = Navigation.Mode.None };
            UIFactory.Place((RectTransform)hit.transform, 150f + buttonPos.x * 1000f - 50f, 200f - buttonPos.y * 1000f - 50f, 100f, 100f);
            hit.gameObject.AddComponent<FarmConsolePressGuard>();
            return (hit, label, status, cap, lamp);
        }

        /// <summary>The beds' old black drip lines (the laterals replace them), and the pergola modules' own.</summary>
        static void RemoveOldDripLines()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var t in go.GetComponentsInChildren<Transform>(true).ToList())
                {
                    if (t == null)
                        continue;
                    if (t.name.StartsWith("Bed_") && t.name.EndsWith("_DripLine"))
                        Object.DestroyImmediate(t.gameObject);
                    else if (t.name == "DripLine" && t.GetComponent<Renderer>() is { } r && t.GetComponentInParent<SelectablePlant>() != null)
                        r.enabled = false;
                }
            }
        }

        /// <summary>Meshes of the first irrigation version that are no longer built.</summary>
        static void DeleteStaleMeshes()
        {
            foreach (var name in new[] { "IrrigationBunds_Band1", "IrrigationBunds_Band2", "IrrigationPipes_Main", "IrrigationPipes_Band1",
                         "IrrigationPipes_Band2", "IrrigationPipes_Band3" })
            {
                var path = $"{MeshFolder}/{name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
        }

        // ------------------------------------------------------------------ meshes

        static Renderer AddMesh(string name, Transform parent, MeshData data, Material material, float raise = 0f)
        {
            var mesh = SaveMesh($"Irrigation{name}", data, raise);
            var renderer = EnvironmentKit.MeshObject(name, parent, mesh, material, Vector3.zero, Quaternion.identity, Vector3.one, false, false)
                .GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return renderer;
        }

        static void Fill(Mesh mesh, MeshData data)
        {
            mesh.indexFormat = data.Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uv0);
            if (data.Uv1.Count == data.Vertices.Count)
                mesh.SetUVs(1, data.Uv1);
            if (data.Uv2.Count == data.Vertices.Count)
                mesh.SetUVs(2, data.Uv2);
            mesh.SetTriangles(data.Triangles, 0, false);
        }

        static Mesh SaveMesh(string name, MeshData data, float raise = 0f)
        {
            var mesh = new Mesh { name = name };
            Fill(mesh, data);
            mesh.RecalculateBounds();
            if (raise > 0f)
            {
                // The pool shader raises the surface by up to its depth.
                var b = mesh.bounds;
                b.Encapsulate(b.max + Vector3.up * raise);
                mesh.bounds = b;
            }

            var path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                ProjectPaths.EnsureFolder(MeshFolder);
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Overwrite in place through the Mesh API (CopySerialized leaves the in-memory mesh stale).
            existing.Clear();
            Fill(existing, data);
            existing.bounds = mesh.bounds;
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        internal sealed class MeshData
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv0 = new List<Vector2>();
            public readonly List<Vector2> Uv1 = new List<Vector2>();
            public readonly List<Vector3> Uv2 = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();

            /// <summary>Adds a triangle facing <paramref name="facing"/> (Unity: front = cross(b - a, c - a)).</summary>
            void Triangle(int a, int b, int c, Vector3 facing)
            {
                var front = Vector3.Dot(Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]), facing) >= 0f;
                Triangles.AddRange(front ? new[] { a, b, c } : new[] { a, c, b });
            }

            /// <summary>
            /// A tube from <paramref name="a"/> to <paramref name="b"/>, extended by its radius at both ends so joints
            /// overlap; UV0.x = network distance. Optional caps.
            /// </summary>
            public void Tube(Vector3 a, Vector3 b, float radius, float d0, float d1, int sides, bool capStart = false, bool capEnd = false, bool extend = true)
            {
                var axis = (b - a).normalized;
                if (axis == Vector3.zero)
                    return;
                var start = a - axis * (extend && !capStart ? radius : 0f);
                var end = b + axis * (extend && !capEnd ? radius : 0f);
                var side = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
                var u = Vector3.Cross(axis, side).normalized;
                var w = Vector3.Cross(axis, u).normalized;
                var first = Vertices.Count;
                for (var i = 0; i <= sides; i++)
                {
                    var angle = i * Mathf.PI * 2f / sides;
                    var n = u * Mathf.Cos(angle) + w * Mathf.Sin(angle);
                    Vertices.Add(start + n * radius); Normals.Add(n); Uv0.Add(new Vector2(d0, (float)i / sides));
                    Vertices.Add(end + n * radius); Normals.Add(n); Uv0.Add(new Vector2(d1, (float)i / sides));
                }

                for (var i = 0; i < sides; i++)
                {
                    var s0 = first + i * 2;
                    var n = (Normals[s0] + Normals[s0 + 2]).normalized;
                    Triangle(s0, s0 + 1, s0 + 3, n);
                    Triangle(s0, s0 + 3, s0 + 2, n);
                }

                if (capStart)
                    Cap(start, -axis, u, w, radius, sides, d0);
                if (capEnd)
                    Cap(end, axis, u, w, radius, sides, d1);
            }

            void Cap(Vector3 centre, Vector3 normal, Vector3 u, Vector3 w, float radius, int sides, float d)
            {
                var c = Vertices.Count;
                Vertices.Add(centre); Normals.Add(normal); Uv0.Add(new Vector2(d, 0f));
                var ring = Vertices.Count;
                for (var i = 0; i < sides; i++)
                {
                    var angle = i * Mathf.PI * 2f / sides;
                    Vertices.Add(centre + (u * Mathf.Cos(angle) + w * Mathf.Sin(angle)) * radius); Normals.Add(normal); Uv0.Add(new Vector2(d, 0f));
                }

                for (var i = 0; i < sides; i++)
                    Triangle(c, ring + i, ring + (i + 1) % sides, normal);
            }

            /// <summary>A pipe run with couplings every <paramref name="couplingSpacing"/> metres (0 = none); advances the distance.</summary>
            public void Pipe(Vector3 a, Vector3 b, float radius, ref float distance, float couplingSpacing, int sides = 12)
            {
                var length = Vector3.Distance(a, b);
                Tube(a, b, radius, distance, distance + length, sides);
                if (couplingSpacing > 0f && radius > 0.015f)
                {
                    var axis = (b - a).normalized;
                    for (var s = couplingSpacing; s < length - 0.5f; s += couplingSpacing)
                        Fitting(a + axis * s, axis, radius * 1.18f, 0.09f);
                }

                distance += length;
            }

            /// <summary>A closed short cylinder: socket, coupling, emitter, connector.</summary>
            public void Fitting(Vector3 centre, Vector3 axis, float radius, float length, int sides = 12)
            {
                var half = axis.normalized * (length * 0.5f);
                Tube(centre - half, centre + half, radius, 0f, 0f, sides, true, true, false);
            }

            /// <summary>An elbow at a turn: a slightly larger socket hub.</summary>
            public void Elbow(Vector3 corner, Vector3 legA, Vector3 legB, float radius)
            {
                // Two sockets, one along each leg, meeting at the corner.
                Fitting(corner + legA.normalized * radius * 1.2f, legA, radius * 1.22f, radius * 3.2f);
                Fitting(corner + legB.normalized * radius * 1.2f, legB, radius * 1.22f, radius * 3.2f);
            }

            public void EndCap(Vector3 end, Vector3 axis, float radius) => Fitting(end, axis, radius * 1.25f, 0.06f);

            /// <summary>Surface of revolution about +Y from a profile of (radius, height), with smooth normals.</summary>
            public void Lathe(List<Vector2> profile, int sides)
            {
                var first = Vertices.Count;
                for (var p = 0; p < profile.Count; p++)
                {
                    var prev = profile[Mathf.Max(0, p - 1)];
                    var next = profile[Mathf.Min(profile.Count - 1, p + 1)];
                    var tangent = (next - prev).normalized;
                    var n2 = new Vector2(tangent.y, -tangent.x);
                    for (var i = 0; i <= sides; i++)
                    {
                        var angle = i * Mathf.PI * 2f / sides;
                        var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        Vertices.Add(dir * profile[p].x + Vector3.up * profile[p].y);
                        Normals.Add((dir * n2.x + Vector3.up * n2.y).normalized);
                        Uv0.Add(new Vector2((float)i / sides, profile[p].y));
                    }
                }

                var stride = sides + 1;
                for (var p = 0; p < profile.Count - 1; p++)
                {
                    for (var i = 0; i < sides; i++)
                    {
                        var a = first + p * stride + i;
                        var b = a + 1;
                        var c = a + stride;
                        var e = c + 1;
                        var facing = Normals[a] + Normals[e];
                        Triangle(a, b, e, facing);
                        Triangle(a, e, c, facing);
                    }
                }
            }

            /// <summary>
            /// A flat pool quad at its empty height: UV0 = local (-1..1), UV1 = (arrival, depth), UV2 = (half width, half length, shore).
            /// </summary>
            public void Pool(Vector3 centre, Vector2 size, float shore, float arrival, float depth)
            {
                var first = Vertices.Count;
                foreach (var c in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
                {
                    Vertices.Add(centre + new Vector3(c.x * size.x * 0.5f, 0f, c.y * size.y * 0.5f));
                    Normals.Add(Vector3.up);
                    Uv0.Add(c);
                    Uv1.Add(new Vector2(arrival, depth));
                    Uv2.Add(new Vector3(size.x * 0.5f, size.y * 0.5f, shore));
                }

                Triangle(first, first + 1, first + 2, Vector3.up);
                Triangle(first, first + 2, first + 3, Vector3.up);
            }

            /// <summary>
            /// A row's wet-soil quad: UV0 = (x - emitter line, z - first emitter), UV1 = (first emitter distance, spacing),
            /// UV2 = (radius, emitter count, z stretch).
            /// </summary>
            public void WetQuad(float y, float minX, float maxX, float minZ, float maxZ, float emitterX, float firstZ, float firstDistance,
                float spacing, float radius, int count, float stretch)
            {
                var first = Vertices.Count;
                foreach (var (x, z) in new[] { (minX, minZ), (maxX, minZ), (maxX, maxZ), (minX, maxZ) })
                {
                    Vertices.Add(new Vector3(x, y, z));
                    Normals.Add(Vector3.up);
                    Uv0.Add(new Vector2(x - emitterX, z - firstZ));
                    Uv1.Add(new Vector2(firstDistance, spacing));
                    Uv2.Add(new Vector3(radius, count, stretch));
                }

                Triangle(first, first + 1, first + 2, Vector3.up);
                Triangle(first, first + 2, first + 3, Vector3.up);
            }
        }
    }
}
