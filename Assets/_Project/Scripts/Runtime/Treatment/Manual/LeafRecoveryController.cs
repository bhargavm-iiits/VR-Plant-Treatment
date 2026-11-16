using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Drives the manual-treatment amounts of the BTP/Leaf Recovery shader (_SpotFade, _YellowFade, _GreenGain,
    /// _VeinGain, _Worsen, _TreatGlow) on the specimen's own renderer with a MaterialPropertyBlock: the same leaf
    /// mesh and material throughout, no swap to a healthy leaf and no instant material switch. The displayed state
    /// moves smoothly towards the target of the last validated Apply (<see cref="LeafHealthMonitor"/>); time alone
    /// never sets a target. Detaching clears the block, so the step lesson sees the leaf exactly as authored.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class LeafRecoveryController : MonoBehaviour
    {
        /// <summary>Gradual enough to watch, and finished within one 12 s stage window.</summary>
        public const float DefaultTransitionSeconds = 3.5f;

        static readonly int SpotFadeId = Shader.PropertyToID("_SpotFade");
        static readonly int YellowFadeId = Shader.PropertyToID("_YellowFade");
        static readonly int GreenGainId = Shader.PropertyToID("_GreenGain");
        static readonly int VeinGainId = Shader.PropertyToID("_VeinGain");
        static readonly int WorsenId = Shader.PropertyToID("_Worsen");
        static readonly int TreatGlowId = Shader.PropertyToID("_TreatGlow");

        [Tooltip("Seconds for the leaf to move to a newly applied treatment result. Kept short: a whole session lasts 60 s.")]
        [SerializeField, Min(0f)] float transitionSeconds = DefaultTransitionSeconds;
        [SerializeField] LeafParticleController particles;

        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        LeafHealthMonitor monitor;

        public LeafHealthMonitor Monitor => monitor;
        public bool IsAttached => monitor != null && renderers.Count > 0;
        public float TransitionSeconds => transitionSeconds;

        internal void Bind(LeafParticleController particleController, float seconds)
        {
            particles = particleController;
            transitionSeconds = seconds;
        }

        /// <summary>Takes over the specimen's leaf renderers, showing <paramref name="state"/> at once.</summary>
        public void Attach(GameObject specimen, LeafRecoveryState state)
        {
            Detach();
            monitor = new LeafHealthMonitor(state);
            if (specimen == null)
                return;
            foreach (var r in specimen.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && r.sharedMaterial != null && r.sharedMaterial.HasProperty(SpotFadeId))
                    renderers.Add(r);
            if (particles != null)
                particles.Attach(specimen.GetComponentInChildren<LeafAnchors>());
            Apply();
        }

        /// <summary>Releases the leaf: the property block is cleared, so its material renders exactly as authored.</summary>
        public void Detach()
        {
            foreach (var r in renderers)
                if (r != null)
                    r.SetPropertyBlock(null);
            renderers.Clear();
            monitor = null;
            if (particles != null)
                particles.Detach();
        }

        /// <summary>Moves the leaf towards a validated treatment result (animated), or jumps there (restoring a session).</summary>
        public void SetTarget(LeafRecoveryState target, bool animate)
        {
            if (monitor == null)
                return;
            if (animate)
            {
                var improving = target.Health > monitor.Current.Health + 1e-4f;
                monitor.SetTarget(target, transitionSeconds);
                if (particles != null && improving)
                    particles.Burst();
            }
            else
            {
                monitor.Snap(target);
            }

            Apply();
        }

        void Update()
        {
            if (monitor == null)
                return;
            var moving = monitor.IsTransitioning;
            monitor.Advance(Time.unscaledDeltaTime);
            if (particles != null)
                particles.Tick(monitor.IsImproving, Time.unscaledDeltaTime);
            if (moving)
                Apply();
        }

        void Apply()
        {
            if (monitor == null)
                return;
            block ??= new MaterialPropertyBlock();
            var s = monitor.Current;
            var glow = monitor.IsImproving ? Mathf.Sin(monitor.Progress * Mathf.PI) : 0f;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(block);
                block.SetFloat(SpotFadeId, s.SpotFade);
                block.SetFloat(YellowFadeId, s.YellowFade);
                block.SetFloat(GreenGainId, s.GreenGain);
                block.SetFloat(VeinGainId, s.VeinGain);
                block.SetFloat(WorsenId, s.Worsen);
                block.SetFloat(TreatGlowId, glow);
                r.SetPropertyBlock(block);
            }
        }

        void OnDisable() => Detach();
    }
}
