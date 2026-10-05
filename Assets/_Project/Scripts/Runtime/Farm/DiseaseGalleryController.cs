using System.Collections.Generic;
using BTP.Plants;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace BTP.Farm
{
    /// <summary>
    /// Farm disease gallery. When the user is near a specimen and selects it, a ring of cards pops up
    /// around the plant, one per PlantVillage disease class, each with a sample leaf image and the
    /// disease name. Only one ring is open at a time. The cards are a pool built once in Awake (one
    /// quad each for border, panel and image, plus TextMeshPro text), so opening and animating
    /// allocate nothing. The ring is an illustration of what the dataset contains; it makes no
    /// statement about the plants in the scene.
    /// </summary>
    [DefaultExecutionOrder(100)] // after XR Interaction Toolkit has dispatched this frame's select events
    public sealed class DiseaseGalleryController : MonoBehaviour
    {
        public const string HintText = "Move closer to see diseases";

        const int MaxCards = 9;
        const float CardWidth = 0.50f;
        const float CardHeight = 0.72f;

        [SerializeField] DiseaseGalleryCatalog catalog;
        [SerializeField] Material borderMaterial;
        [SerializeField] Material panelMaterial;
        [SerializeField] Material imageMaterial;
        [Tooltip("Specimens farther than this (metres) do not open a ring.")]
        [SerializeField, Min(1f)] float nearDistance = 4f;
        [Tooltip("An open ring closes when the viewer walks farther than this from its plant.")]
        [SerializeField, Min(1f)] float closeDistance = 6f;
        [SerializeField, Min(0.5f)] float ringHeight = 1.3f;
        [SerializeField, Min(0.01f)] float stagger = 0.07f;
        [SerializeField, Min(0.1f)] float popDuration = 0.55f;
        [Tooltip("Slow idle rotation of the open ring in degrees per second. 0 = still.")]
        [SerializeField] float idleSpinDegreesPerSecond = 4f;
        [Tooltip("Gentle idle bob in metres. 0 = none.")]
        [SerializeField, Min(0f)] float bobMetres = 0.012f;

        sealed class Card
        {
            public Transform Root;
            public MeshRenderer Image;
            public TextMeshPro Title;
        }

        enum Phase { Closed, Opening, Open, Closing }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static Mesh quad;

        readonly List<XRRayInteractor> rays = new List<XRRayInteractor>();
        Card[] cards;
        MaterialPropertyBlock block;
        Camera viewer;
        Phase phase;
        float time;
        float totalTime;
        float spin;
        int count;
        int clickFrame = -1;
        string openPlantId;
        Transform specimen;
        float radius;

        string pendingPlantId;
        Transform pendingSpecimen;
        float pendingExtent;

        public bool IsOpen => phase != Phase.Closed;
        public string OpenPlantId => phase == Phase.Closed ? null : openPlantId;

        internal void Bind(DiseaseGalleryCatalog diseaseCatalog, Material border, Material panel, Material image)
        {
            catalog = diseaseCatalog;
            borderMaterial = border;
            panelMaterial = panel;
            imageMaterial = image;
        }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            cards = new Card[MaxCards];
            for (var i = 0; i < MaxCards; i++)
                cards[i] = BuildCard(i);
        }

        /// <summary>
        /// Called by a specimen when it is selected. Opens (or toggles, or switches) the ring when the viewer
        /// is near. Returns false when the viewer is too far away, so the caller can show a hint.
        /// </summary>
        public bool HandlePlantClicked(string plantId, Transform plant, float extent)
        {
            clickFrame = Time.frameCount;
            var eye = Viewer();
            if (eye == null || !DiseaseRingLayout.IsWithin(eye.transform.position, plant.position, nearDistance))
            {
                if (phase != Phase.Closed)
                    Close();
                return false;
            }

            if ((phase == Phase.Opening || phase == Phase.Open) && openPlantId == plantId)
            {
                Close();
                return true;
            }

            if (phase != Phase.Closed)
            {
                pendingPlantId = plantId;
                pendingSpecimen = plant;
                pendingExtent = extent;
                Close();
                return true;
            }

            Open(plantId, plant, extent);
            return true;
        }

        public void Close()
        {
            if (phase == Phase.Closed || phase == Phase.Closing)
                return;
            phase = Phase.Closing;
        }

        void Open(string plantId, Transform plant, float extent)
        {
            if (catalog == null)
                return;
            var diseases = catalog.GetDiseases(plantId);
            if (diseases.Count == 0)
                return;

            count = Mathf.Min(diseases.Count, MaxCards);
            for (var i = 0; i < MaxCards; i++)
            {
                var card = cards[i];
                card.Root.gameObject.SetActive(false);
                if (i >= count)
                    continue;
                card.Title.text = diseases[i].displayName;
                block.Clear();
                block.SetTexture(BaseMapId, diseases[i].leaf);
                card.Image.SetPropertyBlock(block);
            }

            openPlantId = plantId;
            specimen = plant;
            radius = DiseaseRingLayout.RadiusFor(extent);
            spin = 0f;
            time = 0f;
            totalTime = DiseaseRingLayout.TotalTime(count, stagger, popDuration);
            phase = Phase.Opening;
            RefreshRays();
        }

        void RefreshRays()
        {
            rays.Clear();
            rays.AddRange(FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None));
        }

        void Update()
        {
            if (phase == Phase.Closed)
                return;

            if (specimen == null || ShouldClose())
                Close();

            var dt = Time.deltaTime;
            switch (phase)
            {
                case Phase.Opening:
                    time += dt;
                    if (time >= totalTime)
                    {
                        time = totalTime;
                        phase = Phase.Open;
                    }

                    break;
                case Phase.Open:
                    break;
                case Phase.Closing:
                    time -= dt * 1.5f;
                    if (time <= 0f)
                    {
                        FinishClose();
                        return;
                    }

                    break;
            }

            spin += idleSpinDegreesPerSecond * dt;
            Layout();
        }

        bool ShouldClose()
        {
            if (phase == Phase.Closing)
                return false;

            var eye = Viewer();
            if (eye != null && !DiseaseRingLayout.IsWithin(eye.transform.position, specimen.position, closeDistance))
                return true;

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.hKey.wasPressedThisFrame ||
                                     keyboard.f1Key.wasPressedThisFrame))
                return true;

            // A click that did not select a specimen this frame (empty space, UI, anything else).
            return clickFrame != Time.frameCount && ClickedThisFrame();
        }

        bool ClickedThisFrame()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed)
                return true;
            for (var i = 0; i < rays.Count; i++)
            {
                var ray = rays[i];
                if (ray != null && ray.isActiveAndEnabled && ray.selectInput.ReadWasPerformedThisFrame())
                    return true;
            }

            return false;
        }

        void FinishClose()
        {
            for (var i = 0; i < MaxCards; i++)
                cards[i].Root.gameObject.SetActive(false);
            phase = Phase.Closed;
            openPlantId = null;
            specimen = null;
            time = 0f;

            if (pendingSpecimen != null)
            {
                var id = pendingPlantId;
                var plant = pendingSpecimen;
                var extent = pendingExtent;
                pendingPlantId = null;
                pendingSpecimen = null;
                Open(id, plant, extent);
            }
        }

        void Layout()
        {
            var eye = Viewer();
            var eyePosition = eye != null ? eye.transform.position : specimen.position + Vector3.back * 3f;
            var centre = specimen.position + Vector3.up * ringHeight;
            for (var i = 0; i < count; i++)
            {
                var card = cards[i];
                var progress = DiseaseRingLayout.CardProgress(time, i, stagger, popDuration);
                if (progress <= 0f)
                {
                    card.Root.gameObject.SetActive(false);
                    continue;
                }

                var pop = DiseaseRingLayout.EaseOutBack(progress);
                // Cards start at the plant centre and spin out to their slot on the ring.
                var angle = DiseaseRingLayout.AngleDegrees(i, count, spin) - (1f - progress) * 220f;
                var position = centre + DiseaseRingLayout.Offset(angle, radius * pop);
                position.y += Mathf.Sin(Time.time * 1.3f + i * 0.9f) * bobMetres * progress;

                // Billboard about the vertical axis so the text always reads from where the viewer stands.
                var away = position - eyePosition;
                away.y = 0f;
                var root = card.Root;
                root.gameObject.SetActive(true);
                root.SetPositionAndRotation(position, away.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(away, Vector3.up) : Quaternion.identity);
                root.localScale = Vector3.one * Mathf.Max(pop, 0f);
            }
        }

        Camera Viewer()
        {
            if (viewer == null || !viewer.isActiveAndEnabled)
                viewer = Camera.main;
            return viewer;
        }

        // ------------------------------------------------------------------ card pool

        Card BuildCard(int index)
        {
            var root = new GameObject($"Card_{index}").transform;
            root.SetParent(transform, false);

            // The card faces -Z: the viewer sees it from the -Z side, so closer to the viewer = smaller z.
            AddQuad(root, "Border", borderMaterial, new Vector2(CardWidth + 0.02f, CardHeight + 0.02f), new Vector3(0f, 0f, 0.004f));
            AddQuad(root, "Panel", panelMaterial, new Vector2(CardWidth, CardHeight), new Vector3(0f, 0f, 0.002f));
            var image = AddQuad(root, "Leaf", imageMaterial, new Vector2(0.44f, 0.44f), new Vector3(0f, 0.11f, 0f));

            var title = AddText(root, "Name", new Vector2(0.46f, 0.21f), new Vector3(0f, -0.215f, -0.003f), 0.6f,
                FontStyles.Bold, Color.white);
            AddText(root, "Caption", new Vector2(0.46f, 0.05f), new Vector3(0f, -0.335f, -0.003f), 0.4f,
                FontStyles.Normal, new Color(0.62f, 0.7f, 0.76f)).text = "PlantVillage sample";

            root.gameObject.SetActive(false);
            return new Card { Root = root, Image = image, Title = title };
        }

        static MeshRenderer AddQuad(Transform parent, string name, Material material, Vector2 size, Vector3 position)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.GetComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        static TextMeshPro AddText(Transform parent, string name, Vector2 size, Vector3 position, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            // TextMeshPro 3D text: 1 font-size unit is about 0.1 m of em height, so 0.6 is roughly 0.042 m cap height.
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        static Mesh Quad()
        {
            if (quad != null)
                return quad;
            quad = new Mesh { name = "DiseaseCardQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            quad.triangles = new[] { 0, 3, 2, 0, 2, 1 }; // clockwise seen from -Z
            quad.RecalculateBounds();
            return quad;
        }
    }
}
