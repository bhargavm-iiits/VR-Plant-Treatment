using UnityEngine;
using UnityEngine.UI;

namespace BTP.Core
{
    /// <summary>
    /// Connects a UI button in an environment scene to the persistent EnvironmentController,
    /// which lives in XRBootstrap and so cannot be referenced across scenes in the Inspector.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class EnvironmentButton : MonoBehaviour
    {
        public enum ButtonAction
        {
            EnterLab,
            ReturnToFarm,
            Recenter
        }

        [SerializeField] ButtonAction action;

        internal ButtonAction Action
        {
            get => action;
            set => action = value;
        }

        void Awake() => GetComponent<Button>().onClick.AddListener(OnClick);

        void OnClick()
        {
            var controller = EnvironmentController.Instance;
            if (controller == null)
            {
                Debug.LogError("[BTP] No EnvironmentController is loaded; start from XRBootstrap.", this);
                return;
            }

            switch (action)
            {
                case ButtonAction.EnterLab:
                    controller.EnterLab();
                    break;
                case ButtonAction.ReturnToFarm:
                    controller.ReturnToFarm();
                    break;
                case ButtonAction.Recenter:
                    controller.Recenter();
                    break;
            }
        }
    }
}
