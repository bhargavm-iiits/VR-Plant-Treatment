using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// The sunlight step's response (Play mode): the room lights fade (the realtime sun and the ambient/reflection light dim,
    /// and three soft dark shells round the plant darken everything beyond arm's reach, including the baked hall lighting;
    /// no post-processing, so it stays cheap on Quest), the grow lamp's warm spot focuses on the plant, the plant's leaves
    /// lift and turn a little towards the light while its colour improves one step, then the room lighting returns.
    /// The glass stays closed throughout.
    /// </summary>
    public sealed class SunlightResponse : MonoBehaviour
    {
        const float FadeIn = 0.9f, FadeOut = 0.9f, SpotIntensity = 28f, RoomDim = 0.15f, MaxTilt = 6f, Lift = 0.05f;
        static readonly float[] ShellAlpha = { 0.45f, 0.42f, 0.4f };
        static readonly float[] ShellRadius = { 0f, 0.35f, 0.75f };

        readonly List<MeshRenderer> shells = new List<MeshRenderer>();
        readonly List<Material> shellMaterials = new List<Material>();
        SunLampStation lamp;
        Transform plant;
        Vector3 plantCentre;
        Quaternion plantRotation;
        Vector3 plantScale;
        Vector3 tiltAxis;
        Light sun;
        float sunIntensity, ambientIntensity, reflectionIntensity;
        float time = -1f, duration;
        Action<float> onColour;
        Action done;
        bool colourSent;

        public bool Playing => time >= 0f;
        /// <summary>0..1 how dark the room is now (verification).</summary>
        public float Darkness { get; private set; }

        public void Bind(SunLampStation station, GuidedProps props)
        {
            lamp = station;
            var mesh = props.Own(GuidedProps.InvertedSphere());
            for (var i = 0; i < ShellAlpha.Length; i++)
            {
                var go = new GameObject("RoomDimShell" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                var material = props.Tint("RoomDim" + i, new Color(0f, 0f, 0f, 0f), 3090 + i);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                shells.Add(renderer);
                shellMaterials.Add(material);
            }
        }

        /// <summary>
        /// Plays the response on <paramref name="specimen"/> (bounds centre <paramref name="centre"/>) for
        /// <paramref name="seconds"/>; <paramref name="colourStep"/> is called once at the peak (improve the colour), then
        /// <paramref name="finished"/> after the room lighting is back.
        /// </summary>
        public void Play(Transform specimen, Vector3 centre, float seconds, Action<float> colourStep, Action finished)
        {
            plant = specimen;
            plantCentre = centre;
            duration = Mathf.Max(seconds, FadeIn + FadeOut + 0.5f);
            onColour = colourStep;
            done = finished;
            colourSent = false;
            time = 0f;
            if (plant != null)
            {
                plantRotation = plant.localRotation;
                plantScale = plant.localScale;
                var toLamp = lamp != null ? lamp.Emitter - centre : Vector3.up;
                toLamp.y = 0f;
                tiltAxis = toLamp.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, toLamp.normalized) : Vector3.right;
            }

            sun = FindSun();
            sunIntensity = sun != null ? sun.intensity : 0f;
            ambientIntensity = RenderSettings.ambientIntensity;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            if (lamp != null)
                lamp.AimAt(centre);

            // The innermost shell encloses the viewer (it must, to darken what is behind the plant and around them).
            var eye = Camera.main != null ? Camera.main.transform.position : centre + Vector3.back;
            var inner = Mathf.Max(Vector3.Distance(eye, centre) + 0.25f, 0.9f);
            for (var i = 0; i < shells.Count; i++)
            {
                shells[i].transform.position = centre;
                shells[i].transform.localScale = Vector3.one * (inner + ShellRadius[i]);
                shells[i].enabled = true;
            }
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            foreach (var light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                    return light;
            return null;
        }

        void Update()
        {
            if (time < 0f)
                return;
            time += Time.deltaTime;
            var fade = time < FadeIn ? time / FadeIn : time > duration - FadeOut ? (duration - time) / FadeOut : 1f;
            fade = GuidedGeometry.Ease(Mathf.Clamp01(fade));
            Darkness = fade;
            for (var i = 0; i < shellMaterials.Count; i++)
                shellMaterials[i].SetColor("_BaseColor", new Color(0f, 0f, 0f, ShellAlpha[i] * fade));
            if (sun != null)
                sun.intensity = Mathf.Lerp(sunIntensity, sunIntensity * RoomDim, fade);
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientIntensity, ambientIntensity * RoomDim, fade);
            RenderSettings.reflectionIntensity = Mathf.Lerp(reflectionIntensity, reflectionIntensity * RoomDim, fade);
            lamp?.SetGlow(fade, SpotIntensity);

            // The leaves lift and lean towards the light, peaking mid-way, and keep a little of it.
            var mid = Mathf.Clamp01((time - FadeIn * 0.5f) / Mathf.Max(0.1f, duration - FadeIn - FadeOut));
            var response = Mathf.Sin(Mathf.Clamp01(mid) * Mathf.PI);
            var keep = Mathf.Clamp01(mid * 2f) * 0.3f;
            if (plant != null)
            {
                var lean = MaxTilt * Mathf.Max(response, keep);
                plant.localRotation = Quaternion.AngleAxis(-lean, plant.parent != null ? plant.parent.InverseTransformDirection(tiltAxis) : tiltAxis) * plantRotation;
                plant.localScale = new Vector3(plantScale.x, plantScale.y * (1f + Lift * Mathf.Max(response, keep)), plantScale.z);
            }

            if (!colourSent && mid >= 0.35f)
            {
                colourSent = true;
                onColour?.Invoke(mid);
            }

            if (time < duration)
                return;
            Finish();
        }

        /// <summary>Ends at once: lights restored, the plant keeps its small lean (verification may call it).</summary>
        public void Finish()
        {
            if (time < 0f)
                return;
            time = -1f;
            Darkness = 0f;
            foreach (var shell in shells)
                shell.enabled = false;
            if (sun != null)
                sun.intensity = sunIntensity;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            lamp?.SetGlow(0f, SpotIntensity);
            if (!colourSent)
                onColour?.Invoke(1f);
            if (plant != null)
                plant.localScale = plantScale;
            var then = done;
            done = null;
            then?.Invoke();
        }

        void OnDisable() => Finish();
    }
}
