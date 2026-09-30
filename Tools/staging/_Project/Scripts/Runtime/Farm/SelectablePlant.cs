using BTP.Core;
using BTP.Plants;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Farm
{
    /// <summary>
    /// A labelled farm specimen. Controller ray or hand pinch selects it: the plant becomes the
    /// shared selection, its ground ring lights up and its name card opens. Hover brightens the
    /// ring so users can see what they are pointing at.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class SelectablePlant : MonoBehaviour
    {
        [SerializeField] string plantId;
        [SerializeField] SelectionRing ring;
        [SerializeField] PlantNameCard nameCard;
        [SerializeField] string selectedDetail = "Selected. Press Enter Lab on the notice board.";

        XRSimpleInteractable interactable;
        bool hovered;
        bool selected;

        public string PlantId => plantId;

        internal void Bind(string id, SelectionRing selectionRing, PlantNameCard card)
        {
            plantId = id;
            ring = selectionRing;
            nameCard = card;
        }

        void Awake() => interactable = GetComponent<XRSimpleInteractable>();

        void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.firstHoverEntered.AddListener(OnFirstHoverEntered);
            interactable.lastHoverExited.AddListener(OnLastHoverExited);

            if (SelectionState.Instance == null)
                return;
            SelectionState.Instance.PlantChanged += OnPlantChanged;
            OnPlantChanged(SelectionState.Instance.Current);
        }

        void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelectEntered);
            interactable.firstHoverEntered.RemoveListener(OnFirstHoverEntered);
            interactable.lastHoverExited.RemoveListener(OnLastHoverExited);
            if (SelectionState.Instance != null)
                SelectionState.Instance.PlantChanged -= OnPlantChanged;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.SelectPlant(plantId);
        }

        void OnFirstHoverEntered(HoverEnterEventArgs args)
        {
            hovered = true;
            Refresh();
        }

        void OnLastHoverExited(HoverExitEventArgs args)
        {
            hovered = false;
            Refresh();
        }

        void OnPlantChanged(PlantDefinition plant)
        {
            selected = plant != null && plant.PlantId == plantId;
            Refresh();
        }

        void Refresh()
        {
            if (ring != null)
                ring.SetState(selected ? SelectionRing.State.Selected : hovered ? SelectionRing.State.Hover : SelectionRing.State.Idle);
            if (nameCard == null)
                return;
            if (selected && SelectionState.Instance != null && SelectionState.Instance.Current != null)
                nameCard.Show(SelectionState.Instance.Current.DisplayName, selectedDetail);
            else
                nameCard.Hide();
        }
    }
}
