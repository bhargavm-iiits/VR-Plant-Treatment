using System.Collections.Generic;
using System.IO;
using BTP.Core;
using BTP.Farm;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Builds Treatment.unity (keeping its existing GUID) and connects it to the rest of the project:
    /// the bootstrap's TreatmentServices and scene mapping, the visiting Lab's Enter Treatment Lab
    /// button and status, the Farm's per-plant treatment status, and the build scene list.
    /// </summary>
    public static class TreatmentSceneBuilder
    {
        const float Eye = 1.6f;
        /// <summary>The default view turns this far right of the specimen, so the specimen and task panel are both in front.</summary>
        public const float ViewYawOffset = 16f;
        /// <summary>Task panel (with the action buttons): right of the specimen, inside the default view.</summary>
        public const float TaskPanelYaw = 40f;
        const string MistMaterial = ProjectPaths.Root + "/Materials/Treatment/Mist.mat";

        [MenuItem("BTP/Treatment/3 - Build Treatment Scene", priority = 42)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before building scenes.");
            var catalog = AssetDatabase.LoadAssetAtPath<TreatmentAssetCatalog>(TreatmentAssetBuilder.CatalogPath);
            if (catalog == null)
                throw new System.InvalidOperationException("Run BTP/Treatment/2 - Build Treatment Assets first.");

            // Open the existing scene and replace its contents, so its GUID (and any references) survive.
            var scene = File.Exists(ProjectPaths.TreatmentScene)
                ? EditorSceneManager.OpenScene(ProjectPaths.TreatmentScene, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var root in scene.GetRootGameObjects())
                Object.DestroyImmediate(root);

            EnvironmentKit.DisableGlobalIllumination(ProjectPaths.Data + "/Lighting/TreatmentLighting.lighting");
            var lighting = new GameObject("Lighting").transform;
            EnvironmentKit.ConfigureLighting(lighting,
                sunColor: new Color(1f, 0.97f, 0.9f), sunIntensity: 1.2f, sunEuler: new Vector3(50f, 30f, 0f),
                skyTint: new Color(0.45f, 0.6f, 0.82f),
                ambientSky: new Color(0.64f, 0.74f, 0.86f), ambientEquator: new Color(0.58f, 0.62f, 0.62f),
                ambientGround: new Color(0.36f, 0.36f, 0.33f),
                fogColor: new Color(0.74f, 0.82f, 0.9f), fogStart: 80f, fogEnd: 600f,
                skyboxPath: ProjectPaths.EnvironmentMaterials + "/LabSky.mat");
            var scenery = new GameObject("Scenery").transform;
            var hills = MaterialFactory.Solid($"{ProjectPaths.EnvironmentMaterials}/Lab_Mountains.mat", Color.white, 0f, ProceduralTextures.Mountain);
            EnvironmentKit.MeshObject("Mountains", scenery, ProceduralMeshes.MountainRing("FarmMountainsTall", 210f, 330f, 45f, 125f, 5), hills,
                Vector3.zero, Quaternion.Euler(0f, 30f, 0f), Vector3.one);
            var grass = MaterialFactory.Solid($"{ProjectPaths.EnvironmentMaterials}/Lab_Grass.mat", Color.white, 0.05f, ProceduralTextures.Grass);
            EnvironmentKit.Primitive(PrimitiveType.Plane, "Ground", scenery, new Vector3(0f, -0.08f, 0f), new Vector3(60f, 1f, 60f), grass, false,
                Quaternion.identity, false);

            var treatment = new GameObject("Treatment");
            var environmentRoot = new GameObject("EnvironmentRoot").transform;
            environmentRoot.SetParent(treatment.transform, false);
            var ui = new GameObject("Interface").transform;
            ui.SetParent(treatment.transform, false);
            ui.localPosition = new Vector3(0f, 0f, -1.35f);
            var rack = new GameObject("ToolRack").transform;
            rack.SetParent(treatment.transform, false);

            // The step-lesson task and case-notes windows were removed: the Manual treatment panels are the lab's UI.
            BuildNavigation(ui);
            var selector = BuildSelector(ui);
            var tooltip = BuildTooltip(treatment.transform, out var tooltipText);

            var navigation = new GameObject("Navigation").transform;
            var spawn = new GameObject("SpawnAnchor", typeof(SpawnAnchor));
            spawn.transform.SetParent(navigation, false);
            spawn.transform.localPosition = new Vector3(0f, 0f, -1.35f);

            var animator = treatment.AddComponent<TreatmentActionAnimator>();
            animator.Bind(AssetDatabase.LoadAssetAtPath<Material>(MistMaterial));
            var controller = treatment.AddComponent<TreatmentController>();
            controller.Bind(environmentRoot, spawn.GetComponent<SpawnAnchor>(), ui, rack, null, null, tooltip, tooltipText, animator, ViewYawOffset);
            // Manual treatment: button-only panels and the learner picker, sharing this room and specimen.
            ManualTreatmentSceneBuilder.Build(treatment.transform, ui, selector, controller);
            // Vineyard view, the case sign and the hand-held Training spray (they stay while case rooms are swapped).
            TreatmentLabStationBuilder.Build(treatment.transform, scenery, controller);

            new GameObject("BootstrapGuard", typeof(BootstrapGuard));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ProjectPaths.TreatmentScene);
            BootstrapSceneBuilder.UpdateBuildScenes();
            Debug.Log($"[BTP] Built {ProjectPaths.TreatmentScene}.");
        }

        [MenuItem("BTP/Treatment/4 - Connect Bootstrap, Lab and Farm", priority = 43)]
        public static void Integrate()
        {
            if (EditorApplication.isPlaying)
                throw new System.InvalidOperationException("Exit Play mode before updating scenes.");

            var bootstrap = EditorSceneManager.OpenScene(ProjectPaths.BootstrapScene, OpenSceneMode.Single);
            var systems = GameObject.Find("BTP Systems");
            if (systems == null)
                throw new System.InvalidOperationException("XRBootstrap has no 'BTP Systems' object.");
            WireBootstrap(systems);
            EditorSceneManager.MarkSceneDirty(bootstrap);
            EditorSceneManager.SaveScene(bootstrap);

            var lab = EditorSceneManager.OpenScene(ProjectPaths.LabScene, OpenSceneMode.Single);
            var exit = FindInScene(lab, "ExitPanel");
            if (exit == null)
                throw new System.InvalidOperationException("Lab has no ExitPanel.");
            AddLabTreatmentControls((RectTransform)exit);
            EditorSceneManager.MarkSceneDirty(lab);
            EditorSceneManager.SaveScene(lab);

            var farm = EditorSceneManager.OpenScene(ProjectPaths.FarmScene, OpenSceneMode.Single);
            var count = 0;
            foreach (var plant in Object.FindObjectsByType<SelectablePlant>(FindObjectsInactive.Include))
                count += AddFarmStatus(plant) ? 1 : 0;
            EditorSceneManager.MarkSceneDirty(farm);
            EditorSceneManager.SaveScene(farm);

            BootstrapSceneBuilder.UpdateBuildScenes();
            Debug.Log($"[BTP] Treatment connected: bootstrap services, Lab button and status, {count} Farm status labels.");
        }

        static Transform FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name)
                        return t;
            return null;
        }

        /// <summary>Adds TreatmentServices to XRBootstrap's systems object and maps the Treatment scene.</summary>
        public static void WireBootstrap(GameObject systems)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TreatmentAssetCatalog>(TreatmentAssetBuilder.CatalogPath);
            if (catalog == null)
                throw new System.InvalidOperationException("Run BTP/Treatment/2 - Build Treatment Assets first.");
            var services = systems.GetComponent<TreatmentServices>() ?? systems.AddComponent<TreatmentServices>();
            services.Bind(catalog);
            EditorUtility.SetDirty(services);
            var environment = systems.GetComponent<EnvironmentController>();
            if (environment != null)
            {
                environment.BindTreatment(Path.GetFileNameWithoutExtension(ProjectPaths.TreatmentScene));
                EditorUtility.SetDirty(environment);
            }
        }

        /// <summary>Enter Treatment Lab button and the selected case's saved treatment status in the visiting Lab.</summary>
        public static void AddLabTreatmentControls(RectTransform panel)
        {
            foreach (var name in new[] { "EnterTreatment", "TreatmentStatus" })
            {
                var existing = panel.Find(name);
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);
            }

            const float width = 440f;
            panel.sizeDelta = new Vector2(width, 660f);
            var enter = UIFactory.Button("EnterTreatment", panel, "Enter Treatment Lab", 32f, out _, new Color(0.06f, 0.42f, 0.3f, 0.98f), Color.white);
            UIFactory.Place((RectTransform)enter.transform, 28f, 326f, width - 56f, 110f);
            enter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.EnterTreatment;
            var status = UIFactory.Text("TreatmentStatus", panel, "", 22f, UIPalette.TextSecondary);
            UIFactory.Place(status.rectTransform, 28f, 452f, width - 56f, 190f);
            status.gameObject.AddComponent<TreatmentStatusReadout>().Bind(status, null);
        }

        /// <summary>
        /// A treatment status line under a Farm planting's name sign: for a disease row, that case's status; for a plant
        /// specimen, the plant's latest case. Also called by FarmSceneBuilder, so a fresh Farm build is complete.
        /// </summary>
        internal static bool AddFarmStatus(SelectablePlant plant)
        {
            var root = plant.transform;
            var existing = root.Find("TreatmentStatus");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
            var sign = root.Find("NameSign") as RectTransform;
            if (sign == null)
                return false;
            // Just under the sign, whatever its height (the row labels are taller than the old specimen signs).
            var below = sign.sizeDelta.y * UIFactory.MillimetreScale * 0.5f + 0.045f;
            var width = Mathf.Max(420f, sign.sizeDelta.x);
            var canvas = UIFactory.WorldCanvas("TreatmentStatus", root, new Vector2(width, 64f), sign.localPosition + sign.localRotation * new Vector3(0f, -below, 0f), sign.localRotation);
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, new Color(0.07f, 0.1f, 0.13f, 0.9f), false);
            var text = UIFactory.Text("Text", rect, "", 20f, Color.white, TextAlignmentOptions.Center);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 22f;
            UIFactory.Stretch(text.rectTransform, 6f);
            canvas.gameObject.AddComponent<TreatmentStatusReadout>().Bind(text, plant.PlantId, plant.DiseaseId);
            // A disease row's status hides with its label.
            if (plant.IsRow)
                canvas.gameObject.AddComponent<CanvasDistanceCull>().Bind(FarmDiseaseRows.LabelViewDistance);
            return true;
        }

        // ------------------------------------------------------------------ panels (Interface local space: viewer at the origin)

        internal static Vector3 PanelPosition(float yaw, float height, float distance) =>
            Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance + Vector3.up * height;

        internal static RectTransform Panel(Transform parent, string name, Vector2 size, float yaw, float height, float distance)
        {
            var position = PanelPosition(yaw, height, distance);
            var canvas = UIFactory.WorldCanvas(name, parent, size, position, UIFactory.Facing(position, Vector3.up * Eye));
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect);
            return rect;
        }

        internal static TMP_Text Text(RectTransform parent, string name, string text, float size, Color color, float x, float y, float w, float h,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var label = UIFactory.Text(name, parent, text, size, color, alignment, style);
            UIFactory.Place(label.rectTransform, x, y, w, h);
            return label;
        }

        /// <summary>Navigation: back to the visiting Lab and recenter (the step lesson's "Reset this case" was removed with its panels).</summary>
        static void BuildNavigation(Transform ui)
        {
            const float vw = 440f;
            var nav = Panel(ui, "NavigationPanel", new Vector2(vw, 310f), -98f, 1.25f, 1.1f);
            Text(nav, "Title", "Navigation", 36f, Color.white, 28f, 20f, vw - 56f, 50f, FontStyles.Bold);
            var back = UIFactory.Button("ReturnToLab", nav, "Return to Visiting Lab", 30f, out _, UIPalette.Cyan, new Color(0.02f, 0.11f, 0.14f));
            UIFactory.Place((RectTransform)back.transform, 28f, 84f, vw - 56f, 110f);
            back.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.ReturnToLab;
            var recenter = UIFactory.Button("Recenter", nav, "Recenter view", 28f, out _);
            UIFactory.Place((RectTransform)recenter.transform, 28f, 210f, vw - 56f, 76f);
            recenter.gameObject.AddComponent<EnvironmentButton>().Action = EnvironmentButton.ButtonAction.Recenter;
        }

        static RectTransform BuildSelector(Transform ui)
        {
            const float w = 620f;
            // Taller than the step-lesson selector alone: the manual mode adds the mode switch and learner picker below.
            var selector = Panel(ui, "CaseSelectorPanel", new Vector2(w, ManualTreatmentSceneBuilder.SelectorHeight), -64f, 1.5f, 1.15f);
            Text(selector, "Title", "Choose a case", 36f, Color.white, 28f, 18f, w - 56f, 48f, FontStyles.Bold);
            Text(selector, "Subtitle", "Shared with the visiting Lab", 20f, UIPalette.Cyan, 28f, 64f, w - 56f, 30f);

            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            var plantIds = new List<string>();
            var plantButtons = new List<Button>();
            var plantStates = new List<SelectedStateVisual>();
            // All 19 crops (nine Farm + ten Lab-only), eight a page in a 4 x 2 grid with the pager below.
            foreach (var plant in catalog.AllPlants)
            {
                var button = UIFactory.Button("Plant_" + plant.PlantId, selector, plant.DisplayName, 22f, out var state);
                plantIds.Add(plant.PlantId);
                plantButtons.Add(button);
                plantStates.Add(state);
            }

            LabOnlyCropSetup.LayoutTreatmentSelector(selector, plantIds, plantButtons);

            Text(selector, "DiseaseTitle", "Disease or pest", 24f, UIPalette.TextSecondary, 28f, 322f, w - 56f, 34f, FontStyles.Bold);
            var diseaseButtons = new List<Button>();
            var diseaseStates = new List<SelectedStateVisual>();
            for (var d = 0; d < 9; d++)
            {
                var button = UIFactory.Button("Disease_" + d, selector, "", 20f, out var state);
                var label = button.GetComponentInChildren<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 14f;
                label.fontSizeMax = 20f;
                UIFactory.Place((RectTransform)button.transform, 28f + (d % 2) * 286f, 364f + (d / 2) * 88f, 278f, 80f);
                diseaseButtons.Add(button);
                diseaseStates.Add(state);
            }

            selector.gameObject.AddComponent<TreatmentCaseSelector>().Bind(plantIds, plantButtons, plantStates, diseaseButtons, diseaseStates);
            return selector;
        }

        static GameObject BuildTooltip(Transform parent, out TMP_Text text)
        {
            var canvas = UIFactory.WorldCanvas("Tooltip", parent, new Vector2(320f, 56f), Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>());
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect);
            text = UIFactory.Text("Text", rect, "", 22f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 22f;
            UIFactory.Stretch(text.rectTransform, 6f);
            return canvas.gameObject;
        }
    }
}