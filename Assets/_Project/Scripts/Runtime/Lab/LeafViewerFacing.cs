using UnityEngine;

namespace BTP.Lab
{
    /// <summary>
    /// Orientation of the Lab's inspection frame and controls. The Lab is static by default: the builder already lays the
    /// leaf and its panels out facing the spawn point, so nothing moves when the user walks or turns. Turning on
    /// <see cref="followViewer"/> restores the old behaviour of re-facing the viewer every frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class LeafViewerFacing : MonoBehaviour
    {
        [Tooltip("Re-face the viewer every frame (the leaf and Lab panels then swing around with the user). Off: everything stays where it was built.")]
        [SerializeField] bool followViewer;
        [SerializeField] Transform modelRoot;
        [SerializeField] Transform comparisonRoot;
        [SerializeField] Transform labUI;
        [SerializeField] RectTransform scaleNote;
        Camera viewer;
        Vector3 notePosition;
        Quaternion noteRotation;
        Quaternion uiRotation;
        Vector3 uiOffset;
        bool bound;

        internal void Bind(Transform model, Transform comparison, Transform controls, RectTransform note)
        {
            modelRoot = model; comparisonRoot = comparison; labUI = controls; scaleNote = note;
        }

        void Awake()
        {
            if (scaleNote != null)
            {
                notePosition = scaleNote.anchoredPosition3D;
                noteRotation = scaleNote.localRotation;
            }
            if (labUI != null)
            {
                uiRotation = labUI.rotation;
                uiOffset = labUI.position - transform.position;
            }
            bound = true;
        }

        internal static Quaternion FacingRotation(Vector3 centre, Vector3 eye, Quaternion fallback)
        {
            var away = centre - eye;
            away.y = 0f;
            return away.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(away, Vector3.up) : fallback;
        }

        public void FaceViewer(Vector3 eye)
        {
            if (!bound) Awake();
            var rotation = FacingRotation(transform.position, eye, modelRoot.rotation);
            modelRoot.rotation = rotation;
            if (comparisonRoot != null) comparisonRoot.rotation = rotation;
            if (labUI != null)
            {
                labUI.SetPositionAndRotation(transform.position + rotation * uiOffset, rotation * uiRotation);
            }
            if (scaleNote != null)
            {
                var localRotation = Quaternion.Inverse(transform.rotation) * rotation;
                scaleNote.anchoredPosition3D = localRotation * notePosition;
                scaleNote.localRotation = localRotation * noteRotation;
            }
        }

        public bool FollowViewer
        {
            get => followViewer;
            set => followViewer = value;
        }

        void LateUpdate()
        {
            if (!followViewer)
                return;
            if (viewer == null || !viewer.isActiveAndEnabled) viewer = Camera.main;
            if (viewer != null && modelRoot != null) FaceViewer(viewer.transform.position);
        }
    }
}
