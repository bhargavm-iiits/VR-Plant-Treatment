using System;
using BTP.Core;
using BTP.Plants;
using TMPro;
using UnityEngine;

namespace BTP.Lab
{
    /// <summary>
    /// Shows the selected plant's lab model on the central pedestal, scaled to fit the display
    /// space (trees are shown as small specimens, small plants enlarged) with an honest scale note.
    /// </summary>
    public sealed class LabPlantDisplay : MonoBehaviour
    {
        [Tooltip("Model parent on top of the pedestal. Its +X axis is the viewer's right.")]
        [SerializeField] Transform modelRoot;
        [Tooltip("Largest footprint width and height of the displayed model, in metres.")]
        [SerializeField] Vector2 maxDisplaySize = new Vector2(1.0f, 1.15f);
        [SerializeField, Range(1f, 3f)] float maxEnlargement = 2f;
        [SerializeField] TMP_Text scaleNote;

        public Transform ModelRoot => modelRoot;
        public GameObject CurrentModel { get; private set; }
        public PlantDefinition CurrentPlant { get; private set; }

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
            Show(state.EnsureSelection());
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.PlantChanged -= Show;
        }

        void Show(PlantDefinition plant)
        {
            if (plant == CurrentPlant && CurrentModel != null)
                return;

            if (CurrentModel != null)
                Destroy(CurrentModel);

            CurrentPlant = plant;
            CurrentModel = PlantSpawner.Spawn(plant, PlantVariant.Lab, modelRoot);
            var scale = CurrentModel != null ? FitToDisplay(CurrentModel) : 1f;
            if (scaleNote != null)
                scaleNote.text = Mathf.Approximately(scale, 1f) ? "Shown at real size" : $"Shown at {Mathf.RoundToInt(scale * 100f)}% of real size";
            ModelChanged?.Invoke(plant, CurrentModel);
        }

        float FitToDisplay(GameObject model)
        {
            model.transform.localScale = Vector3.one;
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1f;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var rootScale = modelRoot.lossyScale.y;
            var width = Mathf.Max(bounds.size.x, bounds.size.z) / rootScale;
            var height = bounds.size.y / rootScale;
            var scale = Mathf.Min(maxDisplaySize.x / Mathf.Max(width, 0.01f), maxDisplaySize.y / Mathf.Max(height, 0.01f));
            scale = Mathf.Clamp(scale, 0.05f, maxEnlargement);

            // Keep plants that already fit at their real size.
            if (scale >= 1f && scale < 1.15f)
                scale = 1f;
            model.transform.localScale = Vector3.one * scale;
            return scale;
        }
    }
}
