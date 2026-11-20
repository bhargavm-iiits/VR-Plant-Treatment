using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Realistic assets for <see cref="TreatmentLabDressing"/>: CC0 scanned/modelled props from Poly Haven and CC0 PBR
    /// surfaces from ambientCG (all under <see cref="LabAssets"/>, see its ATTRIBUTION.md), and the project's own crop models
    /// as young potted plants. The props are copied in as plain static mesh renderers (fitted to a real-world size, with a
    /// LODGroup where the asset ships a "_LOD1" mesh); the surfaces become the dressing palette's URP Lit materials.
    /// Every texture under <see cref="LabAssets"/> is capped at 1024 px (ASTC on Android).
    /// </summary>
    public static partial class TreatmentLabDressing
    {
        const string LabAssets = "Assets/Lab_Treatment_Assets/Lab";
        const string SurfaceFolder = LabAssets + "/Surfaces";
        const string PlantMaterialFolder = MaterialFolder + "/Plants";
        const string PlantPrefabFolder = ProjectPaths.Root + "/Prefabs/Plants";

        // ------------------------------------------------------------------ import settings

        /// <summary>Texture import settings for the downloaded assets (idempotent: only changed importers reimport).</summary>
        static void PrepareLabAssets()
        {
            if (!AssetDatabase.IsValidFolder(LabAssets))
                return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { LabAssets }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;
                var file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                var normal = file.Contains("_nor_gl") || file.EndsWith("_normalgl");
                var linear = file.Contains("_arm") || file.EndsWith("_roughness") || file.EndsWith("_metalness") || file.EndsWith("_metalsmooth") ||
                             file.EndsWith("_ambientocclusion");
                var changed = false;
                if (normal && importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    changed = true;
                }

                var srgb = !normal && !linear;
                if (importer.sRGBTexture != srgb)
                {
                    importer.sRGBTexture = srgb;
                    changed = true;
                }

                if (importer.maxTextureSize > 1024)
                {
                    importer.maxTextureSize = 1024;
                    changed = true;
                }

                if (!importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = true;
                    changed = true;
                }

                var android = importer.GetPlatformTextureSettings("Android");
                if (!android.overridden || android.maxTextureSize > 1024 || android.format != TextureImporterFormat.ASTC_6x6)
                {
                    android.overridden = true;
                    android.maxTextureSize = 1024;
                    android.format = TextureImporterFormat.ASTC_6x6;
                    importer.SetPlatformTextureSettings(android);
                    changed = true;
                }

                if (changed)
                    importer.SaveAndReimport();
            }
        }

        /// <summary>
        /// The URP metallic/smoothness map of an ambientCG surface (R = metalness, A = 1 - roughness), written once next to
        /// its source maps. Null when the surface ships no roughness map.
        /// </summary>
        static Texture2D MetalSmoothMap(string id)
        {
            var folder = $"{SurfaceFolder}/{id}";
            var path = $"{folder}/{id}_MetalSmooth.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var roughnessPath = $"{folder}/{id}_Roughness.jpg";
            if (!File.Exists(roughnessPath))
                return null;
            var roughness = new Texture2D(2, 2);
            roughness.LoadImage(File.ReadAllBytes(roughnessPath));
            Texture2D metal = null;
            var metalPath = $"{folder}/{id}_Metalness.jpg";
            if (File.Exists(metalPath))
            {
                metal = new Texture2D(2, 2);
                metal.LoadImage(File.ReadAllBytes(metalPath));
            }

            var w = roughness.width;
            var h = roughness.height;
            var packed = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            var r = roughness.GetPixels32();
            var m = metal != null && metal.width == w && metal.height == h ? metal.GetPixels32() : null;
            var pixels = new Color32[r.Length];
            for (var i = 0; i < r.Length; i++)
            {
                var metallic = m != null ? m[i].r : (byte)0;
                pixels[i] = new Color32(metallic, metallic, metallic, (byte)(255 - r[i].r));
            }

            packed.SetPixels32(pixels);
            File.WriteAllBytes(path, packed.EncodeToPNG());
            Object.DestroyImmediate(roughness);
            Object.DestroyImmediate(packed);
            if (metal != null)
                Object.DestroyImmediate(metal);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// A URP Lit material from an ambientCG surface: its colour map (unless <paramref name="useColour"/> is false) tinted by
        /// <paramref name="tint"/>, its OpenGL normal map and its packed metal/smoothness; <paramref name="tiling"/> repeats per
        /// metre of the generated meshes' planar UVs. Falls back to a plain colour when the surface is missing.
        /// </summary>
        static Material Surface(string name, string id, Color tint, float tiling, bool useColour = true, float smoothness = 1f, float normalScale = 1f)
        {
            var folder = $"{SurfaceFolder}/{id}";
            var colour = useColour ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{id}_Color.jpg") : null;
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{id}_NormalGL.jpg");
            var metalSmooth = MetalSmoothMap(id);
            var material = MaterialFactory.Lit($"{MaterialFolder}/{name}.mat", tint, 0f, metalSmooth != null ? smoothness : 0.3f, colour, normal, metalSmooth,
                normalScale: normalScale);
            material.SetTextureScale("_BaseMap", Vector2.one * tiling);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ downloaded models

        static GameObject LabModel(string asset) => AssetDatabase.LoadAssetAtPath<GameObject>($"{LabAssets}/{asset}/{asset}.gltf");

        /// <summary>How a fitted model is placed on its holder's origin.</summary>
        enum Anchor
        {
            /// <summary>Bottom on the origin, centred in X and Z.</summary>
            Floor,

            /// <summary>Bottom on the origin, centred in X, its back (+Z) face on the origin (wall-mounted items).</summary>
            Wall,
        }

        /// <summary>
        /// Copies a downloaded model's meshes (main meshes, plus "_LOD1" meshes as a second LOD level) under a new holder at
        /// <paramref name="position"/>: scaled to <paramref name="size"/> per axis (holder space, before the yaw) when given,
        /// otherwise uniformly to <paramref name="height"/>, and anchored on the origin. Returns null if the model is missing.
        /// </summary>
        static Transform Real(string asset, Transform parent, Vector3 position, float yaw, float height = 0f, Vector3 size = default,
            Material[] materials = null, bool shadows = false, string name = null, Anchor anchor = Anchor.Floor, float modelYaw = 0f)
        {
            var model = LabModel(asset);
            if (model == null)
                return null;
            var holder = Group(name ?? asset, parent, position, yaw);
            var inner = new GameObject("Model").transform;
            inner.SetParent(holder, false);
            inner.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
            var lod0 = new List<Renderer>();
            var lod1 = new List<Renderer>();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var go = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(inner, false);
                var matrix = model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                go.transform.SetLocalPositionAndRotation(matrix.GetPosition(), matrix.rotation);
                go.transform.localScale = matrix.lossyScale;
                go.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = materials ?? filter.GetComponent<MeshRenderer>().sharedMaterials;
                (filter.name.EndsWith("_LOD1") ? lod1 : lod0).Add(renderer);
            }

            var bounds = LocalBounds(holder, inner.gameObject);
            if (size != default)
                inner.localScale = Vector3.Scale(inner.localScale,
                    new Vector3(size.x / Mathf.Max(1e-5f, bounds.size.x), size.y / Mathf.Max(1e-5f, bounds.size.y), size.z / Mathf.Max(1e-5f, bounds.size.z)));
            else if (height > 0f)
                inner.localScale *= height / Mathf.Max(1e-5f, bounds.size.y);
            bounds = LocalBounds(holder, inner.gameObject);
            inner.localPosition -= new Vector3(bounds.center.x, bounds.min.y, anchor == Anchor.Wall ? bounds.max.z : bounds.center.z);

            if (lod1.Count > 0)
            {
                var group = inner.gameObject.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(0.2f, lod0.ToArray()), new LOD(0.02f, lod1.ToArray()) });
                group.RecalculateBounds();
            }

            Finish(inner.gameObject, shadows);
            return holder;
        }

        /// <summary>Bounds of a holder's meshes in its own space.</summary>
        static Bounds Extent(Transform holder) => LocalBounds(holder, holder.gameObject);

        /// <summary>A cardboard carton exactly <paramref name="size"/> big, centred on <paramref name="centre"/>.</summary>
        static void Carton(Transform parent, Vector3 centre, Vector3 size, float yaw = 0f)
        {
            if (Real("Carton", parent, centre - Vector3.up * size.y * 0.5f, yaw, size: size) == null)
                Box("Carton", parent, centre, size, pal.Cardboard, 0.006f);
        }

        // ------------------------------------------------------------------ potted plants (project crop models)

        static readonly string[] HealthyPlants = { "Tomato_Farm", "Chilli_Farm", "Soybean_Farm", "BellPepper_Farm" };

        static readonly string[] SickPlants =
        {
            "Tomato/Tomato_early_blight_Farm", "Variants/Chilli_leaf_curl_Farm", "Variants/Soybean_frogeye_leaf_spot_Farm", "Tomato/Tomato_late_blight_Farm",
            "Variants/Chilli_anthracnose_Farm", "Tomato/Tomato_septoria_leaf_spot_Farm",
        };

        static int healthyIndex, sickIndex;

        /// <summary>
        /// A young crop plant in a clay pot (standing on the point): the project's crop model (its mid LOD, without the farm
        /// soil patch) at <c>0.2 m x plantScale</c> tall, its disease showing when <paramref name="sick"/>, healthy otherwise.
        /// </summary>
        static void PottedSeedling(Transform parent, Vector3 basePoint, bool sick, float plantScale = 1f)
        {
            var plantHeight = 0.2f * plantScale;
            var potHeight = 0.12f * Mathf.Max(1f, plantScale / 1.6f);
            var holder = Group("PottedPlant", parent, basePoint, basePoint.x * 300f + basePoint.z * 170f);
            var pot = Real("ClayPot", holder, Vector3.zero, 0f, potHeight, name: "Pot");
            float soilY, soilRadius;
            if (pot != null)
            {
                var b = Extent(pot);
                soilY = b.max.y * 0.86f;
                soilRadius = Mathf.Min(b.size.x, b.size.z) * 0.44f;
            }
            else
            {
                Lathe("Pot", holder, "Pot", PotProfile, pal.Terracotta, Vector3.zero, potHeight / 0.125f, 12, false);
                soilY = potHeight * 0.8f;
                soilRadius = 0.058f * potHeight / 0.125f;
            }

            Cyl("Soil", holder, Vector3.up * (soilY - 0.004f), soilRadius, 0.008f, pal.Soil, 12);
            var prefab = sick ? SickPlants[sickIndex++ % SickPlants.Length] : HealthyPlants[healthyIndex++ % HealthyPlants.Length];
            if (!CropPlant(holder, prefab, sick, new Vector3(0f, soilY, 0f), plantHeight))
                Prop("Seedling", holder, CylinderMesh(), sick ? pal.LeafSick : pal.Leaf, Vector3.up * soilY, Vector3.zero, new Vector3(0.01f, plantHeight, 0.01f));
        }

        /// <summary>The mid-LOD mesh of a crop prefab, scaled to <paramref name="height"/>, base on the point. False if missing.</summary>
        static bool CropPlant(Transform parent, string prefabPath, bool sick, Vector3 basePoint, float height)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PlantPrefabFolder}/{prefabPath}.prefab");
            var lodGroup = prefab != null ? prefab.GetComponent<LODGroup>() : null;
            if (lodGroup == null)
                return false;
            var lods = lodGroup.GetLODs();
            var source = (lods.Length > 2 ? lods[1] : lods[0]).renderers;
            if (source.Length == 0 || source[0] == null)
                return false;
            var filter = source[0].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return false;
            var mesh = PlantMesh(filter.sharedMesh, source[0].sharedMaterials, out var materials);
            if (!sick)
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = HealthyMaterial(materials[i]);

            var go = new GameObject("Plant", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            var scale = height / Mathf.Max(1e-4f, mesh.bounds.size.y);
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = basePoint - new Vector3(mesh.bounds.center.x, mesh.bounds.min.y, mesh.bounds.center.z) * scale;
            Finish(go);
            return true;
        }

        /// <summary>A copy of a crop mesh without its farm soil patch (submeshes whose material is soil), bounds of what is left.</summary>
        static Mesh PlantMesh(Mesh source, Material[] sourceMaterials, out Material[] kept)
        {
            var keep = new List<int>();
            for (var i = 0; i < source.subMeshCount; i++)
                if (i >= sourceMaterials.Length || sourceMaterials[i] == null || !sourceMaterials[i].name.ToLowerInvariant().Contains("soil"))
                    keep.Add(i);
            var materials = new List<Material>();
            foreach (var i in keep)
                materials.Add(i < sourceMaterials.Length ? sourceMaterials[i] : null);
            kept = materials.ToArray();
            return Cached($"Plant_{source.name}", () =>
            {
                var mesh = Object.Instantiate(source);
                var triangles = new List<int[]>();
                foreach (var i in keep)
                    triangles.Add(source.GetTriangles(i));
                mesh.subMeshCount = triangles.Count;
                var vertices = source.vertices;
                var min = Vector3.positiveInfinity;
                var max = Vector3.negativeInfinity;
                for (var s = 0; s < triangles.Count; s++)
                {
                    mesh.SetTriangles(triangles[s], s, false);
                    foreach (var index in triangles[s])
                    {
                        min = Vector3.Min(min, vertices[index]);
                        max = Vector3.Max(max, vertices[index]);
                    }
                }

                mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
                return mesh;
            });
        }

        /// <summary>A healthy copy of a crop's disease material (BTP/Farm Recovery with _DiseaseAmount = 0); other materials as they are.</summary>
        static Material HealthyMaterial(Material source)
        {
            if (source == null || !source.HasProperty("_DiseaseAmount"))
                return source;
            var path = $"{PlantMaterialFolder}/{source.name}_Healthy.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                ProjectPaths.EnsureFolder(PlantMaterialFolder);
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            material.SetFloat("_DiseaseAmount", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ seed trays

        /// <summary>
        /// A 0.55 x 0.275 m seed tray slot: two scanned 3 x 3 plug trays filled with compost and one young seedling per cell
        /// (yellowed when <paramref name="sick"/>), merged into one renderer (tray, compost, leaves).
        /// </summary>
        static GameObject SeedTray(Transform parent, Vector3 position, Vector3 euler, bool sick)
        {
            var mesh = RealSeedTrayMesh(sick, out var materials);
            if (mesh == null)
                return Box("SeedTray", parent, position + Vector3.up * 0.03f, new Vector3(0.54f, 0.06f, 0.28f), pal.Rubber, 0.004f, euler);
            var go = Prop("SeedTray", parent, mesh, null, position, euler, Vector3.one);
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        static Mesh RealSeedTrayMesh(bool sick, out Material[] materials)
        {
            materials = null;
            var trayModel = LabModel("SeedingTray");
            var sprigModel = LabModel("SorrelSeedlings");
            if (trayModel == null || sprigModel == null)
                return null;
            var trayFilter = trayModel.GetComponentInChildren<MeshFilter>();
            var sprigs = new List<MeshFilter>();
            foreach (var f in sprigModel.GetComponentsInChildren<MeshFilter>())
                if (f.sharedMesh != null && f.sharedMesh.GetIndexCount(0) / 3 < 100)
                    sprigs.Add(f);
            if (trayFilter == null || sprigs.Count == 0)
                return null;
            var leaf = sprigs[0].GetComponent<MeshRenderer>().sharedMaterial;
            materials = new[] { trayFilter.GetComponent<MeshRenderer>().sharedMaterial, pal.Soil, sick ? SickLeafMaterial(leaf) : leaf };

            return Cached(sick ? "RealSeedTraySick" : "RealSeedTray", () =>
            {
                var tray = trayFilter.sharedMesh;
                var tb = tray.bounds;
                const float slotDepth = 0.275f;
                var scale = slotDepth / tb.size.z;
                var trayWidth = tb.size.x * scale;
                var trayHeight = tb.size.y * scale;
                var soilY = trayHeight * 0.86f;
                var random = new System.Random(sick ? 23 : 17);

                var trays = new List<CombineInstance>();
                var soils = new List<CombineInstance>();
                var leaves = new List<CombineInstance>();
                var soilQuad = new MeshBuilder();
                var half = (trayWidth - 0.012f) * 0.5f;
                soilQuad.Poly(0, Vector3.up, new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half));
                var soilMesh = soilQuad.Build();
                for (var t = 0; t < 2; t++)
                {
                    var centre = new Vector3((t - 0.5f) * trayWidth, 0f, 0f);
                    trays.Add(new CombineInstance
                    {
                        mesh = tray,
                        transform = Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one * scale) * Matrix4x4.Translate(new Vector3(-tb.center.x, -tb.min.y, -tb.center.z)),
                    });
                    soils.Add(new CombineInstance { mesh = soilMesh, transform = Matrix4x4.Translate(centre + Vector3.up * soilY) });
                    var pitch = trayWidth / 3f;
                    for (var ix = 0; ix < 3; ix++)
                    for (var iz = 0; iz < 3; iz++)
                    {
                        var sprig = sprigs[random.Next(sprigs.Count)].sharedMesh;
                        var sb = sprig.bounds;
                        var cell = centre + new Vector3((ix - 1) * pitch, soilY - 0.003f, (iz - 1) * pitch);
                        // The sorrel sprigs are 4-6 cm; true-leaf seedlings two weeks on stand 7-10 cm.
                        var s = (1.6f + (float)random.NextDouble() * 0.6f) * (sick ? 0.8f : 1f);
                        leaves.Add(new CombineInstance
                        {
                            mesh = sprig,
                            transform = Matrix4x4.TRS(cell, Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Vector3.one * s) *
                                        Matrix4x4.Translate(new Vector3(-sb.center.x, -sb.min.y, -sb.center.z)),
                        });
                    }
                }

                Mesh Merge(List<CombineInstance> parts)
                {
                    var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                    m.CombineMeshes(parts.ToArray(), true, true);
                    return m;
                }

                var groups = new[] { Merge(trays), Merge(soils), Merge(leaves) };
                var vertexCount = 0;
                var all = new CombineInstance[groups.Length];
                for (var i = 0; i < groups.Length; i++)
                {
                    all[i] = new CombineInstance { mesh = groups[i], transform = Matrix4x4.identity };
                    vertexCount += groups[i].vertexCount;
                }

                // The index format is chosen before the data goes in (changing it afterwards drops the indices).
                var combined = new Mesh { indexFormat = vertexCount < 65000 ? IndexFormat.UInt16 : IndexFormat.UInt32 };
                combined.CombineMeshes(all, false, true);
                combined.RecalculateBounds();
                foreach (var g in groups)
                    Object.DestroyImmediate(g);
                Object.DestroyImmediate(soilMesh);
                return combined;
            });
        }

        /// <summary>The seedlings' leaf material, yellowed and spotted-looking for the infected tray.</summary>
        static Material SickLeafMaterial(Material source)
        {
            var path = $"{PlantMaterialFolder}/SeedlingLeaf_Sick.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                ProjectPaths.EnsureFolder(PlantMaterialFolder);
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            var tint = new Color(1f, 0.78f, 0.32f, 1f);
            foreach (var property in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                if (material.HasProperty(property))
                    material.SetColor(property, tint);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
