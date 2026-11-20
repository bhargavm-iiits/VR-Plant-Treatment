using System;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Treatment
{
    /// <summary>
    /// The SUNLIGHT station (built at runtime beside the chamber): a small lab table with an articulated grow lamp whose head
    /// leans over the chamber towards the plant, and a sign. Pressing the lamp or the table (controller ray, hand poke or
    /// pinch, desktop click) reports <see cref="Pressed"/>: the guide answers with the temperature target. During the
    /// sunlight response the lamp's face glows warm and a warm spot light (realtime, no shadows; only on while it plays)
    /// focuses on the plant from the lamp head.
    /// </summary>
    public sealed class SunLampStation : MonoBehaviour
    {
        const float TableHeight = 0.74f;

        Transform head;
        Transform face;
        MeshRenderer faceRenderer;
        Material faceOff, faceOn;
        Light spot;
        XRSimpleInteractable interactable;
        TMP_Text status;
        float glow;

        public event Action Pressed;
        public Light Spot => spot;
        public Transform Head => head;
        public float Glow => glow;
        /// <summary>World position the light leaves from (the lamp face).</summary>
        public Vector3 Emitter => face != null ? face.position : transform.position + Vector3.up * 1.5f;

        public static SunLampStation Build(Transform parent, GuidedProps props)
        {
            var root = new GameObject("SunlightStation").transform;
            root.SetParent(parent, false);
            var station = root.gameObject.AddComponent<SunLampStation>();
            station.Create(props);
            return station;
        }

        void Create(GuidedProps props)
        {
            var wood = props.Lit("SunTable", new Color(0.55f, 0.42f, 0.3f), 0.35f);
            var steel = props.Lit("SunSteel", new Color(0.22f, 0.24f, 0.26f), 0.6f, 0.6f);
            var shell = props.Lit("SunShell", new Color(0.93f, 0.78f, 0.25f), 0.5f);
            faceOff = props.Lit("SunFaceOff", new Color(0.95f, 0.92f, 0.82f), 0.8f);
            faceOn = props.Glow("SunFaceOn", new Color(1f, 0.82f, 0.5f), 2.2f);

            // Table: top, apron and four legs; the top carries a collider (things put down rest on it).
            GuidedProps.Part(PrimitiveType.Cube, "Top", transform, new Vector3(0f, TableHeight - 0.02f, 0f), new Vector3(0.62f, 0.04f, 0.5f), wood, null, true);
            GuidedProps.Part(PrimitiveType.Cube, "Apron", transform, new Vector3(0f, TableHeight - 0.08f, 0f), new Vector3(0.56f, 0.08f, 0.44f), wood);
            foreach (var x in new[] { -0.27f, 0.27f })
            foreach (var z in new[] { -0.21f, 0.21f })
                GuidedProps.Part(PrimitiveType.Cube, "Leg", transform, new Vector3(x, (TableHeight - 0.04f) * 0.5f, z), new Vector3(0.04f, TableHeight - 0.04f, 0.04f), steel);

            // Lamp: weighted base, upright, an arm leaning towards -X (the chamber side) and the head on it.
            var lamp = new GameObject("GrowLamp").transform;
            lamp.SetParent(transform, false);
            lamp.localPosition = new Vector3(0.08f, TableHeight, 0f);
            GuidedProps.Part(PrimitiveType.Cylinder, "Base", lamp, new Vector3(0f, 0.015f, 0f), new Vector3(0.2f, 0.015f, 0.2f), steel);
            GuidedProps.Part(PrimitiveType.Cylinder, "Upright", lamp, new Vector3(0f, 0.36f, 0f), new Vector3(0.03f, 0.35f, 0.03f), steel);
            var elbow = new Vector3(0f, 0.71f, 0f);
            var tip = new Vector3(-0.4f, 1.12f, 0.05f);
            var arm = tip - elbow;
            GuidedProps.Part(PrimitiveType.Cylinder, "Arm", lamp, (elbow + tip) * 0.5f, new Vector3(0.025f, arm.magnitude * 0.5f, 0.025f), steel,
                Quaternion.FromToRotation(Vector3.up, arm.normalized));
            GuidedProps.Part(PrimitiveType.Sphere, "Joint", lamp, elbow, Vector3.one * 0.05f, steel);
            head = new GameObject("Head").transform;
            head.SetParent(lamp, false);
            head.localPosition = tip;
            // A shallow cone shade (scaled cylinder) facing down-left; the glowing face under it.
            GuidedProps.Part(PrimitiveType.Cylinder, "Shade", head, Vector3.zero, new Vector3(0.18f, 0.035f, 0.18f), shell);
            GuidedProps.Part(PrimitiveType.Cylinder, "Cap", head, new Vector3(0f, 0.045f, 0f), new Vector3(0.08f, 0.025f, 0.08f), shell);
            var faceGo = GuidedProps.Part(PrimitiveType.Cylinder, "Face", head, new Vector3(0f, -0.037f, 0f), new Vector3(0.15f, 0.004f, 0.15f), faceOff);
            face = faceGo.transform;
            faceRenderer = faceGo.GetComponent<MeshRenderer>();

            var lightGo = new GameObject("TreatmentSpot");
            lightGo.transform.SetParent(head, false);
            lightGo.transform.localPosition = new Vector3(0f, -0.06f, 0f);
            spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.68f, 0.36f);
            spot.intensity = 0f;
            spot.range = 2.5f;
            spot.spotAngle = 34f;
            spot.innerSpotAngle = 18f;
            spot.shadows = LightShadows.None;
            spot.renderMode = LightRenderMode.ForcePixel;
            spot.enabled = false;

            // Sign on the table's front edge.
            var signGo = new GameObject("Sign", typeof(RectTransform));
            signGo.transform.SetParent(transform, false);
            var canvas = signGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)signGo.transform;
            rect.sizeDelta = new Vector2(600f, 150f);
            rect.localScale = Vector3.one * 0.001f;
            rect.localPosition = new Vector3(0f, TableHeight + 0.09f, -0.24f);
            rect.localRotation = Quaternion.Euler(18f, 0f, 0f);
            var back = new GameObject("Back", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            back.rectTransform.SetParent(rect, false);
            back.rectTransform.anchorMin = Vector2.zero;
            back.rectTransform.anchorMax = Vector2.one;
            back.rectTransform.offsetMin = back.rectTransform.offsetMax = Vector2.zero;
            back.sprite = ChamberDoorPopup.KeycapSprite;
            back.type = UnityEngine.UI.Image.Type.Sliced;
            back.color = new Color(0.07f, 0.1f, 0.13f, 0.95f);
            var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            title.rectTransform.SetParent(rect, false);
            title.rectTransform.anchorMin = new Vector2(0f, 0.45f);
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            if (TMP_Settings.defaultFontAsset != null)
                title.font = TMP_Settings.defaultFontAsset;
            title.text = "SUNLIGHT & TEMPERATURE";
            title.fontSize = 50f;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            title.color = new Color(1f, 0.82f, 0.4f);
            status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            status.rectTransform.SetParent(rect, false);
            status.rectTransform.anchorMin = Vector2.zero;
            status.rectTransform.anchorMax = new Vector2(1f, 0.48f);
            status.rectTransform.offsetMin = status.rectTransform.offsetMax = Vector2.zero;
            if (TMP_Settings.defaultFontAsset != null)
                status.font = TMP_Settings.defaultFontAsset;
            status.fontSize = 32f;
            status.alignment = TextAlignmentOptions.Center;
            status.color = UIPalette.TextSecondary;
            status.text = "Set the temperature on the kiosk, then Apply";

            // One interactable over the lamp and table (ray, poke, pinch, desktop click).
            var hit = new GameObject("PressVolume");
            hit.transform.SetParent(transform, false);
            var box = hit.AddComponent<BoxCollider>();
            box.center = new Vector3(-0.05f, TableHeight + 0.5f, 0f);
            box.size = new Vector3(0.72f, 1.0f, 0.5f);
            box.isTrigger = true;
            interactable = hit.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Add(box);
            interactable.selectEntered.AddListener(OnSelected);
        }

        void OnSelected(SelectEnterEventArgs args) => Pressed?.Invoke();

        /// <summary>Verification / desktop: the same as pressing the lamp.</summary>
        public void Press() => Pressed?.Invoke();

        public void SetStatus(string text)
        {
            if (status != null)
                status.text = text ?? "";
        }

        /// <summary>Aims the lamp head (its face, local -Y) at <paramref name="target"/>.</summary>
        public void AimAt(Vector3 target)
        {
            if (head == null)
                return;
            var direction = target - head.position;
            if (direction.sqrMagnitude > 1e-6f)
                head.rotation = Quaternion.FromToRotation(Vector3.down, direction.normalized);
        }

        /// <summary>0..1: the lamp face glow and the warm spot's share of <paramref name="maxIntensity"/>.</summary>
        public void SetGlow(float value, float maxIntensity)
        {
            glow = Mathf.Clamp01(value);
            if (faceRenderer != null)
                faceRenderer.sharedMaterial = glow > 0.05f ? faceOn : faceOff;
            if (spot == null)
                return;
            spot.enabled = glow > 0.001f;
            spot.intensity = glow * maxIntensity;
        }
    }
}
