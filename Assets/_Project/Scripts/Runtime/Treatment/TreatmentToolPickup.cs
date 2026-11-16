using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Treatment
{
    /// <summary>
    /// A tool on the rack. Selecting it (trigger, pinch, poke or desktop click) takes the tool;
    /// selecting the held tool or the empty-hand slot puts it back.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class TreatmentToolPickup : MonoBehaviour
    {
        TreatmentController controller;
        XRSimpleInteractable interactable;

        /// <summary>Tool key, or null for the empty (gloved) hand.</summary>
        public string ToolKey { get; private set; }
        public string Label { get; private set; }

        internal void Init(TreatmentController owner, string toolKey, string label)
        {
            controller = owner;
            ToolKey = toolKey;
            Label = label;
        }

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(OnSelected);
        }

        void OnDestroy()
        {
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnSelected);
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            if (controller != null)
                controller.OnToolSelected(this, args.interactorObject?.transform);
        }
    }
}