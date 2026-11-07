using BTP.Core;
using BTP.Plants;
using TMPro;
using UnityEngine;

namespace BTP.Lab
{
    /// <summary>
    /// Plant Information panel: concise species identity plus a small rotating 3D view built
    /// from the plant's own model asset.
    /// </summary>
    public sealed class PlantInfoPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text scientificName;
        [SerializeField] TMP_Text details;
        [SerializeField] TMP_Text summary;
        [Tooltip("Turntable parent for the 3D preview.")]
        [SerializeField] Transform previewRoot;
        [SerializeField] float previewSize = 0.32f;

        GameObject preview;

        internal void Bind(TMP_Text titleText, TMP_Text scientificText, TMP_Text detailsText, TMP_Text summaryText, Transform previewParent)
        {
            title = titleText;
            scientificName = scientificText;
            details = detailsText;
            summary = summaryText;
            previewRoot = previewParent;
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
            if (plant == null)
                return;

            title.text = plant.DisplayName;
            scientificName.text = plant.ScientificName;
            details.text = $"Family: {plant.Family}\nType: {plant.PlantType}";
            summary.text = plant.Summary;

            if (preview != null)
                Destroy(preview);
            if (previewRoot == null)
                return;

            // The farm model is lighter and reads well at this size.
            preview = PlantSpawner.Spawn(plant, PlantVariant.Farm, previewRoot);
            if (preview == null)
                return;
            foreach (var lodGroup in preview.GetComponentsInChildren<LODGroup>())
                lodGroup.ForceLOD(0);
            foreach (var collider in preview.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            FitPreview(preview);
        }

        void FitPreview(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / previewRoot.lossyScale.y;
            model.transform.localScale = Vector3.one * (previewSize / Mathf.Max(largest, 0.01f));
        }
    }
}
