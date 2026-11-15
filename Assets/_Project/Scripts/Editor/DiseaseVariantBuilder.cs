using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTP.Plants;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Builds one Farm prefab per PlantVillage disease class (24) for the Farm disease rows, and the
    /// DiseaseVariantCatalog that maps (plantId, diseaseId) to it. Run after the plant prefabs exist (Build Plant
    /// Assets and the Blender tree/tomato replacements); every prefab is rebuilt in place, so its GUID and every
    /// scene instance stay valid.
    ///
    ///   tomato: the nine Blender disease plants of TomatoPlantBuilder, as they are;
    ///   apple, grape: the Blender tree with all three supplied disease leaves remapped to the one disease's leaf
    ///     material. The leaf sub-meshes are merged (one draw instead of three) and the heavy non-leaf parts are
    ///     simplified with MeshLodUtility for row use (the apple's bark and fruit, the grape's wood and stems); the
    ///     grape's 367k-triangle berries become round impostors (BerryImpostors, 2 triangles per berry). The meshes
    ///     are shared by the variants, only the leaf material differs. Both get a baked billboard as their last LOD;
    ///     the grape keeps its pergola module, merged into one mesh and one material;
    ///   corn, potato: the base model with a per-disease illustrative albedo (DiseaseLesionTextures) on the same
    ///     BTP/Farm Recovery material system (the plant's healthy albedo and recovery mask), plus its own billboard;
    ///   cherry, peach, bell pepper, strawberry (one disease each): the base Farm prefab's meshes and materials,
    ///     unchanged, with LOD switch points tuned for rows (and a billboard for the trees).
    ///   blueberry, chilli, cotton, mango, orange, rice, soybean, squash, sugarcane, wheat (34 conditions): built by
    ///     <see cref="FarmCropBuilder"/> from the supplied whole plants, and added to the catalog here.
    /// Every variant keeps BTP/Farm Recovery leaves, so the Farm six-week timeline still fades it.
    /// </summary>
    public static class DiseaseVariantBuilder
    {
        const string TextureFolder = ProjectPaths.PlantTextures + "/Variants";
        const string MaterialFolder = ProjectPaths.PlantMaterials + "/Variants";
        const int BillboardViewSize = 512;

        /// <summary>
        /// LOD switch points (screen-relative height) per plant. Quest (LOD bias 1, ~96 degree vertical field of view)
        /// keeps a level to about size / (2.2 x value) metres: apple (4 m) near mesh to ~7 m, mid mesh to ~11 m;
        /// grape module (3.7 m) to ~4.2 m / ~7.6 m; small crops' full meshes to 2.5-4 m; tomato (1.8 m) near mesh to
        /// ~3.7 m, mid mesh to ~7.5 m. Quest 3 (bias 2) and PC (bias 3) keep each level two to three times further.
        /// Mid meshes cast no shadows (the near meshes do, within the 20 m Quest shadow distance).
        /// </summary>
        sealed class Lods
        {
            public float Near, Mid, Cull;
        }

        static readonly Dictionary<string, Lods> RowLods = new Dictionary<string, Lods>
        {
            ["apple"] = new Lods { Near = 0.25f, Mid = 0.16f, Cull = 0.006f },
            ["cherry"] = new Lods { Near = 0.25f, Mid = 0.16f, Cull = 0.006f },
            ["peach"] = new Lods { Near = 0.25f, Mid = 0.16f, Cull = 0.006f },
            ["grape"] = new Lods { Near = 0.40f, Mid = 0.22f, Cull = 0.01f },
            ["corn"] = new Lods { Near = 0.18f, Cull = 0.006f },
            ["potato"] = new Lods { Near = 0.08f, Cull = 0.006f },
            ["bell_pepper"] = new Lods { Near = 0.10f, Cull = 0.006f },
            ["strawberry"] = new Lods { Near = 0.07f, Cull = 0.006f },
        };

        /// <summary>The supplied disease leaf of the Blender apple and grape trees, per diseaseId.</summary>
        static readonly Dictionary<string, Dictionary<string, string>> TreeLeaves = new Dictionary<string, Dictionary<string, string>>
        {
            ["apple"] = new Dictionary<string, string>
            {
                ["apple_scab"] = "AppleLeaf_Scab", ["black_rot"] = "AppleLeaf_BlackRot", ["cedar_apple_rust"] = "AppleLeaf_Rust",
            },
            ["grape"] = new Dictionary<string, string>
            {
                ["black_rot"] = "GrapeLeaf_BlackRot", ["esca"] = "GrapeLeaf_Esca", ["leaf_blight"] = "GrapeLeaf_LeafBlight",
            },
        };

        /// <summary>File-name stem of each plant's prefabs (Apple_Farm.prefab, BellPepper_Farm.prefab...).</summary>
        public static string Stem(string plantId) => plantId switch
        {
            "bell_pepper" => "BellPepper",
            _ => char.ToUpperInvariant(plantId[0]) + plantId.Substring(1),
        };

        public static string VariantPrefab(string plantId, string diseaseId) =>
            plantId == "tomato" ? TomatoPlantBuilder.VariantPrefab(diseaseId) : $"{ProjectPaths.VariantPrefabs}/{Stem(plantId)}_{diseaseId}_Farm.prefab";

        [MenuItem("BTP/Setup/26 - Build Farm Disease Variants", priority = 26)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode before building the disease variants.");
            foreach (var folder in new[] { ProjectPaths.VariantPrefabs, ProjectPaths.VariantMeshes, TextureFolder, MaterialFolder })
                ProjectPaths.EnsureFolder(folder);

            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog)
                          ?? throw new InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            var entries = new List<DiseaseVariantCatalog.Entry>();
            MeshCache.Clear();
            pergolaCache = default;
            berryCache = default;
            try
            {
                var all = DiseaseClasses.All;
                for (var i = 0; i < all.Count; i++)
                {
                    var info = all[i];
                    EditorUtility.DisplayProgressBar("BTP disease variants", $"{info.PlantId} / {info.DiseaseId}", (float)i / all.Count);
                    if (!catalog.TryGet(info.PlantId, out var plant) || plant.FarmPrefab == null)
                        throw new InvalidOperationException($"No {info.PlantId} Farm prefab in the plant catalog.");
                    entries.Add(Build(plant, info.DiseaseId));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // The ten field and tropical crops (FarmCropBuilder: one supplied whole plant per condition), once their
            // plant definitions exist (BTP/Setup/8).
            if (catalog.LabOnlyPlants.Count > 0)
                entries.AddRange(FarmCropBuilder.BuildVariants());
            else
                Debug.LogWarning("[BTP] The field and tropical crops have no plant definitions yet (BTP/Setup/8); their Farm plants were skipped.");

            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog);
            if (variants == null)
            {
                variants = ScriptableObject.CreateInstance<DiseaseVariantCatalog>();
                AssetDatabase.CreateAsset(variants, ProjectPaths.VariantCatalog);
            }

            variants.SetEntries(entries);
            EditorUtility.SetDirty(variants);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Built {entries.Count} Farm disease variants and {ProjectPaths.VariantCatalog}.");
        }

        /// <summary>
        /// Rebuilds only one plant's disease variants (for example after BellPepperPlantBuilder.Apply) and replaces
        /// their entries in the existing DiseaseVariantCatalog, leaving every other plant's prefabs and entries as
        /// they are. The full <see cref="BuildAll"/> makes the catalog the first time.
        /// </summary>
        public static void BuildPlant(string plantId)
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode before building the disease variants.");
            foreach (var folder in new[] { ProjectPaths.VariantPrefabs, ProjectPaths.VariantMeshes, TextureFolder, MaterialFolder })
                ProjectPaths.EnsureFolder(folder);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog)
                          ?? throw new InvalidOperationException("Run BTP/Setup/2 - Build Plant Assets first.");
            var variants = AssetDatabase.LoadAssetAtPath<DiseaseVariantCatalog>(ProjectPaths.VariantCatalog)
                           ?? throw new InvalidOperationException("Run BTP/Setup/26 - Build Farm Disease Variants first.");
            if (!catalog.TryGet(plantId, out var plant) || plant.FarmPrefab == null)
                throw new InvalidOperationException($"No {plantId} Farm prefab in the plant catalog.");

            MeshCache.Clear();
            pergolaCache = default;
            berryCache = default;
            var built = DiseaseClasses.All.Where(info => info.PlantId == plantId).Select(info => Build(plant, info.DiseaseId)).ToList();
            var entries = variants.Entries.Where(e => e.plantId != plantId).Concat(built)
                .OrderBy(e => DiseaseClasses.AllCases.ToList().FindIndex(info => info.PlantId == e.plantId && info.DiseaseId == e.diseaseId)).ToList();
            variants.SetEntries(entries);
            EditorUtility.SetDirty(variants);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Rebuilt {built.Count} {plantId} Farm disease variant(s); {ProjectPaths.VariantCatalog} has {entries.Count} entries.");
        }

        static DiseaseVariantCatalog.Entry Build(PlantDefinition plant, string diseaseId)
        {
            var id = plant.PlantId;
            switch (id)
            {
                case "tomato":
                {
                    var path = TomatoPlantBuilder.VariantPrefab(diseaseId);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                        throw new InvalidOperationException($"{path} is missing; run BTP/Setup/Replace Tomato With Blender Tomato Plants (Wind).");
                    TomatoPlantBuilder.TuneForRows(path);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    return Entry(id, diseaseId, prefab, AssetDatabase.LoadAssetAtPath<Material>(TomatoPlantBuilder.LeafMaterialPath(diseaseId)));
                }
                case "apple":
                    return BuildApple(plant, diseaseId);
                case "grape":
                    return BuildGrape(plant, diseaseId);
                case "corn":
                case "potato":
                    return BuildRetextured(plant, diseaseId);
                default:
                    return BuildTuned(plant, diseaseId);
            }
        }

        static DiseaseVariantCatalog.Entry Entry(string plantId, string diseaseId, GameObject prefab, Material leaf)
        {
            if (leaf == null)
                throw new InvalidOperationException($"{plantId}/{diseaseId}: no leaf material.");
            return new DiseaseVariantCatalog.Entry { plantId = plantId, diseaseId = diseaseId, farmPrefab = prefab, leafMaterial = leaf };
        }

        // ------------------------------------------------------------------ apple and grape

        /// <summary>
        /// Apple: near mesh = the Blender LOD0 with the bark and fruit simplified (MeshLodUtility; 50k to ~28k
        /// triangles) and the three leaf sub-meshes merged; mid mesh = the Blender LOD1, likewise; far = a billboard
        /// baked from the near mesh with this disease's leaves.
        /// </summary>
        static DiseaseVariantCatalog.Entry BuildApple(PlantDefinition plant, string diseaseId)
        {
            var leaf = TreeLeaf("apple", diseaseId);
            var source = plant.FarmPrefab;
            var near = SourceRenderer(source, "Model");
            var mid = SourceRenderer(source, "ModelFar");
            var leaves = TreeLeaves["apple"].Values.ToArray();
            var nearMesh = TreeMesh(near, leaves, "AppleTree_Near", new Dictionary<string, int> { ["AppleBark"] = 2, ["AppleFruit"] = 1 });
            var midMesh = TreeMesh(mid, leaves, "AppleTree_Mid", new Dictionary<string, int> { ["AppleBark"] = 1, ["AppleFruit"] = 1 });
            var lods = RowLods["apple"];
            var path = VariantPrefab("apple", diseaseId);
            CreateOrRebuild(path, root =>
            {
                var nearRenderer = AddCopy(root, "Model", near, nearMesh.mesh, Remap(nearMesh.materials, leaves, leaf), true);
                var billboard = Billboard(root, $"Apple_{diseaseId}");
                var midRenderer = AddCopy(root, "ModelFar", mid, midMesh.mesh, Remap(midMesh.materials, leaves, leaf), false);
                SetLods(root, (lods.Near, new Renderer[] { nearRenderer }), (lods.Mid, new Renderer[] { midRenderer }), (lods.Cull, new[] { billboard }));
            });
            return Entry("apple", diseaseId, AssetDatabase.LoadAssetAtPath<GameObject>(path), leaf);
        }

        /// <summary>
        /// Grape: one pergola module like Grape_Farm, with this disease's leaves on the whole canopy. The original
        /// vine is 403k triangles, 367k of them its 1,638 berries (224 each). Decimating the berries made faceted
        /// lumps, so they are left out of the vine meshes and drawn as impostors instead (BerryImpostors: one quad per
        /// berry, round and smooth at any distance, 2 triangles each), shared by the near and mid levels. MeshLodUtility
        /// simplifies the bunch stems and wood into a near mesh of ~25k and a mid mesh of ~9k triangles, then a
        /// billboard. The pergola's 17 primitive parts become one mesh with one palette material (one draw per module).
        /// </summary>
        static DiseaseVariantCatalog.Entry BuildGrape(PlantDefinition plant, string diseaseId)
        {
            var leaf = TreeLeaf("grape", diseaseId);
            var source = plant.FarmPrefab;
            var vine = SourceRenderer(source, "Model");
            var leaves = TreeLeaves["grape"].Values.ToArray();
            var near = TreeMesh(vine, leaves, "GrapeVine_Near",
                new Dictionary<string, int> { ["GrapeWood"] = 1, ["GrapeStem"] = 4, ["GrapeBerry"] = Drop });
            var mid = TreeMesh(vine, leaves, "GrapeVine_Mid",
                new Dictionary<string, int> { ["GrapeWood"] = 6, ["GrapeCane"] = 5, ["GrapeStem"] = 6, ["GrapeBerry"] = Drop });
            var (berryMesh, berryMaterial) = GrapeBerries(vine);
            var (pergolaMesh, pergolaMaterial) = PergolaMesh(source);
            var lods = RowLods["grape"];
            var path = VariantPrefab("grape", diseaseId);
            CreateOrRebuild(path, root =>
            {
                var nearRenderer = AddCopy(root, "Model", vine, near.mesh, Remap(near.materials, leaves, leaf), true);
                var berries = AddCopy(root, "Berries", vine, berryMesh, new[] { berryMaterial }, true);
                var farRenderer = Billboard(root, $"Grape_{diseaseId}");
                var midRenderer = AddCopy(root, "ModelMid", vine, mid.mesh, Remap(mid.materials, leaves, leaf), false);
                // The same berries for the mid level, without shadows like the rest of it.
                var midBerries = AddCopy(root, "BerriesMid", vine, berryMesh, new[] { berryMaterial }, false);
                var pergola = new GameObject("Pergola", typeof(MeshFilter), typeof(MeshRenderer));
                pergola.transform.SetParent(root.transform, false);
                pergola.GetComponent<MeshFilter>().sharedMesh = pergolaMesh;
                var pergolaRenderer = pergola.GetComponent<MeshRenderer>();
                pergolaRenderer.sharedMaterial = pergolaMaterial;
                pergolaRenderer.shadowCastingMode = ShadowCastingMode.Off;
                // The pergola belongs to every level, so it culls with the vine but never switches.
                SetLods(root, (lods.Near, new Renderer[] { nearRenderer, berries, pergolaRenderer }),
                    (lods.Mid, new Renderer[] { midRenderer, midBerries, pergolaRenderer }),
                    (lods.Cull, new Renderer[] { farRenderer, pergolaRenderer }));
            });
            return Entry("grape", diseaseId, AssetDatabase.LoadAssetAtPath<GameObject>(path), leaf);
        }

        static (Mesh mesh, Material material) berryCache;

        /// <summary>
        /// The vine's berries (its GrapeBerry sub-mesh) as impostors, Models/Generated/Variants/GrapeBerries.asset, with
        /// GrapeBerry's colour and gloss on BTP/Berry Impostor. Not part of the disease timeline (berries stay as they are).
        /// </summary>
        static (Mesh mesh, Material material) GrapeBerries(MeshRenderer vine)
        {
            if (berryCache.mesh != null)
                return berryCache;
            var materials = vine.sharedMaterials;
            var submesh = Array.FindIndex(materials, m => m != null && m.name == "GrapeBerry");
            if (submesh < 0)
                throw new InvalidOperationException("The grape vine has no GrapeBerry sub-mesh; run BTP/Setup/Replace Grape With Blender Pergola Vine.");
            var built = BerryImpostors.Build(vine.GetComponent<MeshFilter>().sharedMesh, submesh, "GrapeBerries", $"{ProjectPaths.VariantMeshes}/GrapeBerries.asset");
            var material = MaterialFactory.BerryImpostor($"{MaterialFolder}/GrapeBerryImpostor.mat", materials[submesh]);
            Debug.Log($"[BTP] Grape berries: {built.Count} impostors (2 triangles each), radius {built.MinRadius * 100f:F1}-{built.MaxRadius * 100f:F1} cm, " +
                      $"length/width {built.MeanElongation:F2}.");
            return berryCache = (built.Mesh, material);
        }

        static Material TreeLeaf(string plantId, string diseaseId)
        {
            if (!TreeLeaves[plantId].TryGetValue(diseaseId, out var name))
                throw new InvalidOperationException($"No {plantId} leaf for {diseaseId}.");
            var folder = plantId == "apple" ? ProjectPaths.Root + "/Models/AppleTree" : ProjectPaths.Root + "/Models/GrapeVine";
            var path = $"{folder}/{name}.mat";
            return AssetDatabase.LoadAssetAtPath<Material>(path)
                   ?? throw new InvalidOperationException($"{path} is missing; run BTP/Setup/Replace {Stem(plantId)} With Blender ...");
        }

        static Material[] Remap(Material[] materials, string[] leaves, Material leaf) =>
            materials.Select(m => m != null && leaves.Contains(m.name) ? leaf : m).ToArray();

        /// <summary>
        /// A row mesh from one of the tree's meshes, saved under Models/Generated/Variants (rebuilt in place): the
        /// leaf sub-meshes merged into one (the leaf cards are never simplified), every other sub-mesh at the
        /// MeshLodUtility level given for its material (0 = as exported), and unused vertices dropped.
        /// Returns the mesh and the material of each of its sub-meshes (leaves: the first leaf material).
        /// </summary>
        static (Mesh mesh, Material[] materials) TreeMesh(Renderer source, string[] leaves, string name, Dictionary<string, int> levels)
        {
            // The variants of one tree share its row meshes; simplifying the grape takes seconds, so once per run.
            if (MeshCache.TryGetValue(name, out var cached))
                return cached;
            var built = BuildTreeMesh(source, leaves, name, levels);
            MeshCache[name] = built;
            return built;
        }

        static readonly Dictionary<string, (Mesh mesh, Material[] materials)> MeshCache = new Dictionary<string, (Mesh, Material[])>();

        /// <summary>TreeMesh level that leaves the material's sub-mesh out (the grape berries, drawn as impostors instead).</summary>
        const int Drop = -1;

        static (Mesh mesh, Material[] materials) BuildTreeMesh(Renderer source, string[] leaves, string name, Dictionary<string, int> levels)
        {
            var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var materials = source.sharedMaterials;
            var work = Object.Instantiate(sourceMesh);
            try
            {
                if (levels.Values.Any(l => l > 0))
                    MeshLodUtility.GenerateMeshLods(work, 8);

                // Absolute index ranges of the requested level per source sub-mesh.
                int[] allIndices;
                using (var data = Mesh.AcquireReadOnlyMeshData(work))
                {
                    allIndices = data[0].indexFormat == IndexFormat.UInt32
                        ? data[0].GetIndexData<int>().ToArray()
                        : data[0].GetIndexData<ushort>().Select(i => (int)i).ToArray();
                }

                List<int> Indices(int submesh, int level)
                {
                    var descriptor = work.GetSubMesh(submesh);
                    int start, count;
                    if (work.lodCount > 1)
                    {
                        level = Mathf.Min(level, work.lodCount - 1);
                        var range = work.GetLod(submesh, level);
                        // A level that drops the part entirely falls back to the last non-empty one.
                        while (range.indexCount == 0 && level > 0)
                            range = work.GetLod(submesh, --level);
                        start = descriptor.indexStart + (int)range.indexStart;
                        count = (int)range.indexCount;
                    }
                    else
                    {
                        start = descriptor.indexStart;
                        count = descriptor.indexCount;
                    }

                    var list = new List<int>(count);
                    for (var i = 0; i < count; i++)
                        list.Add(allIndices[start + i] + descriptor.baseVertex);
                    return list;
                }

                var parts = new List<(List<int> indices, Material material)>();
                var leafIndices = new List<int>();
                Material firstLeaf = null;
                for (var s = 0; s < sourceMesh.subMeshCount; s++)
                {
                    var material = materials[Mathf.Min(s, materials.Length - 1)];
                    if (material != null && leaves.Contains(material.name))
                    {
                        leafIndices.AddRange(Indices(s, 0));
                        firstLeaf ??= material;
                        continue;
                    }

                    var level = material != null && levels.TryGetValue(material.name, out var requested) ? requested : 0;
                    if (level == Drop)
                        continue;
                    parts.Add((Indices(s, level), material));
                }

                if (leafIndices.Count > 0)
                    parts.Add((leafIndices, firstLeaf));
                var mesh = Compact(sourceMesh, parts.Select(p => p.indices).ToList(), name);
                return (SaveMesh(mesh, $"{ProjectPaths.VariantMeshes}/{name}.asset"), parts.Select(p => p.material).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(work);
            }
        }

        /// <summary>A new mesh with only the vertices <paramref name="submeshes"/> use, every vertex channel kept.</summary>
        static Mesh Compact(Mesh source, List<List<int>> submeshes, string name)
        {
            var remap = new Dictionary<int, int>();
            var order = new List<int>();
            foreach (var indices in submeshes)
            {
                for (var i = 0; i < indices.Count; i++)
                {
                    if (!remap.TryGetValue(indices[i], out var mapped))
                    {
                        mapped = remap[indices[i]] = order.Count;
                        order.Add(indices[i]);
                    }

                    indices[i] = mapped;
                }
            }

            T[] Pick<T>(T[] values) => values.Length == 0 ? values : order.Select(i => values[i]).ToArray();
            var mesh = new Mesh { name = name, indexFormat = order.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = Pick(source.vertices);
            mesh.normals = Pick(source.normals);
            mesh.tangents = Pick(source.tangents);
            if (source.colors32.Length > 0)
                mesh.colors32 = Pick(source.colors32);
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute))
                    continue;
                var uvs = new List<Vector4>();
                source.GetUVs(channel, uvs);
                var picked = order.Select(i => uvs[i]).ToList();
                switch (source.GetVertexAttributeDimension(attribute))
                {
                    case 2: mesh.SetUVs(channel, picked.Select(v => (Vector2)v).ToList()); break;
                    case 3: mesh.SetUVs(channel, picked.Select(v => (Vector3)v).ToList()); break;
                    default: mesh.SetUVs(channel, picked); break;
                }
            }

            mesh.subMeshCount = submeshes.Count;
            for (var s = 0; s < submeshes.Count; s++)
                mesh.SetTriangles(submeshes[s], s, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The pergola module of Grape_Farm (posts, wire grid, ridge, mulch, drip line: 17 primitives with 5 materials)
        /// as one mesh: every part's primitive in module space, UV-mapped onto a pixel of a 5-colour palette texture
        /// taken from the parts' own materials, drawn with one Simple Lit material.
        /// </summary>
        static (Mesh mesh, Material material) pergolaCache;

        static (Mesh mesh, Material material) PergolaMesh(GameObject grapeFarm)
        {
            if (pergolaCache.mesh == null)
                pergolaCache = BuildPergolaMesh(grapeFarm);
            return pergolaCache;
        }

        static (Mesh mesh, Material material) BuildPergolaMesh(GameObject grapeFarm)
        {
            var pergola = grapeFarm.transform.Find("Pergola")
                          ?? throw new InvalidOperationException("Grape_Farm.prefab has no Pergola; run BTP/Setup/Replace Grape With Blender Pergola Vine.");
            var renderers = pergola.GetComponentsInChildren<MeshRenderer>();
            var colours = renderers.Select(r => r.sharedMaterial).Distinct().ToList();
            const int swatch = 4;
            var texture = new Texture2D(colours.Count * swatch, swatch, TextureFormat.RGBA32, false);
            for (var c = 0; c < colours.Count; c++)
            {
                var colour = colours[c].HasProperty("_BaseColor") ? colours[c].GetColor("_BaseColor") : Color.grey;
                for (var x = 0; x < swatch; x++)
                for (var y = 0; y < swatch; y++)
                    texture.SetPixel(c * swatch + x, y, colour);
            }

            texture.Apply();
            var texturePath = $"{TextureFolder}/GrapePergolaPalette.png";
            File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var combine = new List<CombineInstance>();
            var toRoot = grapeFarm.transform.worldToLocalMatrix;
            foreach (var renderer in renderers)
            {
                var mesh = Object.Instantiate(renderer.GetComponent<MeshFilter>().sharedMesh);
                var index = colours.IndexOf(renderer.sharedMaterial);
                var uv = new Vector2((index * swatch + swatch * 0.5f) / (colours.Count * swatch), 0.5f);
                mesh.uv = Enumerable.Repeat(uv, mesh.vertexCount).ToArray();
                combine.Add(new CombineInstance { mesh = mesh, transform = toRoot * renderer.transform.localToWorldMatrix });
            }

            var combined = new Mesh { name = "GrapePergola" };
            combined.CombineMeshes(combine.ToArray(), true, true);
            combined.RecalculateBounds();
            foreach (var part in combine)
                Object.DestroyImmediate(part.mesh);
            var material = MaterialFactory.SimpleLit($"{MaterialFolder}/GrapePergola.mat", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), null, false);
            return (SaveMesh(combined, $"{ProjectPaths.VariantMeshes}/GrapePergola.asset"), material);
        }

        // ------------------------------------------------------------------ corn and potato

        /// <summary>
        /// Corn and potato: the base Farm prefab's near mesh with, per BTP/Farm Recovery material, a copy whose
        /// diseased albedo is this disease's bake (healthy albedo, recovery mask and normal map shared), and a billboard
        /// baked from it.
        /// </summary>
        static DiseaseVariantCatalog.Entry BuildRetextured(PlantDefinition plant, string diseaseId)
        {
            var id = plant.PlantId;
            var near = SourceRenderer(plant.FarmPrefab, "Model");
            var materials = near.sharedMaterials.Select(m => DiseaseMaterial(id, diseaseId, m)).ToArray();
            var lods = RowLods[id];
            var stem = Stem(id);
            var path = VariantPrefab(id, diseaseId);
            CreateOrRebuild(path, root =>
            {
                var model = AddCopy(root, "Model", near, near.GetComponent<MeshFilter>().sharedMesh, materials, true);
                var billboard = Billboard(root, $"{stem}_{diseaseId}");
                SetLods(root, (lods.Near, new Renderer[] { model }), (lods.Cull, new[] { billboard }));
            });

            // The leaf material: corn's leaf blades (Maize), the potato's single atlas.
            var leaf = materials.FirstOrDefault(m => m.name.Contains("_Maize_")) ?? materials.First(m => m.shader.name == MaterialFactory.FarmRecoveryShader);
            return Entry(id, diseaseId, AssetDatabase.LoadAssetAtPath<GameObject>(path), leaf);
        }

        static Material DiseaseMaterial(string plantId, string diseaseId, Material source)
        {
            if (source == null || source.shader.name != MaterialFactory.FarmRecoveryShader)
                return source;
            var healthy = source.GetTexture("_BaseMap") as Texture2D;
            var mask = source.GetTexture("_RecoveryMask");
            var normal = source.GetTexture("_BumpMap");
            var cutout = source.GetFloat("_AlphaClip") > 0.5f;
            var stem = source.name.EndsWith("_Farm") ? source.name.Substring(0, source.name.Length - "_Farm".Length) : source.name;
            var output = $"{TextureFolder}/{stem}_{diseaseId}_FarmDiseased.{(cutout ? "png" : "jpg")}";
            // Read the source file when the healthy albedo is its own asset (full resolution); a glTF sub-asset via a blit.
            var healthyPath = AssetDatabase.GetAssetPath(healthy);
            var baked = Path.GetExtension(healthyPath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg"
                ? DiseaseLesionTextures.Create(healthyPath, output, plantId, diseaseId, cutout)
                : DiseaseLesionTextures.Create(healthy, output, plantId, diseaseId, cutout);
            var material = MaterialFactory.FarmRecovery($"{MaterialFolder}/{stem}_{diseaseId}_Farm.mat", healthy,
                AssetDatabase.LoadAssetAtPath<Texture2D>(baked), mask, normal, cutout, source.GetFloat("_MaskScale"));
            material.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ cherry, peach, bell pepper, strawberry

        /// <summary>
        /// One disease each: the base prefab's own meshes and materials (shared, so they batch with any other use),
        /// with row LOD switch points; the trees get a billboard as their last level.
        /// </summary>
        static DiseaseVariantCatalog.Entry BuildTuned(PlantDefinition plant, string diseaseId)
        {
            var id = plant.PlantId;
            var source = plant.FarmPrefab;
            var lods = RowLods[id];
            var path = VariantPrefab(id, diseaseId);
            var near = SourceRenderer(source, "Model");
            var tree = id == "cherry" || id == "peach";
            CreateOrRebuild(path, root =>
            {
                if (tree)
                {
                    // Leaves untouched (the cherry's per-leaf wind lives in their second UV set, which compaction keeps);
                    // the bark, fruit and the cherry's 28k-triangle fruit stalks simplified.
                    var leaves = new[] { id == "cherry" ? "CherryLeaf" : "PeachLeaf" };
                    var nearLevels = id == "cherry"
                        ? new Dictionary<string, int> { ["CherryPedicel"] = 2, ["CherryBark"] = 1, ["CherryFruit"] = 1 }
                        : new Dictionary<string, int> { ["PeachBark"] = 2, ["PeachSkin"] = 1 };
                    var midLevels = id == "cherry"
                        ? new Dictionary<string, int> { ["CherryPedicel"] = 1, ["CherryBark"] = 1, ["CherryFruit"] = 1 }
                        : new Dictionary<string, int> { ["PeachBark"] = 1, ["PeachSkin"] = 1 };
                    var nearMesh = TreeMesh(near, leaves, $"{Stem(id)}Tree_Near", nearLevels);
                    var model = AddCopy(root, "Model", near, nearMesh.mesh, nearMesh.materials, true);
                    var billboard = Billboard(root, $"{Stem(id)}_{diseaseId}");
                    var far = SourceRenderer(source, "ModelFar");
                    var midMesh = TreeMesh(far, leaves, $"{Stem(id)}Tree_Mid", midLevels);
                    var mid = AddCopy(root, "ModelFar", far, midMesh.mesh, midMesh.materials, false);
                    SetLods(root, (lods.Near, new Renderer[] { model }), (lods.Mid, new Renderer[] { mid }), (lods.Cull, new[] { billboard }));
                }
                else
                {
                    var model = AddCopy(root, "Model", near, near.GetComponent<MeshFilter>().sharedMesh, near.sharedMaterials, true);
                    var billboard = SourceRenderer(source, "Billboard");
                    var far = AddCopy(root, "Billboard", billboard, billboard.GetComponent<MeshFilter>().sharedMesh, billboard.sharedMaterials, false);
                    MakeBatchable(far.gameObject);
                    SetLods(root, (lods.Near, new Renderer[] { model }), (lods.Cull, new Renderer[] { far }));
                }
            });

            var leaf = near.sharedMaterials.FirstOrDefault(m => m != null && m.shader.name == MaterialFactory.FarmRecoveryShader && m.name.Contains("Leaf"))
                       ?? near.sharedMaterials.First(m => m != null && m.shader.name == MaterialFactory.FarmRecoveryShader);
            return Entry(id, diseaseId, AssetDatabase.LoadAssetAtPath<GameObject>(path), leaf);
        }

        // ------------------------------------------------------------------ prefab helpers

        static MeshRenderer SourceRenderer(GameObject prefab, string name)
        {
            var renderer = prefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == name);
            if (renderer == null || renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
                throw new InvalidOperationException($"{AssetDatabase.GetAssetPath(prefab)} has no '{name}' mesh renderer.");
            return renderer;
        }

        /// <summary>A plain renderer standing where <paramref name="source"/> stands relative to its prefab root.</summary>
        static MeshRenderer AddCopy(GameObject root, string name, Renderer source, Mesh mesh, Material[] materials, bool shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            var relative = source.transform.root.worldToLocalMatrix * source.transform.localToWorldMatrix;
            go.transform.SetLocalPositionAndRotation(relative.GetPosition(), relative.rotation);
            go.transform.localScale = relative.lossyScale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>
        /// Bakes a billboard from what the root holds so far (the near mesh) and adds it. Its material blends a healthy
        /// and a diseased atlas with the tiled recovery noise, like every Farm billboard.
        /// </summary>
        static Renderer Billboard(GameObject root, string name)
        {
            var bake = BillboardBaker.Bake(root, name, BillboardViewSize);
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(PlantAssetPipeline.BillboardNoisePath)
                        ?? AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.CreateNoise(PlantAssetPipeline.BillboardNoisePath));
            var material = MaterialFactory.FarmRecovery($"{MaterialFolder}/{name}_Billboard.mat", bake.HealthyTexture, bake.Texture, noise, null,
                true, PlantAssetPipeline.BillboardMaskScale);
            var go = new GameObject("Billboard", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = bake.Mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            MakeBatchable(go);
            return renderer;
        }

        /// <summary>
        /// Billboards are batching-static: a row's distant plants (4 triangles each, one material) are then drawn in one
        /// static batch instead of one draw call per plant. The rows never move; the mesh copies cost almost nothing.
        /// </summary>
        internal static void MakeBatchable(GameObject billboard) =>
            GameObjectUtility.SetStaticEditorFlags(billboard, StaticEditorFlags.BatchingStatic);

        static void SetLods(GameObject root, params (float until, Renderer[] renderers)[] levels)
        {
            if (!root.TryGetComponent<LODGroup>(out var group))
                group = root.AddComponent<LODGroup>();
            group.SetLODs(levels.Select(l => new LOD(l.until, l.renderers)).ToArray());
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }

        /// <summary>Rebuilds an existing prefab in place (GUID kept) or creates it.</summary>
        internal static void CreateOrRebuild(string path, Action<GameObject> build)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                BlenderTreeBuilder.Rebuild(path, build);
                return;
            }

            var created = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                build(created);
                PrefabUtility.SaveAsPrefabAsset(created, path);
            }
            finally
            {
                Object.DestroyImmediate(created);
            }
        }

        internal static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Through the Mesh API, not EditorUtility.CopySerialized: that writes the right data to disk but leaves the
            // loaded mesh's render data stale for the rest of the session (the grape leaves vanished from the
            // billboard bakes and every render until the asset was reloaded).
            CopyMesh(mesh, existing);
            existing.name = mesh.name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>Replaces <paramref name="target"/>'s geometry with <paramref name="source"/>'s, every vertex channel kept.</summary>
        static void CopyMesh(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.vertices = source.vertices;
            if (source.HasVertexAttribute(VertexAttribute.Normal))
                target.normals = source.normals;
            if (source.HasVertexAttribute(VertexAttribute.Tangent))
                target.tangents = source.tangents;
            if (source.HasVertexAttribute(VertexAttribute.Color))
                target.colors32 = source.colors32;
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute))
                    continue;
                var uvs = new List<Vector4>();
                source.GetUVs(channel, uvs);
                switch (source.GetVertexAttributeDimension(attribute))
                {
                    case 2: target.SetUVs(channel, uvs.Select(v => (Vector2)v).ToList()); break;
                    case 3: target.SetUVs(channel, uvs.Select(v => (Vector3)v).ToList()); break;
                    default: target.SetUVs(channel, uvs); break;
                }
            }

            target.subMeshCount = source.subMeshCount;
            for (var s = 0; s < source.subMeshCount; s++)
                target.SetTriangles(source.GetTriangles(s), s, false);
            target.bounds = source.bounds;
        }
    }
}
