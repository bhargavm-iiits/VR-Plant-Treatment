using System;
using System.Collections.Generic;
using System.IO;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using SQLite;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Builds everything the Treatment lab resolves through asset keys: the SQLite content asset
    /// (imported straight from treatment_content.sql), leaf materials, tool/target/fixture prefabs,
    /// the four environment prefabs and the TreatmentAssetCatalog that binds the keys. The catalog
    /// stores Unity references only; treatment advice stays in the SQL source.
    /// </summary>
    public static partial class TreatmentAssetBuilder
    {
        public const string DataFolder = ProjectPaths.Data + "/Treatment";
        public const string SqlPath = DataFolder + "/treatment_content.sql";
        public const string CatalogPath = DataFolder + "/TreatmentAssetCatalog.asset";
        const string Prefabs = ProjectPaths.Root + "/Prefabs/Treatment";
        const string Materials = ProjectPaths.Root + "/Materials/Treatment";
        const string ToolsGlb = "Assets/Lab_Treatment_Assets/gardening_tools.glb";
        const string GlovesGlb = "Assets/Lab_Treatment_Assets/work_gloves.glb";

        static readonly List<TreatmentAssetCatalog.Entry> Entries = new List<TreatmentAssetCatalog.Entry>();

        [MenuItem("BTP/Treatment/1 - Import Content Database", priority = 40)]
        public static SQLiteAsset ImportContent()
        {
            if (!File.Exists(SqlPath))
                throw new InvalidOperationException($"{SqlPath} is missing. Run: python Tools/treatment_content_gen.py");
            AssetDatabase.ImportAsset(SqlPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.GetImporterOverride(SqlPath) != typeof(SQLite.Editor.SQLScriptDatabaseImporter))
                AssetDatabase.SetImporterOverride<SQLite.Editor.SQLScriptDatabaseImporter>(SqlPath);
            AssetDatabase.ImportAsset(SqlPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<SQLiteAsset>(SqlPath);
            if (asset == null)
                throw new InvalidOperationException($"{SqlPath} did not import as a SQLite database.");
            using (var db = asset.CreateConnection())
                Debug.Log($"[BTP] Treatment content imported: {db.ExecuteScalar<int>("SELECT COUNT(*) FROM cases")} cases.");
            return asset;
        }

        [MenuItem("BTP/Treatment/2 - Build Treatment Assets", priority = 41)]
        public static TreatmentAssetCatalog Build()
        {
            var content = ImportContent();
            Entries.Clear();
            foreach (var folder in new[] { Prefabs + "/Tools", Prefabs + "/Targets", Prefabs + "/Fixtures", Prefabs + "/Environments", Prefabs + "/UI", Prefabs + "/Specimens", Materials })
                ProjectPaths.EnsureFolder(folder);

            BuildLeafMaterials();
            BuildUi();
            BuildTools();
            BuildTargets();
            BuildFixtures();
            BuildSpecimens();
            BuildLeafTargets();
            BuildLeafSpecimens();
            BuildEnvironments();

            var catalog = AssetDatabase.LoadAssetAtPath<TreatmentAssetCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TreatmentAssetCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Set(content, Entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BTP] Treatment asset catalog: {Entries.Count} bindings.");
            return catalog;
        }

        static void Bind(string key, Object asset)
        {
            if (asset == null)
                throw new InvalidOperationException($"Asset for '{key}' was not created.");
            Entries.RemoveAll(e => e.key == key);
            Entries.Add(new TreatmentAssetCatalog.Entry { key = key, asset = asset });
        }

        // ------------------------------------------------------------------ materials

        static Material Mat(string name, Color color, float smoothness = 0.2f) => MaterialFactory.Solid($"{Materials}/{name}.mat", color, smoothness);
        static Material Clear(string name, Color color) => MaterialFactory.Lit($"{Materials}/{name}.mat", color, 0f, 0.85f, transparent: true, doubleSided: true);
        static Material Glow(string name, Color color, float intensity = 1.6f) => MaterialFactory.Glow($"{Materials}/{name}.mat", color, intensity);

        /// <summary>
        /// A copy of the Lab leaf's own material (BTP/Leaf Recovery masks the leaf outline): diseased
        /// (recovery 0) or the reconstructed healthy foliage (recovery 1), without the comparison split.
        /// </summary>
        static Material LeafMaterial(string name, Material source, float recovery)
        {
            var path = $"{Materials}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = source.shader;
                material.CopyPropertiesFromMaterial(source);
            }

            foreach (var property in new[] { "_Recovery", "_BaselineRecovery", "_RecoveryFrom", "_BaselineFrom" })
                if (material.HasProperty(property))
                    material.SetFloat(property, recovery);
            if (material.HasProperty("_SplitEnabled"))
                material.SetFloat("_SplitEnabled", 0f);
            if (material.HasProperty("_TransitionParams"))
                material.SetVector("_TransitionParams", new Vector4(1f, 0f, 1f, 0f));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void BuildLeafMaterials()
        {
            var leaves = AssetDatabase.LoadAssetAtPath<LeafCatalog>(LeafAssetPipeline.CatalogPath);
            var healthyDone = new HashSet<string>();
            foreach (var info in DiseaseClasses.AllCases)
            {
                if (leaves == null || !leaves.TryGet(info.PlantId, info.DiseaseId, out var prefab))
                    throw new InvalidOperationException($"Leaf model missing for {info.PlantId}/{info.DiseaseId}.");
                var source = prefab.GetComponentInChildren<Renderer>().sharedMaterial;
                Bind($"leafmat.{info.PlantId}.{info.DiseaseId}", LeafMaterial($"Leaf_{info.PlantId}_{info.DiseaseId}", source, 0f));
                if (!healthyDone.Add(info.PlantId))
                    continue;
                var healthy = LeafMaterial($"Leaf_{info.PlantId}_Healthy", source, 1f);
                Bind($"leafmat.healthy.{info.PlantId}", healthy);
                Bind($"leafmat.new_growth.{info.PlantId}", healthy);
            }
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Root(string name) => new GameObject(name);

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material,
            Vector3 euler = default, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(euler));
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void Label(Transform parent, string text, Vector3 position, float width = 300f, float size = 34f, Color? color = null, float yaw = 0f)
        {
            var canvas = UIFactory.WorldCanvas("Label", parent, new Vector2(width, size * 2.6f), position, Quaternion.Euler(0f, yaw, 0f));
            Object.DestroyImmediate(canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>());
            var label = UIFactory.Text("Text", canvas.transform, text, size, color ?? Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            UIFactory.Stretch(label.rectTransform);
        }

        static GameObject Save(GameObject root, string folder, string key)
        {
            var path = $"{Prefabs}/{folder}/{key.Replace('.', '_')}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Bind(key, prefab);
            return prefab;
        }

        static void AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        /// <summary>Copies one mesh of a supplied GLB into a centred child scaled to <paramref name="length"/> metres.</summary>
        static bool GlbPart(Transform parent, string glbPath, string meshName, float length, Vector3 euler)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (glb == null)
                return false;
            foreach (var filter in glb.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshName != null && !filter.name.StartsWith(meshName))
                    continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                var holder = new GameObject("Model");
                holder.transform.SetParent(parent, false);
                holder.transform.localRotation = Quaternion.Euler(euler);
                var go = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                go.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                go.transform.SetParent(holder.transform, false);
                var bounds = filter.sharedMesh.bounds;
                var scale = length / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                go.transform.localScale = Vector3.one * scale;
                go.transform.localPosition = -bounds.center * scale + Vector3.up * (bounds.extents.y * scale);
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ UI

        static void BuildUi()
        {
            var marker = Root("Marker");
            var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(marker.transform, false);
            ring.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * 0.19f;
            ring.GetComponent<MeshFilter>().sharedMesh = ProceduralMeshes.Ring("TreatmentMarkerRing", 0.42f, 0.5f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = Glow("Marker", UIPalette.Cyan, 2.2f);
            ring.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Save(marker, "UI", "ui.marker");

            var label = Root("RackLabel");
            var canvas = UIFactory.WorldCanvas("Canvas", label.transform, new Vector2(150f, 56f), new Vector3(0f, -0.03f, -0.1f), Quaternion.Euler(35f, 0f, 0f));
            Object.DestroyImmediate(canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>());
            UIFactory.Panel((RectTransform)canvas.transform, null, false);
            var text = UIFactory.Text("Text", canvas.transform, "Tool", 19f, Color.white, TextAlignmentOptions.Center);
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f;
            text.fontSizeMax = 19f;
            UIFactory.Stretch(text.rectTransform, 4f);
            Save(label, "UI", "ui.rack_label");
        }
    }
}