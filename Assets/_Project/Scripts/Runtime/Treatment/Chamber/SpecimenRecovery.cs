using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The chamber specimen's recovery after a correct Apply: the FarmRecovery shader's _DiseaseAmount eases from its
    /// current value to 0 over <see cref="Seconds"/> with a smootherstep (lesions shrink back, yellow/brown turns green), the
    /// leaves take a soft green glow that peaks half way, the plant perks up (its root stretches about 2 % taller) and a
    /// one-shot sparkle rises at the end.
    /// <para>
    /// Apply is pressed at the kiosk, from where the chamber is far off to the side (about 85 degrees from the kiosk's
    /// view), so a recovery started on the press finished before anyone could turn round and see it. Apply therefore
    /// <see cref="Queue"/>s the recovery: it starts the moment the viewer looks at the plant (or after <see cref="MaxWait"/>
    /// seconds at the latest), and the kiosk's status line says where to look and how far it has got.
    /// </para>
    /// The values are written to the specimen's own material copies (the station clones them per specimen), not through a
    /// property block, so nothing that clears property blocks or re-reads the materials can bring the disease back; they
    /// stay recovered until the case or specimen changes (a new <see cref="Bind"/>). No per-frame allocations.
    /// </summary>
    public sealed class SpecimenRecovery
    {
        public const float Seconds = 8f;
        /// <summary>The perk-up: the specimen root's height grows by this fraction.</summary>
        public const float Lift = 0.02f;
        public const int SparkleCount = 40;
        /// <summary>A queued recovery starts once the plant is within this angle of the viewer's gaze...</summary>
        public const float ViewAngle = 38f;
        /// <summary>...or after this many seconds, whichever comes first.</summary>
        public const float MaxWait = 12f;
        /// <summary>Peak green lift of the glow (fraction added to the green channel at the half-way point).</summary>
        public const float GlowGreen = 0.18f;

        static readonly int DiseaseAmountId = Shader.PropertyToID(SpecimenGeometry.DiseaseAmountProperty);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<Renderer> renderers = new List<Renderer>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Color> baseColors = new List<Color>();
        Transform root;
        Vector3 baseScale = Vector3.one;
        float start;
        float elapsed;
        float waited;
        ParticleSystem sparkle;
        Material sparkleMaterial;

        /// <summary>Applied, waiting for the viewer to look at the plant.</summary>
        public bool Waiting { get; private set; }
        public bool Active { get; private set; }
        /// <summary>The recovery finished (the plant is healthy and stays so until the case changes).</summary>
        public bool Done { get; private set; }
        /// <summary>True once Apply asked for the recovery (waiting, running or finished).</summary>
        public bool Engaged => Waiting || Active || Done;
        /// <summary>The disease amount shown now.</summary>
        public float Amount { get; private set; } = 1f;
        public float Progress => Mathf.Clamp01(elapsed / Seconds);
        /// <summary>How far the plant has recovered, 0..100 (the eased curve, as the leaves show it).</summary>
        public int Percent => Done ? 100 : Active ? Mathf.RoundToInt(100f * Smootherstep(Progress)) : 0;
        /// <summary>Seconds waited for the viewer so far.</summary>
        public float Waited => waited;
        /// <summary>The material copies the recovery writes (verification).</summary>
        public IReadOnlyList<Material> Materials => materials;

        /// <summary>Smootherstep (6t^5 - 15t^4 + 10t^3) on 0..1: zero speed and acceleration at both ends.</summary>
        public static float Smootherstep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>The disease amount <paramref name="seconds"/> into a recovery from <paramref name="from"/>.</summary>
        public static float AmountAt(float from, float seconds) => Mathf.Lerp(from, 0f, Smootherstep(seconds / Seconds));

        /// <summary>The root's height factor <paramref name="seconds"/> into the recovery (1 .. 1 + <see cref="Lift"/>).</summary>
        public static float LiftAt(float seconds) => 1f + Lift * Smootherstep(seconds / Seconds);

        /// <summary>
        /// The leaves' tint multiplier at <paramref name="progress"/> (0..1): white at both ends, greener in between (a
        /// sin^2 bell peaking at 0.5), so the colour change reads as a living glow and the end state is the plain material.
        /// </summary>
        public static Color GlowTint(float progress)
        {
            var bell = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            bell *= bell;
            return new Color(1f - 0.05f * bell, 1f + GlowGreen * bell, 1f - 0.08f * bell, 1f);
        }

        /// <summary>Whether a queued recovery may start now.</summary>
        public static bool ShouldStart(bool viewerSees, float waitedSeconds) => viewerSees || waitedSeconds >= MaxWait;

        /// <summary>Watches a new specimen (null: none) without materials of its own (tests).</summary>
        public void Bind(GameObject specimen) => Bind(specimen, null);

        /// <summary>
        /// Watches a new specimen (null: none). <paramref name="owned"/> are its per-specimen material copies (never shared
        /// assets): those that carry _DiseaseAmount are the ones the recovery writes. The previous specimen is reset.
        /// </summary>
        public void Bind(GameObject specimen, IReadOnlyList<Material> owned)
        {
            Reset();
            renderers.Clear();
            materials.Clear();
            baseColors.Clear();
            root = specimen != null ? specimen.transform : null;
            if (root == null)
                return;
            baseScale = root.localScale;
            foreach (var renderer in specimen.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                    continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.HasProperty(DiseaseAmountId))
                    {
                        renderers.Add(renderer);
                        break;
                    }
            }

            if (owned == null)
                return;
            foreach (var material in owned)
            {
                if (material == null || !material.HasProperty(DiseaseAmountId))
                    continue;
                materials.Add(material);
                baseColors.Add(material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white);
            }
        }

        /// <summary>Back to the untreated look: the original tint and scale, nothing waiting or running.</summary>
        public void Reset()
        {
            Waiting = false;
            Active = false;
            Done = false;
            elapsed = 0f;
            waited = 0f;
            for (var i = 0; i < materials.Count; i++)
                if (materials[i] != null && materials[i].HasProperty(BaseColorId))
                    materials[i].SetColor(BaseColorId, baseColors[i]);
            if (root != null)
                root.localScale = baseScale;
        }

        /// <summary>Apply: the recovery from <paramref name="from"/> waits for the viewer (ignored once engaged).</summary>
        public bool Queue(float from)
        {
            if (Engaged || root == null)
                return false;
            start = Mathf.Clamp01(from);
            Amount = start;
            elapsed = 0f;
            waited = 0f;
            Waiting = true;
            return true;
        }

        /// <summary>Starts the recovery from <paramref name="from"/> right away (ignored while running or once finished).</summary>
        public bool Begin(float from)
        {
            if (Engaged || root == null)
                return false;
            start = Mathf.Clamp01(from);
            Amount = start;
            elapsed = 0f;
            Active = true;
            return true;
        }

        /// <summary>Advances the recovery as if the viewer were watching; true on the frame it finishes.</summary>
        public bool Tick(float deltaTime) => Tick(deltaTime, true);

        /// <summary>
        /// Advances the recovery: a queued one starts when <paramref name="viewerSees"/> the plant (or the wait runs out);
        /// a running one eases on. True on the frame it finishes (the sparkle fires).
        /// </summary>
        public bool Tick(float deltaTime, bool viewerSees)
        {
            if (Waiting)
            {
                waited += deltaTime;
                if (!ShouldStart(viewerSees, waited))
                    return false;
                Waiting = false;
                Active = true;
                elapsed = 0f;
                Write(start, 0f);
                return false;
            }

            if (!Active)
                return false;
            elapsed += deltaTime;
            Amount = AmountAt(start, elapsed);
            Write(Amount, Progress);
            if (root != null)
                root.localScale = new Vector3(baseScale.x, baseScale.y * LiftAt(elapsed), baseScale.z);
            if (elapsed < Seconds)
                return false;
            Active = false;
            Done = true;
            Amount = 0f;
            Write(0f, 1f);
            Sparkle();
            return true;
        }

        void Write(float amount, float progress)
        {
            var tint = GlowTint(progress);
            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;
                material.SetFloat(DiseaseAmountId, amount);
                if (material.HasProperty(BaseColorId))
                {
                    var b = baseColors[i];
                    material.SetColor(BaseColorId, new Color(b.r * tint.r, b.g * tint.g, b.b * tint.b, b.a));
                }
            }
        }

        /// <summary>The glow sparkle's material (a green-tinted copy of the chamber's accent glow) and its parent; set once.</summary>
        public void SetSparkle(Material accent, Transform parent)
        {
            if (sparkle != null || accent == null || parent == null)
                return;
            sparkleMaterial = ChamberGlassPhysics.Tinted(accent, new Color(0.55f, 1f, 0.6f, 1f), "sparkle");
            sparkle = ChamberGlassPhysics.CreateParticles("RecoverySparkle", parent, sparkleMaterial, SparkleCount, false, -0.05f);
        }

        void Sparkle()
        {
            if (sparkle == null || root == null)
                return;
            var bounds = new Bounds(root.position, Vector3.zero);
            var any = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!any)
                    bounds = renderer.bounds;
                else
                    bounds.Encapsulate(renderer.bounds);
                any = true;
            }

            var seed = 0x9E3779B9u;
            float Next()
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                return (seed & 0xFFFFFF) / (float)0x1000000;
            }

            for (var i = 0; i < SparkleCount; i++)
            {
                var p = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, Next()), Mathf.Lerp(bounds.min.y + bounds.size.y * 0.25f, bounds.max.y, Next()),
                    Mathf.Lerp(bounds.min.z, bounds.max.z, Next()));
                sparkle.Emit(new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = new Vector3(Next() - 0.5f, 0.6f + Next(), Next() - 0.5f) * 0.12f,
                    startSize = Mathf.Lerp(0.008f, 0.018f, Next()),
                    startLifetime = Mathf.Lerp(0.9f, 1.6f, Next()),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        /// <summary>Destroys the sparkle's runtime material (the station is going away).</summary>
        public void Dispose()
        {
            if (sparkleMaterial != null)
                Object.Destroy(sparkleMaterial);
        }
    }
}
