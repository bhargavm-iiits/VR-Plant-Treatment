using BTP.Core;
using BTP.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Lab
{
    /// <summary>A button that selects one plant; shows the selected state while it is the current plant.</summary>
    [RequireComponent(typeof(Button))]
    public sealed class PlantButton : MonoBehaviour
    {
        [SerializeField] string plantId;
        [SerializeField] SelectedStateVisual selectedVisual;

        public string PlantId => plantId;

        internal void Bind(string id, SelectedStateVisual visual)
        {
            plantId = id;
            selectedVisual = visual;
        }

        void Awake() => GetComponent<Button>().onClick.AddListener(OnClick);

        void OnEnable()
        {
            if (SelectionState.Instance == null)
                return;
            SelectionState.Instance.PlantChanged += OnPlantChanged;
            OnPlantChanged(SelectionState.Instance.Current);
        }

        void OnDisable()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.PlantChanged -= OnPlantChanged;
        }

        void OnClick()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.SelectPlant(plantId);
        }

        void OnPlantChanged(Plants.PlantDefinition plant)
        {
            if (selectedVisual != null)
                selectedVisual.Selected = plant != null && plant.PlantId == plantId;
        }
    }
}
