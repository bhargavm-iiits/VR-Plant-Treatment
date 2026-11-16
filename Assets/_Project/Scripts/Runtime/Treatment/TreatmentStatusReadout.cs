using BTP.Core;
using TMPro;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Shows the saved treatment result in the Farm and the visiting Lab: for the shared selected case, for one
    /// plant (its most recently practised case), or for one case (a Farm disease row).
    /// </summary>
    public sealed class TreatmentStatusReadout : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [Tooltip("Empty: follow the shared selected case. Otherwise: this plant's latest case.")]
        [SerializeField] string plantId;
        [Tooltip("With a plant: this one case (plant and disease) instead of the plant's latest case.")]
        [SerializeField] string diseaseId;

        internal void Bind(TMP_Text label, string plant, string disease = null)
        {
            text = label;
            plantId = plant;
            diseaseId = disease;
        }

        void OnEnable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged += OnCaseChanged;
            if (TreatmentServices.Instance != null)
                TreatmentServices.Instance.ProgressChanged += OnProgressChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged -= OnCaseChanged;
            if (TreatmentServices.Instance != null)
                TreatmentServices.Instance.ProgressChanged -= OnProgressChanged;
        }

        void OnCaseChanged(string plant, string disease) => Refresh();
        void OnProgressChanged(CaseKey key) => Refresh();

        public void Refresh()
        {
            if (text == null)
                return;
            var services = TreatmentServices.Instance;
            if (services == null || !services.Content.IsReady)
            {
                text.text = "";
                return;
            }

            if (string.IsNullOrEmpty(plantId))
            {
                var key = services.SelectedCase();
                var content = services.GetCase(key, out _);
                var title = content != null ? content.Title : key.ToString();
                text.text = $"<b>{title}</b>\n{services.DescribeProgress(key)}";
                return;
            }

            if (!string.IsNullOrEmpty(diseaseId))
            {
                text.text = services.DescribeProgress(new CaseKey(plantId, diseaseId));
                return;
            }

            var latest = services.Progress.LatestForPlant(plantId);
            if (latest == null)
            {
                text.text = "Treatment: not started";
                return;
            }

            var latestCase = services.GetCase(latest.Key, out _);
            var name = latestCase != null ? latestCase.DiseaseName : latest.Key.DiseaseId;
            text.text = $"{name}: {services.DescribeProgress(latest.Key)}";
        }
    }
}