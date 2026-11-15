using System;
using System.Collections.Generic;
using System.Linq;
using BTP.Lab;
using BTP.Plants;
using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Adds the ten Lab-only crops (34 leaf specimens, <see cref="DiseaseClasses.LabOnly"/>) to the Lab and the
    /// Treatment lab without regenerating either scene: it creates their PlantDefinitions (no Farm model; the
    /// default leaf specimen is the labelled Lab preview), lists them in the PlantCatalog's Lab-only list, and
    /// patches only the crop selector panels (new buttons, paging, the specimen note box) of the saved scenes.
    /// Farm rows, Farm models and CropScan XR mapping read <see cref="PlantCatalog.Plants"/> and
    /// <see cref="DiseaseClasses.All"/>, which are unchanged. Running it again updates in place.
    /// </summary>
    public static class LabOnlyCropSetup
    {
        // id, display name, scientific name, family, type, summary
        static readonly (string id, string name, string scientific, string family, string type, string summary)[] Crops =
        {
            ("blueberry", "Blueberry", "Vaccinium corymbosum", "Ericaceae", "Perennial fruit shrub",
             "Acid-soil shrub grown for its berries. Leaf rust needs an alternate conifer host and long leaf wetness."),
            ("chilli", "Chilli", "Capsicum annuum", "Solanaceae", "Warm-season fruiting vegetable",
             "Hot pepper grown for its pungent fruit. Whitefly-borne leaf curl virus is a major constraint in South Asia."),
            ("cotton", "Cotton", "Gossypium hirsutum", "Malvaceae", "Annual fibre crop",
             "Shrubby annual grown for the lint on its seed. Leaves are broad and lobed on long petioles."),
            ("mango", "Mango", "Mangifera indica", "Anacardiaceae", "Evergreen tropical fruit tree",
             "Large evergreen tree with leathery lance-shaped leaves; new flushes are the most susceptible to disease."),
            ("orange", "Orange", "Citrus sinensis", "Rutaceae", "Evergreen citrus tree",
             "Sweet orange. Huanglongbing, spread by the Asian citrus psyllid, is the most damaging citrus disease."),
            ("rice", "Rice", "Oryza sativa", "Poaceae", "Annual cereal grass",
             "Staple grain grown mostly in flooded paddies; long narrow leaf blades with parallel veins."),
            ("soybean", "Soybean", "Glycine max", "Fabaceae", "Annual legume",
             "Oilseed and protein legume with trifoliate leaves; it fixes nitrogen through root nodules."),
            ("squash", "Squash", "Cucurbita pepo", "Cucurbitaceae", "Annual vine vegetable",
             "Summer squash and pumpkin relatives with large lobed leaves on hollow petioles."),
            ("sugarcane", "Sugarcane", "Saccharum spp. hybrids", "Poaceae", "Perennial grass grown for sugar",
             "Tall grass propagated from stem cuttings (setts), so diseases often travel with the planting material."),
            ("wheat", "Wheat", "Triticum aestivum", "Poaceae", "Annual cereal grass",
             "Bread wheat; narrow leaf blades whose flag leaf supplies most of the grain fill."),
        };

        // Lab plant panel (560 x 960): ten buttons a page, then the pager row.
        internal const int LabPageSize = 10;
        const float LabWidth = 560f, LabTop = 140f, LabStep = 74f, LabHeight = 68f, LabPagerY = 884f, LabPagerHeight = 64f;
        // Treatment selector (620 wide): crops in a 4 x 2 grid where the 3 x 3 grid was, pager in its third row.
        internal const int TreatmentPageSize = 8;
        const float TreatmentWidth = 620f, TreatmentTop = 104f, TreatmentColumn = 141f, TreatmentStep = 70f, TreatmentHeight = 62f;

        [MenuItem("BTP/Setup/8 - Add Lab-only Crops (Lab + Treatment)", priority = 8)]
        public static void ApplyFromMenu()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Apply();
        }

        /// <summary>Plant definitions and catalog, then the two scene patches.</summary>
        public static void Apply()
        {
            CreatePlantDefinitions();
            PatchLabScene();
            PatchTreatmentScene();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Command line: leaf assets, reference images, plant definitions, treatment assets and both scene patches.
        /// Unity.exe -batchmode -projectPath . -executeMethod BTP.Editor.LabOnlyCropSetup.RunAll -quit
        /// </summary>
        public static void RunAll()
        {
            var result = Automation.Run("lab_only_crops", () =>
            {
                LeafAssetPipeline.Build();
                DiseaseGalleryBuilder.Build();
                CreatePlantDefinitions();
                TreatmentAssetBuilder.Build();
                PatchLabScene();
                PatchTreatmentScene();
                AssetDatabase.SaveAssets();
            });
            if (Application.isBatchMode && result != "done")
                EditorApplication.Exit(1);
        }

        // ------------------------------------------------------------------ plant definitions

        public static List<PlantDefinition> CreatePlantDefinitions()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            var leaves = AssetDatabase.LoadAssetAtPath<LeafCatalog>(LeafAssetPipeline.CatalogPath);
            if (catalog == null || leaves == null)
                throw new InvalidOperationException("PlantCatalog or LeafCatalog missing: run Build Plant Assets and Prepare Leaf Display Assets first.");
            ProjectPaths.EnsureFolder(ProjectPaths.PlantData);
            var definitions = new List<PlantDefinition>();
            foreach (var crop in Crops)
            {
                var path = $"{ProjectPaths.PlantData}/{crop.id}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<PlantDefinition>(path);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<PlantDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                var disease = DiseaseClasses.DefaultDiseaseId(crop.id);
                if (!leaves.TryGet(crop.id, disease, out var leaf))
                    throw new InvalidOperationException($"Leaf specimen missing for {crop.id}/{disease}: run Prepare Leaf Display Assets.");
                definition.Configure(crop.id, crop.name, crop.scientific, crop.family, crop.type, crop.summary, true);
                // The Lab preview is the crop's default leaf specimen (the info panel labels it). The Farm model is
                // FarmCropBuilder's, kept when it has been built (BTP/Setup/27).
                definition.SetPrefabs(AssetDatabase.LoadAssetAtPath<GameObject>(FarmCropBuilder.FarmPrefab(crop.id)), leaf);
                EditorUtility.SetDirty(definition);
                definitions.Add(definition);
            }

            catalog.SetLabOnlyPlants(definitions);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            var problems = catalog.Validate();
            if (problems.Count > 0)
                throw new InvalidOperationException("Plant catalog: " + string.Join("; ", problems));
            Debug.Log($"[BTP] Plant catalog: {catalog.Plants.Count} Farm plants + {catalog.LabOnlyPlants.Count} Lab-only crops.");
            return definitions;
        }

        // ------------------------------------------------------------------ shared selector layouts (also used by the scene builders)

        /// <summary>One Lab plant button: bold name with the scientific name below, as on the original panel.</summary>
        internal static Button LabPlantButton(RectTransform panel, PlantDefinition plant)
        {
            var text = $"<b>{plant.DisplayName}</b>\n<size=70%><i>{plant.ScientificName}</i></size>";
            var button = UIFactory.Button($"Plant_{plant.PlantId}", panel, text, 30f, out var selected);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.margin = new Vector4(18f, 0f, 0f, 0f);
            button.gameObject.AddComponent<PlantButton>().Bind(plant.PlantId, selected);
            return button;
        }

        /// <summary>
        /// Lays out the Lab plant buttons ten a page and binds the pager: page 1 is the nine Farm crops (the original
        /// panel), page 2 the ten Lab-only crops.
        /// </summary>
        internal static void LayoutLabPanel(RectTransform panel, IReadOnlyList<PlantDefinition> plants, IReadOnlyList<Button> buttons)
        {
            var pager = Pager(panel, "PlantPager", LabWidth, LabPagerY, LabPagerHeight, 120f, out var previous, out var next, out var text);
            var farm = plants.Count(p => !DiseaseClasses.IsLabOnlyPlant(p.PlantId));
            var items = new List<GameObject>();
            var ids = new List<string>();
            for (var i = 0; i < plants.Count; i++)
            {
                // Pad the Farm page so the Lab-only crops always start a new page.
                if (i == farm)
                    while (items.Count % LabPageSize != 0)
                    {
                        items.Add(null);
                        ids.Add(string.Empty);
                    }

                items.Add(buttons[i].gameObject);
                ids.Add(plants[i].PlantId);
                var label = buttons[i].GetComponentInChildren<TextMeshProUGUI>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 20f;
                label.fontSizeMax = 28f;
                UIFactory.Place((RectTransform)buttons[i].transform, 28f, LabTop + (items.Count - 1) % LabPageSize * LabStep, LabWidth - 56f, LabHeight);
            }

            var titles = farm > 0 && farm <= LabPageSize ? new List<string> { "PlantVillage crops", "Field and tropical crops" } : new List<string>();
            pager.Bind(ids, items, LabPageSize, titles, previous, next, text);
        }

        /// <summary>Lays out the Treatment crop buttons four by two a page, with the pager in the third row.</summary>
        internal static void LayoutTreatmentSelector(RectTransform selector, IReadOnlyList<string> plantIds, IReadOnlyList<Button> buttons)
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                var slot = i % TreatmentPageSize;
                UIFactory.Place((RectTransform)buttons[i].transform, 28f + slot % 4 * TreatmentColumn, TreatmentTop + slot / 4 * TreatmentStep,
                    TreatmentColumn - 8f, TreatmentHeight);
                var label = buttons[i].GetComponentInChildren<TMP_Text>();
                label.enableAutoSizing = true;
                label.fontSizeMin = 16f;
                label.fontSizeMax = 22f;
            }

            var pager = Pager(selector, "CropPager", TreatmentWidth, TreatmentTop + 2f * TreatmentStep, TreatmentHeight, TreatmentColumn - 8f,
                out var previous, out var next, out var text);
            pager.Bind(plantIds.ToList(), buttons.Select(b => b.gameObject).ToList(), TreatmentPageSize, new List<string> { "Crops" },
                previous, next, text);
        }

        static PlantButtonPager Pager(RectTransform panel, string name, float width, float y, float height, float buttonWidth,
            out Button previous, out Button next, out TMP_Text text)
        {
            var existing = panel.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
            // A full-panel node without a graphic, so it never blocks the buttons it sits above.
            var row = UIFactory.Node(name, panel);
            UIFactory.Place(row, 0f, 0f, width, y + height);
            previous = UIFactory.Button("Previous", row, "◀  Prev", 24f, out _);
            UIFactory.Place((RectTransform)previous.transform, 28f, y, buttonWidth, height);
            next = UIFactory.Button("Next", row, "Next  ▶", 24f, out _);
            UIFactory.Place((RectTransform)next.transform, width - 28f - buttonWidth, y, buttonWidth, height);
            text = UIFactory.Text("PageLabel", row, "", 22f, UIPalette.Cyan, TextAlignmentOptions.Center, FontStyles.Bold);
            text.raycastTarget = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14f;
            text.fontSizeMax = 22f;
            UIFactory.Place(text.rectTransform, 28f + buttonWidth + 8f, y, width - 56f - 2f * (buttonWidth + 8f), height);
            row.SetAsLastSibling();
            return row.gameObject.AddComponent<PlantButtonPager>();
        }

        // ------------------------------------------------------------------ scene patches

        public static void PatchLabScene()
        {
            var scene = Open(ProjectPaths.LabScene);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            var panel = Find<RectTransform>(scene, "PlantPanel");
            var plants = catalog.AllPlants;
            var buttons = new List<Button>();
            foreach (var plant in plants)
            {
                var child = panel.Find($"Plant_{plant.PlantId}");
                buttons.Add(child != null ? child.GetComponent<Button>() : LabPlantButton(panel, plant));
            }

            var subtitle = panel.Find("Subtitle")?.GetComponent<TMP_Text>();
            if (subtitle != null)
                subtitle.text = $"Choose a species to study ({plants.Count} crops)";
            LayoutLabPanel(panel, plants, buttons);

            // Room for a specimen note (e.g. a supplied model that does not match its label) under the leaf.
            var display = Object.FindObjectsByType<LabPlantDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(d => d.gameObject.scene == scene);
            var note = display != null ? display.transform.Find("ScaleNote") : null;
            if (note != null)
            {
                ((RectTransform)note).sizeDelta = new Vector2(640f, 120f);
                var text = note.GetComponentInChildren<TMP_Text>();
                text.enableAutoSizing = true;
                text.fontSizeMin = 14f;
                text.fontSizeMax = 22f;
                EditorUtility.SetDirty(text);
            }

            Save(scene);
            Debug.Log($"[BTP] Lab plant panel: {plants.Count} crops ({catalog.Plants.Count} Farm + {catalog.LabOnlyPlants.Count} Lab-only), paged.");
        }

        public static void PatchTreatmentScene()
        {
            var scene = Open(ProjectPaths.TreatmentScene);
            var catalog = AssetDatabase.LoadAssetAtPath<PlantCatalog>(ProjectPaths.Catalog);
            var selector = Find<TreatmentCaseSelector>(scene, "CaseSelectorPanel");
            var rect = (RectTransform)selector.transform;
            var serialized = new SerializedObject(selector);
            var diseaseButtons = Read<Button>(serialized, "diseaseButtons");
            var diseaseStates = Read<SelectedStateVisual>(serialized, "diseaseStates");
            var largest = DiseaseClasses.AllCases.GroupBy(i => i.PlantId).Max(g => g.Count());
            if (diseaseButtons.Count < largest)
                throw new InvalidOperationException($"The Treatment disease grid has {diseaseButtons.Count} buttons; a crop has {largest} conditions.");

            var ids = new List<string>();
            var buttons = new List<Button>();
            var states = new List<SelectedStateVisual>();
            foreach (var plant in catalog.AllPlants)
            {
                var child = rect.Find("Plant_" + plant.PlantId);
                Button button;
                SelectedStateVisual state;
                if (child != null)
                {
                    button = child.GetComponent<Button>();
                    state = child.GetComponent<SelectedStateVisual>();
                }
                else
                {
                    button = UIFactory.Button("Plant_" + plant.PlantId, rect, plant.DisplayName, 22f, out state);
                    // Keep the new button with the crop buttons, before the disease grid in the hierarchy.
                    var anchor = rect.Find("DiseaseTitle");
                    if (anchor != null)
                        button.transform.SetSiblingIndex(anchor.GetSiblingIndex());
                }

                ids.Add(plant.PlantId);
                buttons.Add(button);
                states.Add(state);
            }

            LayoutTreatmentSelector(rect, ids, buttons);
            selector.Bind(ids, buttons, states, diseaseButtons, diseaseStates);
            EditorUtility.SetDirty(selector);
            Save(scene);
            Debug.Log($"[BTP] Treatment case selector: {ids.Count} crops, {DiseaseClasses.AllCases.Count} cases.");
        }

        static List<T> Read<T>(SerializedObject serialized, string property) where T : Object
        {
            var list = serialized.FindProperty(property);
            var result = new List<T>();
            for (var i = 0; i < list.arraySize; i++)
                result.Add(list.GetArrayElementAtIndex(i).objectReferenceValue as T);
            return result;
        }

        static Scene Open(string path)
        {
            var scene = SceneManager.GetSceneByPath(path);
            return scene.isLoaded ? scene : EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"Could not save {scene.path}.");
        }

        static T Find<T>(Scene scene, string name) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == name && candidate.TryGetComponent<T>(out var component))
                    return component;
            throw new InvalidOperationException($"{name} not found in {scene.path}.");
        }
    }
}
