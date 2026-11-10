using UnityEngine;
using UnityEngine.InputSystem;

namespace BTP.Core
{
    /// <summary>
    /// Recenters the user on the current spawn point from a controller button (the left
    /// controller's menu button by default; R on the keyboard for Editor testing). Hand
    /// tracking users use the Recenter buttons placed in each environment instead.
    /// </summary>
    public sealed class RecenterControl : MonoBehaviour
    {
        [SerializeField] InputActionProperty recenterAction = new InputActionProperty(CreateDefaultAction());

        static InputAction CreateDefaultAction()
        {
            var action = new InputAction("Recenter", InputActionType.Button);
            action.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            action.AddBinding("<Keyboard>/r");
            return action;
        }

        void OnEnable()
        {
            var action = recenterAction.action;
            if (action == null)
                return;
            action.performed += OnRecenter;
            action.Enable();
        }

        void OnDisable()
        {
            var action = recenterAction.action;
            if (action == null)
                return;
            action.performed -= OnRecenter;
            if (recenterAction.reference == null)
                action.Disable();
        }

        static void OnRecenter(InputAction.CallbackContext context)
        {
            if (EnvironmentController.Instance != null)
                EnvironmentController.Instance.Recenter();
        }
    }
}
