using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace BTP.Lab
{
    /// <summary>Drag the leaf with the left mouse button; keep the controls and specimen centre fixed.</summary>
    public sealed class LeafMouseRotation : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float degreesPerPixel = 0.35f;
        Transform pivot;
        Renderer[] targets;
        Vector2 previousPointer;
        float yaw;
        float pitch;
        readonly List<RaycastResult> uiHits = new();
        public bool IsDragging { get; private set; }

        void Awake() => Initialize();

        internal void Initialize()
        {
            if (pivot != null) return;
            targets = GetComponentsInChildren<Renderer>(true);
            if (targets.Length == 0) return;
            var bounds = new Bounds();
            bool first = true;
            foreach (var renderer in targets)
            {
                var local = renderer.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var point = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            var children = new Transform[transform.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = transform.GetChild(i);
            pivot = new GameObject("Leaf Spin Pivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = bounds.center;
            foreach (var child in children) child.SetParent(pivot, true);
        }

        void OnDisable() => IsDragging = false;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || mouse.rightButton.isPressed || !Application.isFocused)
            {
                IsDragging = false;
                return;
            }
            var position = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            ProcessPointer(Camera.main, position, pressed, mouse.leftButton.isPressed, pressed && OverUI(position));
        }

        bool OverUI(Vector2 position)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            uiHits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = position }, uiHits);
            foreach (var hit in uiHits)
                if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Canvas>() != null) return true;
            return false;
        }

        internal bool HitsLeaf(Camera camera, Vector2 position)
        {
            if (camera == null || targets == null) return false;
            var ray = camera.ScreenPointToRay(position);
            foreach (var renderer in targets)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var localRay = new Ray(renderer.transform.InverseTransformPoint(ray.origin),
                    renderer.transform.InverseTransformVector(ray.direction));
                if (renderer.localBounds.IntersectRay(localRay)) return true;
            }
            return false;
        }

        internal void ProcessPointer(Camera camera, Vector2 position, bool pressed, bool held, bool overUI)
        {
            if (pivot == null || camera == null || !held)
            {
                IsDragging = false;
                return;
            }
            if (pressed)
            {
                IsDragging = !overUI && HitsLeaf(camera, position);
                previousPointer = position;
                return;
            }
            if (!IsDragging) return;
            var delta = position - previousPointer;
            previousPointer = position;
            yaw = Mathf.Repeat(yaw - delta.x * degreesPerPixel, 360f);
            pitch = Mathf.Clamp(pitch + delta.y * degreesPerPixel, -65f, 65f);
            pivot.localRotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
        }
    }
}
