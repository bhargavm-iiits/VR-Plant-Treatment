using UnityEngine;
using UnityEngine.EventSystems;

namespace BTP.Farm
{
    /// <summary>
    /// Marks presses on the Farm console, so the disease gallery does not treat them as a click on empty space.
    /// It sits on the console canvas and on each button (a button consumes the press before its parents see it).
    /// </summary>
    public sealed class FarmConsolePressGuard : MonoBehaviour, IPointerDownHandler
    {
        /// <summary>Frame of the most recent press on the console, or -1.</summary>
        public static int LastPressFrame { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LastPressFrame = -1;

        public void OnPointerDown(PointerEventData eventData) => LastPressFrame = Time.frameCount;
    }
}
