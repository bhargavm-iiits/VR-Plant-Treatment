using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.UI
{
    /// <summary>
    /// Makes a world-space uGUI button feel like a real key (added at runtime; works with controller rays, hand poke and
    /// the desktop mouse ray, and leaves <see cref="PressGuard"/> and <see cref="HoldRepeatButton"/> untouched). The
    /// button's own image becomes the dark base in the screen plane with a rim, its face (a new child that takes the colour
    /// transition and carries the label) stands 5 mm proud with a gloss band, and travels down to the base while pressed.
    /// Hover brightens the rim; a disabled key is dimmed and does not move. A press sends a short haptic pulse to the
    /// controller that pressed it (<see cref="TrackedDeviceEventData.interactor"/> as an <see cref="XRBaseInputInteractor"/>)
    /// and plays an optional procedural click. Bound to a 3D cap instead (<see cref="BindCap"/>), the cap itself travels.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class PhysicalButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>Press travel (and how far the face stands proud of the screen).</summary>
        public const float TravelMetres = 0.005f;
        const float PressSeconds = 0.05f, PulseSeconds = 0.14f;

        /// <summary>Play the procedural click on presses (world-space, quiet).</summary>
        public static bool ClickSound = true;

        static readonly Color BaseColor = new Color(0.035f, 0.04f, 0.05f, 1f);
        static readonly Color RimHover = new Color(0.13f, 0.83f, 0.93f, 1f);
        static readonly Color GlossColor = new Color(1f, 1f, 1f, 0.11f);
        static AudioClip click;

        Button button;
        Image baseImage;
        RectTransform face;
        Image faceImage;
        Image gloss;
        Transform cap;
        Vector3 capHome;
        Vector3 capAxis;
        float capTravelLocal;
        bool hovered, down, held;
        float pulse, press = -1f;
        bool lastInteractable = true, lastHovered;
        AudioSource source;

        public Button Button => button;
        /// <summary>0 = at rest, 1 = fully pressed.</summary>
        public float Pressed => Mathf.Max(0f, press);
        public bool IsCap => cap != null;

        /// <summary>Adds (or returns) the physical look on <paramref name="target"/>.</summary>
        public static PhysicalButton Attach(Button target)
        {
            if (target == null)
                return null;
            var physical = target.GetComponent<PhysicalButton>();
            return physical != null ? physical : target.gameObject.AddComponent<PhysicalButton>();
        }

        /// <summary>A key whose visible part is the 3D <paramref name="capTransform"/> (the WATER ON / OFF keys): the cap travels.</summary>
        public static PhysicalButton AttachCap(Button target, Transform capTransform)
        {
            var physical = Attach(target);
            physical?.BindCap(capTransform);
            return physical;
        }

        void Awake()
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(() => pulse = PulseSeconds);
        }

        void Start()
        {
            if (cap == null)
                BuildFace();
        }

        public void BindCap(Transform capTransform)
        {
            if (capTransform == null)
                return;
            cap = capTransform;
            capHome = cap.localPosition;
            // Down the cap's own up axis, in the cap's parent space (local units).
            capAxis = cap.parent != null ? cap.parent.InverseTransformDirection(cap.up).normalized : cap.up;
            var parentScale = cap.parent != null ? cap.parent.lossyScale.y : 1f;
            capTravelLocal = TravelMetres / Mathf.Max(parentScale, 1e-4f);
        }

        /// <summary>The button image becomes the base; a raised face (the colour-tinted target) carries the label and outline.</summary>
        void BuildFace()
        {
            if (face != null || button == null)
                return;
            baseImage = button.targetGraphic as Image;
            if (baseImage == null || baseImage.color.a < 0.01f)
                return; // invisible hit areas (keys over 3D caps) keep their look
            var go = new GameObject("Face", typeof(RectTransform), typeof(Image));
            face = (RectTransform)go.transform;
            face.SetParent(transform, false);
            face.anchorMin = Vector2.zero;
            face.anchorMax = Vector2.one;
            face.offsetMin = new Vector2(3f, 3f);
            face.offsetMax = new Vector2(-3f, -3f);
            face.SetAsFirstSibling();
            faceImage = go.GetComponent<Image>();
            faceImage.sprite = baseImage.sprite;
            faceImage.type = baseImage.type;
            faceImage.pixelsPerUnitMultiplier = baseImage.pixelsPerUnitMultiplier;
            faceImage.color = Color.white;
            faceImage.raycastTarget = true;

            // Children (label, selected outline) move onto the face so they travel with it.
            var children = new Transform[transform.childCount];
            for (var i = 0; i < children.Length; i++)
                children[i] = transform.GetChild(i);
            foreach (var child in children)
                if (child != face)
                    child.SetParent(face, true);

            var glossGo = new GameObject("Gloss", typeof(RectTransform), typeof(Image));
            var glossRect = (RectTransform)glossGo.transform;
            glossRect.SetParent(face, false);
            glossRect.SetAsFirstSibling();
            glossRect.anchorMin = new Vector2(0f, 0.52f);
            glossRect.anchorMax = Vector2.one;
            glossRect.offsetMin = new Vector2(4f, 0f);
            glossRect.offsetMax = new Vector2(-4f, -3f);
            gloss = glossGo.GetComponent<Image>();
            gloss.sprite = baseImage.sprite;
            gloss.type = baseImage.type;
            gloss.pixelsPerUnitMultiplier = baseImage.pixelsPerUnitMultiplier;
            gloss.color = GlossColor;
            gloss.raycastTarget = false;

            // The colour transition now tints the face; the base keeps a dark rim.
            button.targetGraphic = faceImage;
            baseImage.color = BaseColor;
            faceImage.canvasRenderer.SetColor(button.interactable ? button.colors.normalColor : button.colors.disabledColor);
            press = -1f;
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            down = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (button == null || !button.IsInteractable())
                return;
            down = true;
            if (eventData is TrackedDeviceEventData tracked && tracked.interactor is XRBaseInputInteractor input)
                input.SendHapticImpulse(0.35f, 0.04f);
            PlayClick();
        }

        public void OnPointerUp(PointerEventData eventData) => down = false;

        void OnDisable()
        {
            hovered = false;
            down = false;
            pulse = 0f;
            press = -1f;
            Apply(0f);
        }

        /// <summary>A short press animation (programmatic presses, tests).</summary>
        public void Pulse() => pulse = PulseSeconds;

        /// <summary>Holds the key down (a verification shot of a finger on the key) or lets it go.</summary>
        public void HoldDown(bool value)
        {
            held = value;
            press = -1f;
            Apply(value ? 1f : 0f);
        }

        void LateUpdate()
        {
            if (button == null)
                return;
            var interactable = button.IsInteractable();
            pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime);
            var target = interactable && (down || held || pulse > 0f) ? 1f : 0f;
            var next = press < 0f ? target : Mathf.MoveTowards(press, target, Time.unscaledDeltaTime / PressSeconds);
            if (next == press && interactable == lastInteractable && hovered == lastHovered)
                return;
            press = next;
            lastInteractable = interactable;
            lastHovered = hovered;
            Apply(press);
            if (baseImage != null && face != null)
            {
                baseImage.color = interactable && hovered ? Color.Lerp(BaseColor, RimHover, 0.55f) : interactable ? BaseColor : new Color(BaseColor.r, BaseColor.g, BaseColor.b, 0.55f);
                gloss.color = interactable ? GlossColor : new Color(1f, 1f, 1f, 0.03f);
            }
        }

        void Apply(float amount)
        {
            if (cap != null)
            {
                cap.localPosition = capHome - capAxis * (capTravelLocal * amount);
                return;
            }

            if (face == null)
                return;
            // Canvas units along the screen normal (-Z is towards the user): proud at rest, flush when pressed.
            var unit = Mathf.Max(face.lossyScale.z, 1e-6f);
            var proud = TravelMetres / unit;
            var p = face.localPosition;
            face.localPosition = new Vector3(p.x, p.y, -proud * (1f - amount));
        }

        void PlayClick()
        {
            if (!ClickSound || !Application.isPlaying)
                return;
            if (click == null)
                click = MakeClick();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.minDistance = 0.3f;
                source.maxDistance = 6f;
                source.volume = 0.35f;
            }

            source.PlayOneShot(click);
        }

        /// <summary>A 25 ms damped click (no audio asset needed).</summary>
        static AudioClip MakeClick()
        {
            const int rate = 44100;
            var samples = new float[rate / 40];
            var seed = 22222u;
            for (var i = 0; i < samples.Length; i++)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                var noise = (seed & 0xFFFF) / 32768f - 1f;
                var t = i / (float)rate;
                var envelope = Mathf.Exp(-t * 260f);
                samples[i] = (noise * 0.35f + Mathf.Sin(t * 2f * Mathf.PI * 1900f) * 0.65f) * envelope * 0.8f;
            }

            var clip = AudioClip.Create("PhysicalButtonClick", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static Mesh keycap;

        /// <summary>
        /// A bevelled keycap in the unit box (-0.5..0.5): straight chamfered sides up to 70 % of the height, then a bevel to
        /// a top inset by 12 % (flat normals). Scaled like the cube it replaces.
        /// </summary>
        public static Mesh KeycapMesh
        {
            get
            {
                if (keycap != null)
                    return keycap;
                const float chamfer = 0.12f, inset = 0.12f, shoulder = 0.2f;
                Vector2[] Ring(float half)
                {
                    var c = chamfer * half * 2f;
                    return new[]
                    {
                        new Vector2(half - c, -half), new Vector2(half, -half + c), new Vector2(half, half - c), new Vector2(half - c, half),
                        new Vector2(-half + c, half), new Vector2(-half, half - c), new Vector2(-half, -half + c), new Vector2(-half + c, -half),
                    };
                }

                var bottom = Ring(0.5f);
                var top = Ring(0.5f - inset);
                var vertices = new System.Collections.Generic.List<Vector3>();
                var normals = new System.Collections.Generic.List<Vector3>();
                var triangles = new System.Collections.Generic.List<int>();
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
                {
                    var n = Vector3.Cross(b - a, d - a).normalized;
                    var s = vertices.Count;
                    vertices.Add(a);
                    vertices.Add(b);
                    vertices.Add(c);
                    vertices.Add(d);
                    for (var k = 0; k < 4; k++)
                        normals.Add(n);
                    triangles.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }

                for (var i = 0; i < 8; i++)
                {
                    var j = (i + 1) % 8;
                    Vector3 P(Vector2 p, float y) => new Vector3(p.x, y, p.y);
                    // Side wall (bottom to shoulder), then the bevel (shoulder to the inset top).
                    Quad(P(bottom[i], -0.5f), P(bottom[i], shoulder), P(bottom[j], shoulder), P(bottom[j], -0.5f));
                    Quad(P(bottom[i], shoulder), P(top[i], 0.5f), P(top[j], 0.5f), P(bottom[j], shoulder));
                }

                var centre = vertices.Count;
                vertices.Add(new Vector3(0f, 0.5f, 0f));
                normals.Add(Vector3.up);
                for (var i = 0; i < 8; i++)
                {
                    vertices.Add(new Vector3(top[i].x, 0.5f, top[i].y));
                    normals.Add(Vector3.up);
                }

                for (var i = 0; i < 8; i++)
                    triangles.AddRange(new[] { centre, centre + 1 + (i + 1) % 8, centre + 1 + i });
                keycap = new Mesh { name = "Keycap", hideFlags = HideFlags.DontSave };
                keycap.SetVertices(vertices);
                keycap.SetNormals(normals);
                keycap.SetTriangles(triangles, 0);
                keycap.RecalculateBounds();
                return keycap;
            }
        }
    }
}
