using System.Collections;
using System.Collections.Generic;
using BTP.Core;
using BTP.Treatment;
using BTP.UI;
using BTP.XR;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BTP.Farm
{
    /// <summary>
    /// The Farm end of the after-treatment story (<see cref="RecoveryFlow"/>). Once the visiting Lab has shown the treated
    /// leaf's six weeks, EVERY Farm plant with the treated crop and disease turns healthy: the whole disease row, the treated
    /// specimen, and any other plant in the Farm drawn with that disease's own recovery materials. Each plant gets its own
    /// material copies (<see cref="FarmSpecimenVisual"/>: near mesh and billboard LOD together), so other diseases, even of
    /// the same crop, are untouched. A partly effective treatment heals them all part way, as in the Lab. They stay healed
    /// for the session, and each row plant's result is saved to the learner progress (<see cref="SpecimenTreatmentResults"/>),
    /// so later sessions keep them. The first time, "See changes in the Farm" places the user beside the treated plant
    /// (<see cref="PrepareArrival"/>), it is selected (bright ring and name card), the plants heal once the view fades in,
    /// and a green "Recovered · N plants" label floats over the treated plant for a few seconds.
    /// One pass runs per Farm load, after every planting has woken (so saved per-plant results cannot undo it).
    /// </summary>
    public sealed class RecoveredPlantHighlight : MonoBehaviour
    {
        public const string LabelTitle = "Recovered";
        public const float LabelSeconds = 14f;
        /// <summary>How far (m) from the plant "See changes in the Farm" puts the user.</summary>
        public const float StandDistance = 1.5f;
        const float DelayAfterFade = 0.6f, FadeSeconds = 0.8f, LabelScale = 0.0016f, LabelClearance = 0.35f;
        static readonly int DiseaseAmountId = Shader.PropertyToID("_DiseaseAmount");

        static bool arrivalPending;
        static RecoveredPlantHighlight current;

        bool firstArrival;
        float target;
        Transform focus;
        readonly List<FarmSpecimenVisual> visuals = new List<FarmSpecimenVisual>();
        GameObject label;
        CanvasGroup labelGroup;

        /// <summary>The healing pass of the loaded Farm, or null.</summary>
        public static RecoveredPlantHighlight Current => current != null ? current : null;
        /// <summary>The plants (and other renderers of the disease) this pass heals.</summary>
        public IReadOnlyList<FarmSpecimenVisual> Healed => visuals;
        /// <summary>How many plants of the treated case it heals.</summary>
        public int PlantCount { get; private set; }
        /// <summary>The treated plant the label floats over.</summary>
        public Transform Focus => focus;
        /// <summary>True while the first arrival's label is up.</summary>
        public bool LabelShowing => label != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            arrivalPending = false;
            current = null;
        }

        /// <summary>
        /// Called by every <see cref="SelectablePlant"/> as it wakes: when the planting is part of the healed story (the
        /// treated case's row, or the treated specimen) the Farm's healing pass is scheduled once for the scene. Returns true
        /// when the planting is part of it.
        /// </summary>
        public static bool ApplyTo(SelectablePlant planting)
        {
            if (planting == null || !RecoveryFlow.FarmUnlocked)
                return false;
            if (!Matches(planting) && IndexOf(planting, RecoveryFlow.Specimen) < 0)
                return false;
            if (current == null || current.gameObject.scene != planting.gameObject.scene)
            {
                var go = new GameObject("Recovered plants");
                if (planting.gameObject.scene.IsValid())
                    SceneManager.MoveGameObjectToScene(go, planting.gameObject.scene);
                current = go.AddComponent<RecoveredPlantHighlight>();
                current.firstArrival = RecoveryFlow.FarmShown();
            }

            return true;
        }

        /// <summary>A disease row of the treated crop and disease.</summary>
        public static bool Matches(SelectablePlant planting) =>
            planting != null && planting.IsRow && RecoveryFlow.IsActive && planting.PlantId == RecoveryFlow.PlantId &&
            planting.DiseaseId == RecoveryFlow.DiseaseId;

        /// <summary>The index of <paramref name="specimen"/> among the planting's plants, by its specimen ID; -1 when it is not one of them.</summary>
        public static int IndexOf(SelectablePlant planting, SpecimenRef specimen)
        {
            if (planting == null || specimen == null || planting.PlantId != specimen.PlantId)
                return -1;
            var count = planting.IsRow ? planting.Plants.Length : 1;
            for (var i = 0; i < count; i++)
                if (planting.SpecimenAt(i).SpecimenId == specimen.SpecimenId)
                    return i;
            return -1;
        }

        /// <summary>
        /// The recovery materials that draw only the treated disease: those on its plants (<paramref name="treated"/>) that no
        /// plant of another disease (<paramref name="others"/>) uses. Material copies already made for single plants are skipped.
        /// </summary>
        internal static HashSet<Material> DiseaseMaterials(IEnumerable<Renderer> treated, IEnumerable<Renderer> others)
        {
            var own = new HashSet<Material>();
            foreach (var renderer in treated)
                if (renderer != null)
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && material.HasProperty(DiseaseAmountId) && !material.name.EndsWith("(specimen)"))
                            own.Add(material);
            foreach (var renderer in others)
                if (renderer != null)
                    foreach (var material in renderer.sharedMaterials)
                        own.Remove(material);
            return own;
        }

        IEnumerator Start()
        {
            target = RecoveryFlow.FinalDiseaseAmount;
            var scene = gameObject.scene;
            var plants = new List<Transform>();
            var treatedRenderers = new List<Renderer>();
            var otherRenderers = new List<Renderer>();
            SelectablePlant focusPlanting = null;
            var focusIndex = -1;
            foreach (var planting in FindObjectsByType<SelectablePlant>(FindObjectsInactive.Include))
            {
                if (planting.gameObject.scene != scene)
                    continue;
                var index = IndexOf(planting, RecoveryFlow.Specimen);
                if (index >= 0 && focusPlanting == null)
                {
                    focusPlanting = planting;
                    focusIndex = index;
                    AddPlant(plants, planting.PlantAt(index));
                }

                if (Matches(planting))
                {
                    for (var i = 0; i < planting.Plants.Length; i++)
                        AddPlant(plants, planting.PlantAt(i));
                }
                else if (planting.IsRow)
                {
                    otherRenderers.AddRange(planting.GetComponentsInChildren<Renderer>(true));
                    foreach (var plant in planting.Plants)
                        if (plant != null)
                            otherRenderers.AddRange(plant.GetComponentsInChildren<Renderer>(true));
                }
            }

            foreach (var plant in plants)
                treatedRenderers.AddRange(plant.GetComponentsInChildren<Renderer>(true));
            PlantCount = plants.Count;
            focus = focusPlanting != null ? focusPlanting.PlantAt(focusIndex) : plants.Count > 0 ? plants[0] : null;

            // Any other plant in the Farm drawn with the disease's own materials (outside the rows) heals too.
            var diseaseMaterials = DiseaseMaterials(treatedRenderers, otherRenderers);
            var targets = new List<GameObject>();
            foreach (var plant in plants)
                targets.Add(plant.gameObject);
            if (diseaseMaterials.Count > 0)
                foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                {
                    // Only a renderer on its own (its copies must not reach renderers below it that draw something else).
                    if (renderer.gameObject.scene != scene || UnderAny(renderer.transform, plants) || renderer.GetComponentsInChildren<Renderer>(true).Length > 1)
                        continue;
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null && diseaseMaterials.Contains(material))
                        {
                            targets.Add(renderer.gameObject);
                            break;
                        }
                }

            var start = firstArrival ? Mathf.Max(RecoveryFlow.StartAmount, target) : target;
            foreach (var go in targets)
            {
                var visual = go.GetComponent<FarmSpecimenVisual>();
                if (visual == null)
                    visual = go.AddComponent<FarmSpecimenVisual>();
                visual.Show(start, false);
                visuals.Add(visual);
            }

            Debug.Log($"[BTP] Recovery story: {RecoveryFlow.PlantId}/{RecoveryFlow.DiseaseId}: {plants.Count} Farm plants" +
                      $"{(targets.Count > plants.Count ? $" and {targets.Count - plants.Count} other renderers" : "")} shown " +
                      $"{(RecoveryFlow.FullyHealed ? "healthy" : "improved")} (disease {target:0.00}){(firstArrival ? ", first arrival" : "")}.");
            if (!firstArrival)
            {
                current = current == this ? null : current;
                Destroy(gameObject);
                yield break;
            }

            SaveHealed(plants);
            var state = SelectionState.Instance;
            if (state != null && focusPlanting != null)
                state.SelectSpecimen(focusPlanting.SpecimenAt(focusIndex));
            if (focusPlanting != null)
                focusPlanting.SetSelectedDetail(RecoveryFlow.FullyHealed ? "Recovered: healthy after your treatment." : "Improved after your treatment (partly effective).");

            // Wait for the switch to finish (the view fades in), then heal in front of the user.
            while (EnvironmentController.Instance != null && EnvironmentController.Instance.IsSwitching)
                yield return null;
            yield return new WaitForSeconds(DelayAfterFade);
            foreach (var visual in visuals)
                if (visual != null)
                    visual.Show(target, true);
            BuildLabel();
            var elapsed = 0f;
            while (elapsed < LabelSeconds)
            {
                elapsed += Time.deltaTime;
                if (labelGroup != null)
                {
                    labelGroup.alpha = Mathf.Clamp01(Mathf.Min(elapsed / FadeSeconds, (LabelSeconds - elapsed) / FadeSeconds));
                    label.transform.localScale = Vector3.one * (LabelScale * (1f + 0.04f * Mathf.Sin(elapsed * 4f)));
                }

                yield return null;
            }

            Destroy(label);
        }

        void OnDestroy()
        {
            if (current == this)
                current = null;
            if (label != null)
                Destroy(label);
        }

        static void AddPlant(List<Transform> plants, Transform plant)
        {
            if (plant != null && !plants.Contains(plant))
                plants.Add(plant);
        }

        static bool UnderAny(Transform t, List<Transform> roots)
        {
            foreach (var root in roots)
                if (t.IsChildOf(root))
                    return true;
            return false;
        }

        /// <summary>Keeps every healed row plant in the learner progress (each specimen's saved treatment result).</summary>
        static void SaveHealed(List<Transform> plants)
        {
            var ids = new List<SpecimenRef>();
            if (RecoveryFlow.Specimen != null)
                ids.Add(RecoveryFlow.Specimen);
            foreach (var planting in FindObjectsByType<SelectablePlant>(FindObjectsInactive.Include))
                if (Matches(planting))
                    for (var i = 0; i < planting.Plants.Length; i++)
                        if (!ids.Exists(s => s.SpecimenId == planting.SpecimenAt(i).SpecimenId))
                            ids.Add(planting.SpecimenAt(i));
            foreach (var specimen in ids)
            {
                var record = SpecimenTreatmentResults.Load(specimen.SpecimenId) ?? new SpecimenTreatmentRecord
                {
                    specimenId = specimen.SpecimenId, plantId = RecoveryFlow.PlantId, diseaseId = RecoveryFlow.DiseaseId, score = RecoveryFlow.Healed,
                    passed = true, outcome = RecoveryFlow.Outcome, startDiseaseAmount = RecoveryFlow.StartAmount,
                };
                record.diseaseAmount = RecoveryFlow.FinalDiseaseAmount;
                if (string.IsNullOrEmpty(record.outcomeLabel))
                    record.outcomeLabel = RecoveryFlow.FullyHealed ? "Recovered" : "Partly recovered";
                SpecimenTreatmentResults.Save(record);
            }
        }

        /// <summary>
        /// "See changes in the Farm": the next Farm load stands the user beside the story's plant, facing it (through the
        /// environment's spawn resolver, which is put back once the Farm has loaded).
        /// </summary>
        public static void PrepareArrival(EnvironmentController environment)
        {
            if (environment == null || arrivalPending)
                return;
            arrivalPending = true;
            var previous = environment.SpawnResolver;
            System.Func<EnvironmentId, Scene, Transform> resolver = null;
            resolver = (id, scene) =>
            {
                if (id == EnvironmentId.Farm && arrivalPending && TryViewpoint(scene, out var viewpoint))
                    return viewpoint;
                return previous?.Invoke(id, scene);
            };
            environment.SpawnResolver = resolver;

            void Restore(EnvironmentId _)
            {
                environment.EnvironmentLoaded -= Restore;
                arrivalPending = false;
                if (environment.SpawnResolver == resolver)
                    environment.SpawnResolver = previous;
            }

            environment.EnvironmentLoaded += Restore;
        }

        /// <summary>A standing point beside the story's plant in the loaded Farm (a marker object in that scene).</summary>
        static bool TryViewpoint(Scene scene, out Transform viewpoint)
        {
            viewpoint = null;
            if (RecoveryFlow.Specimen == null || !scene.IsValid())
                return false;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var planting in root.GetComponentsInChildren<SelectablePlant>(true))
            {
                var index = IndexOf(planting, RecoveryFlow.Specimen);
                var plant = index >= 0 ? planting.PlantAt(index) : null;
                if (plant == null || !FarmSpecimenLocator.TryStandingPose(planting, plant, StandDistance, out var position, out var rotation))
                    continue;
                var marker = new GameObject("Recovered plant viewpoint").transform;
                SceneManager.MoveGameObjectToScene(marker.gameObject, scene);
                marker.SetPositionAndRotation(position, rotation);
                return (viewpoint = marker) != null;
            }

            Debug.LogWarning($"[BTP] Recovery story: no standing point found beside {RecoveryFlow.Specimen.SpecimenId}; the Farm opens at its spawn point.");
            return false;
        }

        void BuildLabel()
        {
            if (focus == null)
                return;
            var top = focus.position.y + 1f;
            var any = false;
            foreach (var renderer in focus.GetComponentsInChildren<Renderer>())
            {
                top = any ? Mathf.Max(top, renderer.bounds.max.y) : renderer.bounds.max.y;
                any = true;
            }

            label = new GameObject("Recovered label", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(FaceCamera));
            SceneManager.MoveGameObjectToScene(label, gameObject.scene);
            label.transform.position = new Vector3(focus.position.x, top + LabelClearance, focus.position.z);
            label.transform.localScale = Vector3.one * LabelScale;
            var canvas = label.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            label.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            labelGroup = label.GetComponent<CanvasGroup>();
            labelGroup.alpha = 0f;
            labelGroup.blocksRaycasts = false;
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(460f, 130f);
            var back = new GameObject("Background", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            back.rectTransform.SetParent(rect, false);
            back.rectTransform.anchorMin = Vector2.zero;
            back.rectTransform.anchorMax = Vector2.one;
            back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
            var green = UIPalette.HealthGood;
            back.color = new Color(green.r * 0.3f, green.g * 0.3f, green.b * 0.3f, 0.92f);
            back.raycastTarget = false;
            AddText(rect, RecoveryFlow.FullyHealed ? LabelTitle : "Improved", 56f, FontStyles.Bold, green, 0.42f, 1f);
            AddText(rect, LabelLine(PlantCount, RecoveryFlow.FullyHealed), 28f, FontStyles.Normal, UIPalette.TextPrimary, 0f, 0.42f);
        }

        /// <summary>The label's second line: "4 plants healthy after your treatment".</summary>
        internal static string LabelLine(int plants, bool fully) =>
            $"{plants} {(plants == 1 ? "plant" : "plants")} {(fully ? "healthy" : "partly recovered")} after your treatment";

        static void AddText(RectTransform parent, string text, float size, FontStyles style, Color color, float bottom, float top)
        {
            var label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(parent, false);
            label.rectTransform.anchorMin = new Vector2(0f, bottom);
            label.rectTransform.anchorMax = new Vector2(1f, top);
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }
}
