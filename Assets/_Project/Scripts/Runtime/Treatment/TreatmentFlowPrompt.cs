using BTP.Core;
using BTP.Farm;
using BTP.UI;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The Treatment lab's end of the after-treatment story (<see cref="RecoveryFlow"/>): a successful final result (the
    /// guided treatment's completion, a passed Check Health, a managed manual session) records the story and shows
    /// "Treatment applied. Return to the Lab to see the weekly changes." with Go to Lab and Close; a failed one shows a
    /// "try again" message and leaves the story alone. Go to Lab selects the treated specimen (else its case) and loads the
    /// visiting Lab.
    /// </summary>
    public static class TreatmentFlowPrompt
    {
        public const string AppliedTitle = "Treatment applied";
        public const string AppliedBody = "Return to the Lab to see the weekly changes.";
        public const string PartlyNote = "Partly effective: the plant improves but does not fully recover.";
        public const string FailedTitle = "Not recovered yet";
        public const string GoToLabLabel = "Go to Lab";
        public const string CloseLabel = "Close";

        /// <summary>
        /// The Farm plant the story follows: the carried specimen, else the selected one of this case, else the first plant of
        /// the case's Farm disease row; null for a crop that is not planted in the Farm.
        /// </summary>
        public static SpecimenRef StorySpecimen(SpecimenRef carried, string plantId, string diseaseId)
        {
            if (carried != null && carried.PlantId == plantId)
                return carried;
            var state = SelectionState.Instance;
            var selected = state != null ? state.SelectedSpecimen : null;
            if (selected != null && selected.IsCase(plantId, diseaseId))
                return selected;
            var catalog = state != null ? state.Catalog : null;
            if (catalog != null && catalog.TryGet(plantId, out var plant) && plant.FarmPrefab == null)
                return null;
            return FarmSpecimenIds.Ref(plantId, diseaseId, 0);
        }

        /// <summary>The pop-up's message for the current story.</summary>
        public static string AppliedText() =>
            RecoveryFlow.FullyHealed ? AppliedBody : AppliedBody + "\n<size=85%><color=#F9BF24>" + PartlyNote + "</color></size>";

        /// <summary>
        /// A treatment of the case succeeded: records the story and, when <paramref name="popup"/> (the guided completion card
        /// carries its own Go to Lab), shows the pop-up in <paramref name="owner"/>'s scene.
        /// </summary>
        public static FlowPopup Applied(Component owner, string plantId, string diseaseId, SpecimenRef carried, float startAmount, float healed,
            string outcome, bool popup)
        {
            var specimen = StorySpecimen(carried, plantId, diseaseId);
            var started = RecoveryFlow.TreatmentApplied(plantId, diseaseId, specimen, startAmount, healed, outcome);
            Debug.Log($"[BTP] Recovery story: {plantId}/{diseaseId} treated ({outcome}, heals {healed:P0}) for Farm plant " +
                      $"{(specimen != null ? specimen.SpecimenId : "none")}{(started ? "" : " (same treatment again)")}.");
            if (!popup || !Application.isPlaying)
                return null;
            return FlowPopup.Show(owner != null ? owner.gameObject.scene : default, AppliedTitle, AppliedText(), UIPalette.HealthGood,
                new FlowPopup.Choice(GoToLabLabel, GoToLab), new FlowPopup.Choice(CloseLabel, null, false));
        }

        /// <summary>A failed or incomplete final result: a "try again" message (the story is unchanged).</summary>
        public static FlowPopup Failed(Component owner, string detail)
        {
            Debug.Log("[BTP] Recovery story: treatment not successful - " + detail);
            if (!Application.isPlaying)
                return null;
            var body = (string.IsNullOrEmpty(detail) ? "" : detail + "\n") + "Adjust the treatment and try again.";
            return FlowPopup.Show(owner != null ? owner.gameObject.scene : default, FailedTitle, body, UIPalette.Warning,
                new FlowPopup.Choice("Try again", null));
        }

        /// <summary>Go to Lab: the treated specimen (or its case) becomes the selection, then the visiting Lab loads.</summary>
        public static void GoToLab()
        {
            var state = SelectionState.Instance;
            if (state != null && RecoveryFlow.IsActive)
            {
                var selected = RecoveryFlow.Specimen != null && state.SelectSpecimen(RecoveryFlow.Specimen);
                if (!selected)
                    state.SelectCase(RecoveryFlow.PlantId, RecoveryFlow.DiseaseId);
            }

            var environment = EnvironmentController.Instance;
            if (environment != null)
                environment.EnterLab();
            else
                Debug.LogError("[BTP] Go to Lab: no EnvironmentController is loaded; start from XRBootstrap.");
        }
    }
}
