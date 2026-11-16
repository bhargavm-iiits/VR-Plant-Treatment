using System;
using BTP.Core;
using BTP.Plants;
using TMPro;
using UnityEngine;

namespace BTP.Lab
{
    /// <summary>
    /// Shows one individual leaf for the selected plant and disease on the central pedestal.
    /// Each supplied specimen is upright, centred on the comparison plane and enlarged for inspection.
    /// </summary>
    public sealed class LabPlantDisplay : MonoBehaviour
    {
        [Tooltip("Model parent on top of the pedestal. Its +X axis is the viewer's right.")]
        [SerializeField] Transform modelRoot;
        [Tooltip("Largest footprint width and height of the displayed model, in metres.")]
        [SerializeField] Vector2 maxDisplaySize = new Vector2(0.9f, 0.88f);
        [SerializeField] LeafCatalog leaves;
        [SerializeField] TMP_Text scaleNote;

        public Transform ModelRoot => modelRoot;
        public GameObject CurrentModel { get; private set; }
        public PlantDefinition CurrentPlant { get; private set; }
        public string CurrentDiseaseId { get; private set; }

        internal void BindLeaves(LeafCatalog catalog) => leaves = catalog;

        /// <summary>Raised after a new plant model has been placed on the pedestal.</summary>
        public event Action<PlantDefinition, GameObject> ModelChanged;

        internal void Bind(Transform root, TMP_Text note)
        {
            modelRoot = root;
            scaleNote = note;
        }

        void OnEnable()
        {
            var state = SelectionState.Instance;
            if (state == null)
                return;
            state.PlantChanged += Show;
            state.CaseChanged += OnCaseChanged;
            Show(state.EnsureSelection());
        }

        void OnDisable()
        {
            if (SelectionState.Instance == null)
                return;
            SelectionState.Instance.PlantChanged -= Show;
            SelectionState.Instance.CaseChanged -= OnCaseChanged;
        }

        // Disease chosen elsewhere (e.g. the Treatment lab's selector) while this display is loaded.
        void OnCaseChanged(string plantId, string diseaseId)
        {
            if (CurrentPlant != null && CurrentPlant.PlantId == plantId && diseaseId != CurrentDiseaseId)
                ShowDisease(diseaseId);
        }

        void Show(PlantDefinition plant)
        {
            if (plant == null)
                return;
            if (plant == CurrentPlant && CurrentModel != null)
                return;
            CurrentPlant = plant;
            // The shared selection keeps the disease chosen in either lab.
            var state = SelectionState.Instance;
            var disease = state != null && state.Current == plant ? state.CurrentDiseaseId : null;
            ShowDisease(disease ?? DiseaseClasses.DefaultDiseaseId(plant.PlantId));
        }

        public bool ShowDisease(string diseaseId)
        {
            if (CurrentPlant == null || string.IsNullOrEmpty(diseaseId))
                return false;
            // A connected XR session keeps its own case on the pedestal.
            if (SelectionState.Instance != null && !SelectionState.Instance.CanSelect(CurrentPlant.PlantId, diseaseId))
                return false;
            string key = CurrentPlant.PlantId + "/" + diseaseId;
            if (CurrentDiseaseId == diseaseId && CurrentModel != null && CurrentModel.name == key)
                return true;
            if (CurrentModel != null)
            {
                CurrentModel.SetActive(false);
                Destroy(CurrentModel);
            }
            CurrentDiseaseId = diseaseId;
            if (SelectionState.Instance != null && SelectionState.Instance.Current == CurrentPlant)
                SelectionState.Instance.SelectDisease(diseaseId);
            bool found = leaves != null && leaves.TryGet(CurrentPlant.PlantId, diseaseId, out _);
            if (found)
            {
                leaves.TryGet(CurrentPlant.PlantId, diseaseId, out var prefab);
                CurrentModel = Instantiate(prefab, modelRoot, false);
                CurrentModel.name = key;
                FitToDisplay(CurrentModel);
                CurrentModel.AddComponent<LeafMouseRotation>();
            }
            else
            {
                CurrentModel = PlantSpawner.CreateSetupErrorMarker("Leaf missing: " + key, modelRoot);
                Debug.LogError("[BTP] Leaf catalog missing " + key, this);
            }
            if (scaleNote != null)
            {
                var note = DiseaseClasses.TryGet(CurrentPlant.PlantId, diseaseId, out var info) ? info.SpecimenNote : null;
                scaleNote.text = !found ? "Leaf model unavailable"
                    : string.IsNullOrEmpty(note) ? "Drag leaf to rotate - illustrative recovery"
                    : "Drag leaf to rotate - illustrative recovery\n<color=#F2C14E>" + note + "</color>";
            }
            ModelChanged?.Invoke(CurrentPlant, CurrentModel);
            return found;
        }

        float FitToDisplay(GameObject model)
        {
            model.transform.localScale = Vector3.one;
            model.transform.localPosition = Vector3.zero;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1f;

            // Measure in inspection space: world bounds change as the viewer walks around.
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in renderers)
            {
                var local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var point = modelRoot.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            var width = Mathf.Max(bounds.size.x, bounds.size.z);
            var height = bounds.size.y;
            var scale = Mathf.Min(maxDisplaySize.x / Mathf.Max(width, 0.01f), maxDisplaySize.y / Mathf.Max(height, 0.01f));
            model.transform.localScale = Vector3.one * scale;
            var centre = bounds.center * scale;
            model.transform.localPosition = new Vector3(-centre.x, 0.07f - centre.y + height * scale * 0.5f, -centre.z);
            return scale;
        }
    }
}
