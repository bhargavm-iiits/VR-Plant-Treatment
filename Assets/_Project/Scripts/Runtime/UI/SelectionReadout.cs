using BTP.Core;
using BTP.Plants;
using TMPro;
using UnityEngine;

namespace BTP.UI
{
    /// <summary>Shows the current plant selection, e.g. on the farm notice board.</summary>
    public sealed class SelectionReadout : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] string noSelectionText = "No plant selected yet: the Lab will open with Tomato.";

        internal void Bind(TMP_Text text) => label = text;

        void OnEnable()
        {
            if (SelectionState.Instance == null)
                return;
            SelectionState.Instance.PlantChanged += Show;
            Show(SelectionState.Instance.Current);
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.PlantChanged -= Show;
        }

        void Show(PlantDefinition plant) =>
            label.text = plant == null ? noSelectionText : $"Selected: <b>{plant.DisplayName}</b>";
    }
}
