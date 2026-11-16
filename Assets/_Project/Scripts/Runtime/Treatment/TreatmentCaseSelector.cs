using System.Collections.Generic;
using BTP.Core;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// Plant and disease selection for the Treatment lab. It edits the shared SelectionState, so the
    /// visiting Lab and Farm follow the same case. Every canonical case is listed, including any whose
    /// database record is missing, so a missing record shows the unavailable message instead of vanishing.
    /// </summary>
    public sealed class TreatmentCaseSelector : MonoBehaviour
    {
        [SerializeField] List<string> plantIds = new List<string>();
        [SerializeField] List<Button> plantButtons = new List<Button>();
        [SerializeField] List<SelectedStateVisual> plantStates = new List<SelectedStateVisual>();
        [SerializeField] List<Button> diseaseButtons = new List<Button>();
        [SerializeField] List<SelectedStateVisual> diseaseStates = new List<SelectedStateVisual>();

        IReadOnlyList<DiseaseClasses.Info> diseases;

        internal void Bind(List<string> plants, List<Button> plantControls, List<SelectedStateVisual> plantVisuals,
            List<Button> diseaseControls, List<SelectedStateVisual> diseaseVisuals)
        {
            plantIds = plants; plantButtons = plantControls; plantStates = plantVisuals;
            diseaseButtons = diseaseControls; diseaseStates = diseaseVisuals;
        }

        void Awake()
        {
            for (var i = 0; i < plantButtons.Count; i++)
            {
                var id = plantIds[i];
                plantButtons[i].onClick.AddListener(() => SelectionState.Instance?.SelectPlant(id));
            }

            for (var i = 0; i < diseaseButtons.Count; i++)
            {
                var index = i;
                diseaseButtons[i].onClick.AddListener(() =>
                {
                    if (diseases != null && index < diseases.Count)
                        SelectionState.Instance?.SelectDisease(diseases[index].DiseaseId);
                });
            }
        }

        void OnEnable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged += OnCaseChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged -= OnCaseChanged;
        }

        void OnCaseChanged(string plantId, string diseaseId) => Refresh();

        public void Refresh()
        {
            var state = SelectionState.Instance;
            var plant = state != null ? state.Current : null;
            for (var i = 0; i < plantStates.Count; i++)
                plantStates[i].Selected = plant != null && plantIds[i] == plant.PlantId;

            diseases = plant != null ? DiseaseClasses.For(plant.PlantId) : null;
            for (var i = 0; i < diseaseButtons.Count; i++)
            {
                var visible = diseases != null && i < diseases.Count;
                diseaseButtons[i].gameObject.SetActive(visible);
                if (!visible)
                    continue;
                diseaseButtons[i].GetComponentInChildren<TMP_Text>().text = diseases[i].DisplayName;
                diseaseStates[i].Selected = diseases[i].DiseaseId == state.CurrentDiseaseId;
            }
        }
    }
}