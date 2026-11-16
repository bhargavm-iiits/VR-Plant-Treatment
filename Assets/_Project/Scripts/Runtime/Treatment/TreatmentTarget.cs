using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Treatment
{
    /// <summary>
    /// A selectable treatment target or fixture (leaf, mummy, root zone, vent switch, disposal bin).
    /// Trigger on a controller ray, hand pinch or poke, or a desktop click all select it the same
    /// way; the TreatmentController validates the action against the lesson. Child objects named
    /// "State_&lt;state&gt;" show the target's saved state (e.g. State_placed shows the stake).
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class TreatmentTarget : MonoBehaviour
    {
        const string StatePrefix = "State_";

        TreatmentController controller;
        XRSimpleInteractable interactable;
        GameObject marker;

        public string TargetId { get; private set; }
        public string Tag { get; private set; }
        public string Label { get; private set; }
        public TreatmentTargetDef Definition { get; private set; }
        public string State { get; private set; }

        internal void Init(TreatmentController owner, TreatmentTargetDef definition, GameObject markerPrefab)
        {
            controller = owner;
            Definition = definition;
            TargetId = definition.TargetId;
            Tag = definition.Tag;
            Label = definition.Label;
            if (markerPrefab != null)
            {
                marker = Instantiate(markerPrefab, transform, false);
                marker.name = "Marker";
                marker.SetActive(false);
            }

            EnsureCollider();
            if (marker != null)
            {
                // Size the ring to the target: small for leaf spots, larger for props.
                var size = 0f;
                foreach (var c in GetComponentsInChildren<Collider>(true))
                {
                    var b = c is BoxCollider box ? Vector3.Scale(box.size, c.transform.lossyScale) : c.bounds.size;
                    size = Mathf.Max(size, Mathf.Max(b.x, b.y));
                }

                size = Mathf.Max(size, 0.03f);
                marker.transform.localScale = Vector3.one * Mathf.Clamp(size * 1.25f / 0.19f, 0.3f, 1.4f);
            }

            ApplyState(null);
        }

        void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(OnSelected);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        void OnDestroy()
        {
            if (interactable == null)
                return;
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            if (controller != null)
                controller.OnTargetSelected(this, args.interactorObject?.transform);
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (controller != null)
                controller.ShowTooltip(this);
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            if (controller != null)
                controller.HideTooltip(this);
        }

        /// <summary>Activates the State_&lt;state&gt; children matching the saved state.</summary>
        public void ApplyState(string state)
        {
            State = state;
            foreach (Transform child in transform)
            {
                if (!child.name.StartsWith(StatePrefix))
                    continue;
                child.gameObject.SetActive(state != null && child.name == StatePrefix + state);
            }
        }

        public void SetMarked(bool marked)
        {
            if (marker != null && marker.activeSelf != marked)
                marker.SetActive(marked);
        }

        public void SetInteractable(bool value)
        {
            if (interactable != null)
                interactable.enabled = value;
        }

        void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>(true) != null)
                return;
            var renderers = GetComponentsInChildren<Renderer>(true);
            var box = gameObject.AddComponent<BoxCollider>();
            if (renderers.Length == 0)
            {
                box.size = Vector3.one * 0.1f;
                return;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            box.center = transform.InverseTransformPoint(bounds.center);
            var size = transform.InverseTransformVector(bounds.size);
            box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
        }
    }
}