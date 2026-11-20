using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BTP.UI
{
    /// <summary>
    /// Press-and-hold auto-repeat for a uGUI button (XR poke, ray or mouse): after a short delay the button's onClick fires
    /// again at a steady rate until it is released, so stepping a value by 20 is quick.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Min(0.05f)] float delay = 0.45f;
        [SerializeField, Min(0.02f)] float interval = 0.08f;

        Button button;
        bool held;
        float next;

        void Awake() => button = GetComponent<Button>();

        public void OnPointerDown(PointerEventData eventData)
        {
            held = true;
            next = Time.unscaledTime + delay;
        }

        public void OnPointerUp(PointerEventData eventData) => held = false;

        public void OnPointerExit(PointerEventData eventData) => held = false;

        void OnDisable() => held = false;

        void Update()
        {
            if (!held || button == null || !button.interactable || Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + interval;
            // A deliberate repeat, not a duplicate of one gesture.
            PressGuard.Shared.Forget(button);
            button.onClick.Invoke();
        }
    }
}
