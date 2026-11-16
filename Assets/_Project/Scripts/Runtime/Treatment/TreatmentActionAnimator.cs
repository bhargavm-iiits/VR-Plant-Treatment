using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Shows a button action without hands: a copy of the tool lifts off the rack, flies to the target, plays a
    /// short tool-specific effect (magnifier hover, shears snip, spray mist, gloved-hand press), then flies back.
    /// The lesson result is applied at the moment the tool reaches the target. Timing uses
    /// <see cref="Time.deltaTime"/>, so the animation lasts the same time at any frame rate.
    /// </summary>
    public sealed class TreatmentActionAnimator : MonoBehaviour
    {
        /// <summary>How a tool is held at the target and which point of it meets the target.</summary>
        struct Grip
        {
            public bool Pointing;   // tip (+Y) points at the target; otherwise upright, facing the target
            public Vector3 Focus;   // tool-local point that is brought to the target
            public float Standoff;  // metres between that point and the target, toward the viewer
            public Effect Effect;
        }

        enum Effect { Press, Hover, Snip, Mist, Lower }

        static readonly Dictionary<string, Grip> Grips = new Dictionary<string, Grip>
        {
            { "magnifier", new Grip { Focus = new Vector3(0f, 0.13f, 0f), Standoff = 0.09f, Effect = Effect.Hover } },
            { "shears", new Grip { Pointing = true, Focus = new Vector3(0f, 0.2f, 0f), Standoff = 0.015f, Effect = Effect.Snip } },
            { "sprayer", new Grip { Focus = new Vector3(0f, 0.16f, 0.045f), Standoff = 0.22f, Effect = Effect.Mist } },
            { "water_wand", new Grip { Pointing = true, Focus = new Vector3(0f, 0.24f, 0f), Standoff = 0.16f, Effect = Effect.Mist } },
            { "transplant", new Grip { Focus = new Vector3(0f, 0.0f, 0f), Standoff = 0f, Effect = Effect.Lower } },
            { "containment_bag", new Grip { Focus = new Vector3(0f, 0.03f, 0f), Standoff = 0.06f, Effect = Effect.Lower } },
            { "insect_mesh", new Grip { Focus = new Vector3(0f, 0.03f, 0f), Standoff = 0.05f, Effect = Effect.Lower } },
            { "sticky_card", new Grip { Focus = new Vector3(0f, 0.08f, 0f), Standoff = 0.05f, Effect = Effect.Lower } },
            { "predator_release", new Grip { Focus = new Vector3(0f, 0.12f, 0f), Standoff = 0.06f, Effect = Effect.Lower } },
            { "spade", new Grip { Pointing = true, Focus = new Vector3(0f, 0.25f, 0f), Standoff = 0.02f, Effect = Effect.Press } },
            { "rake", new Grip { Pointing = true, Focus = new Vector3(0f, 0.23f, 0f), Standoff = 0.02f, Effect = Effect.Press } },
            { "stake", new Grip { Pointing = true, Focus = new Vector3(0f, 0.24f, 0f), Standoff = 0.02f, Effect = Effect.Lower } }
        };

        static readonly Grip HandGrip = new Grip { Focus = new Vector3(0f, 0.07f, 0f), Standoff = 0.04f, Effect = Effect.Press };
        static readonly Grip DefaultGrip = new Grip { Focus = new Vector3(0f, 0.06f, 0f), Standoff = 0.06f, Effect = Effect.Press };
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Translucent material for spray mist puffs (Materials/Treatment/Mist).")]
        [SerializeField] Material mistMaterial;
        [SerializeField, Min(0.05f)] float liftSeconds = 0.2f;
        [SerializeField, Min(0.05f)] float travelSeconds = 0.45f;
        [SerializeField, Min(0.05f)] float effectSeconds = 0.6f;
        [SerializeField, Min(0.05f)] float returnSeconds = 0.45f;
        [SerializeField, Min(0f)] float liftHeight = 0.14f;

        GameObject clone;
        Transform hiddenModel;
        readonly List<GameObject> puffs = new List<GameObject>();

        /// <summary>Length of one tool-to-target animation.</summary>
        public float UseSeconds => liftSeconds + travelSeconds + effectSeconds + returnSeconds;

        /// <summary>The flying tool copy while an animation plays (null otherwise).</summary>
        public GameObject ActiveTool => clone;

        internal void Bind(Material mist) => mistMaterial = mist;

        /// <summary>
        /// Flies the tool shown at <paramref name="rackSlot"/> to <paramref name="target"/>, plays its effect, calls
        /// <paramref name="onHit"/> half-way through the effect, and returns the tool to the rack.
        /// </summary>
        public IEnumerator Use(Transform rackSlot, string toolKey, Vector3 target, Vector3 viewer, Action onHit)
        {
            var hit = false;
            void Hit()
            {
                if (hit)
                    return;
                hit = true;
                onHit?.Invoke();
            }

            var model = rackSlot != null ? rackSlot.Find("Model") : null;
            if (model == null)
            {
                // Nothing to fly: still pace the action so the result reads as a step.
                yield return Wait(effectSeconds * 0.5f);
                Hit();
                yield return Wait(effectSeconds * 0.5f);
                yield break;
            }

            try
            {
                var grip = toolKey == null ? HandGrip : Grips.TryGetValue(toolKey, out var g) ? g : DefaultGrip;
                var scale = model.lossyScale;
                clone = Instantiate(model.gameObject, model.position, model.rotation, transform);
                clone.name = "AnimatedTool_" + (toolKey ?? "hand");
                clone.transform.localScale = Vector3.one;
                clone.transform.localScale = new Vector3(scale.x / clone.transform.lossyScale.x, scale.y / clone.transform.lossyScale.y,
                    scale.z / clone.transform.lossyScale.z);
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                hiddenModel = model;
                model.gameObject.SetActive(false);

                var startPosition = model.position;
                var startRotation = model.rotation;
                var lifted = startPosition + Vector3.up * liftHeight;

                // Pose at the target: approach from the viewer's side.
                var toward = viewer - target;
                var flat = new Vector3(toward.x, 0f, toward.z);
                flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.back;
                var direction = toward.sqrMagnitude > 1e-4f ? toward.normalized : flat;
                var rotation = grip.Pointing
                    ? Quaternion.LookRotation(-direction, Vector3.up) * Quaternion.Euler(90f, 0f, 0f)
                    : Quaternion.LookRotation(-flat, Vector3.up);
                var meet = target + (grip.Pointing ? direction : flat) * grip.Standoff;
                var arrive = meet - rotation * Vector3.Scale(grip.Focus, scale);

                // 1. Lift off the rack.
                yield return Move(startPosition, startRotation, lifted, startRotation, liftSeconds, null);
                // 2. Arc to the target.
                yield return Move(lifted, startRotation, arrive, rotation, travelSeconds, 0.22f);
                // 3. Effect; the lesson result lands half-way.
                yield return PlayEffect(grip, arrive, rotation, target, scale, Hit);
                // 4. Back to the rack.
                yield return Move(arrive, rotation, lifted, startRotation, returnSeconds, 0.22f);
                yield return Move(lifted, startRotation, startPosition, startRotation, liftSeconds * 0.75f, null);
            }
            finally
            {
                Hit();
                Cleanup();
            }
        }

        /// <summary>A short shake of the rack tool for a refused action (wrong tool or not yet).</summary>
        public IEnumerator Refuse(Transform rackSlot)
        {
            var model = rackSlot != null ? rackSlot.Find("Model") : null;
            if (model == null)
            {
                yield return Wait(0.3f);
                yield break;
            }

            var home = model.localPosition;
            try
            {
                for (var t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
                {
                    if (model == null)
                        yield break;
                    model.localPosition = home + Vector3.right * (Mathf.Sin(t * Mathf.PI * 6f) * 0.012f * (1f - t));
                    yield return null;
                }
            }
            finally
            {
                if (model != null)
                    model.localPosition = home;
            }
        }

        /// <summary>Stops an animation immediately (case change or reset) and puts the rack tool back.</summary>
        public void Cancel()
        {
            StopAllCoroutines();
            Cleanup();
        }

        void Cleanup()
        {
            if (clone != null)
                Destroy(clone);
            clone = null;
            if (hiddenModel != null)
                hiddenModel.gameObject.SetActive(true);
            hiddenModel = null;
            foreach (var puff in puffs)
                if (puff != null)
                    Destroy(puff);
            puffs.Clear();
        }

        static IEnumerator Wait(float seconds)
        {
            for (var t = 0f; t < seconds; t += Time.deltaTime)
                yield return null;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        IEnumerator Move(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation, float seconds, float? arc)
        {
            var control = (from + to) * 0.5f + Vector3.up * (arc ?? 0f);
            for (var t = 0f; ; t += Time.deltaTime / seconds)
            {
                var k = Ease(Mathf.Clamp01(t));
                var position = arc.HasValue
                    ? (1f - k) * (1f - k) * from + 2f * (1f - k) * k * control + k * k * to
                    : Vector3.LerpUnclamped(from, to, k);
                if (clone != null)
                    clone.transform.SetPositionAndRotation(position, Quaternion.Slerp(fromRotation, toRotation, k));
                if (t >= 1f)
                    yield break;
                yield return null;
            }
        }

        IEnumerator PlayEffect(Grip grip, Vector3 at, Quaternion rotation, Vector3 target, Vector3 scale, Action hit)
        {
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;
            var forward = (target - at).normalized;
            var nextPuff = 0f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / effectSeconds)
            {
                var s = Mathf.Clamp01(t);
                var position = at;
                var spin = Quaternion.identity;
                switch (grip.Effect)
                {
                    case Effect.Hover:
                        // Slow figure-eight over the target, like scanning a lesion.
                        position += right * (Mathf.Sin(s * Mathf.PI * 2f) * 0.018f) + up * (Mathf.Sin(s * Mathf.PI * 4f) * 0.008f);
                        break;
                    case Effect.Snip:
                        // Two quick snips: a short jab with a twist.
                        var snip = Mathf.Abs(Mathf.Sin(s * Mathf.PI * 2f));
                        position += forward * (snip * 0.02f);
                        spin = Quaternion.AngleAxis(snip * 18f, up);
                        break;
                    case Effect.Mist:
                        position += up * (Mathf.Sin(s * Mathf.PI * 6f) * 0.004f);
                        if (s >= nextPuff && s < 0.8f)
                        {
                            nextPuff = s + 0.07f;
                            var nozzle = at + rotation * Vector3.Scale(grip.Focus, scale);
                            StartCoroutine(Puff(nozzle, target));
                        }

                        break;
                    case Effect.Lower:
                        position += Vector3.down * (Mathf.Sin(s * Mathf.PI) * 0.05f);
                        break;
                    default:
                        // A press toward the target and back.
                        position += forward * (Mathf.Sin(s * Mathf.PI) * 0.035f);
                        break;
                }

                if (clone != null)
                    clone.transform.SetPositionAndRotation(position, spin * rotation);
                if (s >= 0.5f)
                    hit();
                yield return null;
            }

            hit();
        }

        /// <summary>One mist puff from the nozzle to the target: it grows and fades as it travels.</summary>
        IEnumerator Puff(Vector3 from, Vector3 to)
        {
            if (mistMaterial == null)
                yield break;
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "MistPuff";
            Destroy(puff.GetComponent<Collider>());
            puff.transform.SetParent(transform, true);
            var renderer = puff.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mistMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            puffs.Add(puff);
            var block = new MaterialPropertyBlock();
            var baseColor = mistMaterial.HasProperty(BaseColor) ? mistMaterial.GetColor(BaseColor) : new Color(0.75f, 0.92f, 1f, 0.32f);
            var jitter = UnityEngine.Random.insideUnitSphere * 0.025f;
            for (var t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                if (puff == null)
                    yield break;
                var k = Mathf.Clamp01(t);
                puff.transform.position = Vector3.Lerp(from, to + jitter, k);
                puff.transform.localScale = Vector3.one * Mathf.Lerp(0.015f, 0.07f, k);
                var color = baseColor;
                color.a = baseColor.a * 1.6f * (1f - k);
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
                yield return null;
            }

            puffs.Remove(puff);
            Destroy(puff);
        }
    }
}
