using UnityEngine;

namespace BTP.UI
{
    /// <summary>
    /// Switches a world-space canvas off while the viewer is farther than <see cref="maxDistance"/> (ground distance),
    /// for the Farm's 24 row labels: their 40 mm text is unreadable beyond about 12 m anyway, and every visible canvas
    /// costs a few draw calls on Quest. Checks a few times a second, staggered, so dozens of labels cost nothing.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public sealed class CanvasDistanceCull : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxDistance = 12f;
        [Tooltip("Frames between checks (each label starts at a different frame).")]
        [SerializeField, Min(1)] int interval = 15;

        Canvas canvas;
        Camera viewer;
        int offset;

        internal void Bind(float distance) => maxDistance = distance;

        void Awake()
        {
            canvas = GetComponent<Canvas>();
            offset = (int)((uint)GetHashCode() % (uint)interval);
        }

        void OnEnable() => Check();

        void Update()
        {
            if ((Time.frameCount + offset) % interval == 0)
                Check();
        }

        void Check()
        {
            if (viewer == null || !viewer.isActiveAndEnabled)
                viewer = Camera.main;
            if (viewer == null)
                return;
            canvas.enabled = IsNear(viewer.transform.position, transform.position, maxDistance);
        }

        /// <summary>Whether two points are within <paramref name="distance"/> of each other on the ground plane.</summary>
        public static bool IsNear(Vector3 viewer, Vector3 label, float distance)
        {
            var delta = label - viewer;
            delta.y = 0f;
            return delta.sqrMagnitude <= distance * distance;
        }
    }
}
