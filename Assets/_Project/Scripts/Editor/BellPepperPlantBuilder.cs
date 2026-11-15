using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Editor
{
    /// <summary>
    /// Replaces the bell pepper with the Blender plant (Assets/Plant_Asstes/Pepper/Bell Pepper/Bell_Pepper_Plant/
    /// Bell_Pepper_Plant_3D_Wind_8s.blend), exported for VR by Tools/blender_bell_pepper_plant.py into
    /// Models/BellPepperPlant: the 1.5 m source plant scaled to 0.9 m for the Farm beds, its 31 Meshy bacterial-spot
    /// leaves as curved cut-out cards, six decimated peppers and the flowers on one atlas, the curve stems, two mesh
    /// LODs plus a baked billboard, and the blend's 8 s leaf wind replayed by the Farm Recovery shader (_LEAF_WIND,
    /// baked by <see cref="LeafWindBaker"/>, as the cherry and the tomatoes do).
    ///
    /// BellPepper_Farm.prefab and BellPepper_Lab.prefab are rebuilt in place (GUIDs and every scene/prefab instance
    /// kept). The leaf material is the Farm Recovery pair (the supplied diseased leaf, and a healthy copy with its
    /// lesions recoloured green), so the Farm six-week timeline still fades the disease; fruit, flowers and stems are
    /// plain Simple Lit and never change. Run BTP/Setup/26 - Build Farm Disease Variants afterwards so the bell pepper
    /// disease row picks up the new leaf material.
    /// </summary>
    public static class BellPepperPlantBuilder
    {
        public const string Folder = ProjectPaths.Root + "/Models/BellPepperPlant";
        public const string ModelPath = Folder + "/BellPepper.fbx";
        public const string FarmPrefab = ProjectPaths.PlantPrefabs + "/BellPepper_Farm.prefab";
        public const string LabPrefab = ProjectPaths.PlantPrefabs + "/BellPepper_Lab.prefab";
        public const string LeafMaterialPath = Folder + "/BellPepperLeaf.mat";
        public const string WindTexturePath = Folder + "/BellPepper_Wind.asset";

        // LOD switch points (screen-relative height). Quest (LOD bias 1) keeps a level to about height / (2.2 x value):
        // for the 0.9 m plant the ~12k-triangle mesh to 0.9 / (2.2 x 0.12) = 3.4 m, the ~4k mesh to about 6.8 m, then
        // the 4-triangle billboard to ~68 m; PC about twice as far. The Farm's pepper bed (10 plants, 0.66 m apart) then
        // shows two or three full meshes from the path, the rest as one static billboard batch.
        const float NearUntil = 0.12f;
        const float FarUntil = 0.06f;
        const float CullAt = 0.006f;
        // Per-view billboard bake size (atlas 1024 x 512): plenty for a plant that is under 0.06 of the screen.
        const int BillboardViewSize = 512;
        // The leaf sprite is 1024 x 512; Quest gets 512 on its long side, ample at arm's length.
        const int LeafTextureSize = 1024;
        const int LeafTextureAndroid = 512;

        [MenuItem("BTP/Setup/Replace Bell Pepper With Blender Plant (Wind)", priority = 25)]
        public static void Apply()
        {
            // Picks up a fresh Tools/blender_bell_pepper_plant.py export made while the editor was open.
            AssetDatabase.Refresh();
            Build();
        }

        /// <summary>
        /// Imports the export, builds its materials and wind, and rebuilds both prefabs in place. Returns the prefab
        /// assets, or (null, null) with a warning, keeping the old pepper, when the export is missing.
        /// </summary>
        internal static (GameObject farm, GameObject lab) Build()
        {
            if (!File.Exists(ModelPath))
            {
                Debug.LogWarning($"[BTP] {ModelPath} is missing; run Tools/blender_bell_pepper_plant.py first. The old bell pepper is kept.");
                return (null, null);
            }

            var leaf = LeafMaterial();
            var remaps = new[] { ("BellPepperLeaf", leaf), ("BellPepperFruit", FruitMaterial()), ("BellPepperStem", StemMaterial()) };
            // Exact mesh: no compression (wind indices and pivots must survive) and MikkTSpace tangents.
            BlenderTreeBuilder.ConfigureModel(ModelPath, remaps, false, true);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var filters = model.GetComponentsInChildren<MeshFilter>();
            var near = filters.First(f => f.name.Contains("LOD0"));
            var far = filters.First(f => f.name.Contains("LOD1"));
            var wind = LeafWindBaker.Bake($"{Folder}/BellPepper_Wind.bytes", near.sharedMesh, far.sharedMesh, WindTexturePath, "Bell pepper");
            LeafWindBaker.Assign(leaf, wind);

            BlenderTreeBuilder.Rebuild(FarmPrefab, root => FarmContent(root, near, far));
            // The Lab prefab is decoration only (the pedestal shows single leaves): the near mesh, no LODs.
            BlenderTreeBuilder.Rebuild(LabPrefab, root =>
            {
                if (root.TryGetComponent<LODGroup>(out var lod))
                    Object.DestroyImmediate(lod);
                TomatoPlantBuilder.AddPart(root, "Model", near, true);
            });
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Bell pepper replaced with the Blender plant ({near.sharedMesh.bounds.size.y:0.00} m, " +
                      $"LOD0 {TomatoPlantBuilder.Tris(near.sharedMesh)} / LOD1 {TomatoPlantBuilder.Tris(far.sharedMesh)} triangles + billboard).");
            return (AssetDatabase.LoadAssetAtPath<GameObject>(FarmPrefab), AssetDatabase.LoadAssetAtPath<GameObject>(LabPrefab));
        }

        /// <summary>
        /// The leaf card material: the supplied bacterial-spot leaf (diseased, Week 1), a healthy copy (lesions and
        /// halos recoloured to the leaf's own green, Week 6) and the recovery mask, cut-out and double-sided.
        /// </summary>
        static Material LeafMaterial()
        {
            var diseasedPath = $"{Folder}/BellPepperLeaf.png";
            var healthyPath = BlenderTreeBuilder.WriteHealthySprite(diseasedPath, $"{Folder}/BellPepperLeaf_Healthy.png");
            BlenderTreeBuilder.ImportSprite(diseasedPath, LeafTextureSize);
            var healthy = BlenderTreeBuilder.ImportSprite(healthyPath, LeafTextureSize);
            // Cut-out import (coverage-preserving mips, ASTC on Quest); ImportSprite's clamp wrap is kept.
            foreach (var path in new[] { diseasedPath, healthyPath })
                TextureSettings.ConfigureCutout(path, LeafTextureSize, LeafTextureAndroid);
            var diseased = AssetDatabase.LoadAssetAtPath<Texture2D>(diseasedPath);
            healthy = AssetDatabase.LoadAssetAtPath<Texture2D>(healthyPath);
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.Create(healthy, $"{Folder}/BellPepperLeaf_RecoveryMask.png", "bell_pepper"));
            var material = MaterialFactory.FarmRecovery(LeafMaterialPath, healthy, diseased, mask, null, true);
            material.SetFloat("_Cutoff", 0.45f);
            material.doubleSidedGI = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Peppers and flowers: one 1024 atlas (red, green and yellow pepper, petal and anther swatches).</summary>
        static Material FruitMaterial()
        {
            var atlasPath = $"{Folder}/BellPepperFruit.png";
            TextureSettings.ConfigureAlbedo(atlasPath, 1024, 512);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath)
                        ?? throw new FileNotFoundException($"[BTP] {atlasPath} is missing; run Tools/blender_bell_pepper_plant.py first.");
            var fruit = MaterialFactory.SimpleLit($"{Folder}/BellPepperFruit.mat", atlas, null, false);
            MaterialFactory.Glossy(fruit, 0.7f, new Color(0.4f, 0.4f, 0.4f));
            return fruit;
        }

        /// <summary>Stems, petioles, calyces and fruit stalks: the blend's linear stem colour; Simple Lit takes sRGB.</summary>
        static Material StemMaterial() =>
            TomatoPlantBuilder.Matte(MaterialFactory.Solid($"{Folder}/BellPepperStem.mat", new Color(0.13f, 0.235f, 0.025f).gamma, 0.2f));

        static Texture2D BillboardNoise()
        {
            var noise = AssetDatabase.LoadAssetAtPath<Texture2D>(PlantAssetPipeline.BillboardNoisePath);
            return noise != null ? noise : AssetDatabase.LoadAssetAtPath<Texture2D>(FarmRecoveryMasks.CreateNoise(PlantAssetPipeline.BillboardNoisePath));
        }

        /// <summary>Farm prefab content: near mesh, far mesh and billboard under one LODGroup, on the export's origin.</summary>
        static void FarmContent(GameObject root, MeshFilter nearSource, MeshFilter farSource)
        {
            // The billboard is baked from the near mesh alone, before the far mesh joins it.
            var near = TomatoPlantBuilder.AddPart(root, "Model", nearSource, true);
            var bake = BillboardBaker.Bake(root, "BellPepper", BillboardViewSize);
            var billboardMaterial = MaterialFactory.FarmRecovery($"{ProjectPaths.PlantMaterials}/BellPepper_Billboard.mat",
                bake.HealthyTexture, bake.Texture, BillboardNoise(), null, true, PlantAssetPipeline.BillboardMaskScale);
            // The mid mesh casts no shadow: within the 20 m Quest shadow distance only the near mesh does.
            var far = TomatoPlantBuilder.AddPart(root, "ModelFar", farSource, false);
            var billboard = new GameObject("Billboard", typeof(MeshFilter), typeof(MeshRenderer));
            billboard.transform.SetParent(root.transform, false);
            billboard.GetComponent<MeshFilter>().sharedMesh = bake.Mesh;
            var renderer = billboard.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = billboardMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            DiseaseVariantBuilder.MakeBatchable(billboard);

            if (!root.TryGetComponent<LODGroup>(out var group))
                group = root.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(NearUntil, new Renderer[] { near }),
                new LOD(FarUntil, new Renderer[] { far }),
                new LOD(CullAt, new Renderer[] { renderer }),
            });
            group.fadeMode = LODFadeMode.None;
            group.RecalculateBounds();
        }
    }
}
