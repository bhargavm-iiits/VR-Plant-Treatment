using TMPro;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The charcoal sign over the Treatment lab's counter: the current action ("Apply targeted spray" for a case
    /// that uses the hand sprayer) and the loaded case as "Crop / Disease". Updates whenever a case loads.
    /// </summary>
    public sealed class TreatmentLabSign : MonoBehaviour
    {
        public const string DefaultTitle = "Treatment lab";

        [SerializeField] TreatmentController controller;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;

        internal void Bind(TreatmentController owner, TMP_Text titleText, TMP_Text subtitleText)
        {
            controller = owner;
            title = titleText;
            subtitle = subtitleText;
        }

        void OnEnable()
        {
            if (controller != null)
                controller.CaseLoaded += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (controller != null)
                controller.CaseLoaded -= Refresh;
        }

        void Refresh()
        {
            var content = controller != null ? controller.Session?.Case : null;
            if (title != null)
                title.text = content != null && content.RackTools.Contains("sprayer") ? TrainingSprayer.ActionLabel : DefaultTitle;
            if (subtitle != null)
                subtitle.text = content != null ? $"{content.PlantName} / {content.DiseaseName}" : "";
        }
    }
}
