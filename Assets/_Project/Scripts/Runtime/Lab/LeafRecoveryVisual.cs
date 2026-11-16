using UnityEngine;

namespace BTP.Lab
{
    [DefaultExecutionOrder(110)]
    public sealed class LeafRecoveryVisual : MonoBehaviour
    {
        [Tooltip("Seconds for one week change (forward or back). The healing front sweeps across the leaf in this time.")]
        [SerializeField, Min(0f)] float transitionSeconds = 3f;
        [Tooltip("Seconds for Reset back to Week 1.")]
        [SerializeField, Min(0f)] float resetSeconds = 1.5f;
        [Tooltip("Peak strength of the soft green glow on the healing front (forward steps only). 0 disables it.")]
        [SerializeField, Range(0f, 1f)] float healingGlow = 1f;

        static readonly int RecoveryId = Shader.PropertyToID("_Recovery");
        static readonly int RecoveryFromId = Shader.PropertyToID("_RecoveryFrom");
        static readonly int BaselineRecoveryId = Shader.PropertyToID("_BaselineRecovery");
        static readonly int BaselineFromId = Shader.PropertyToID("_BaselineFrom");
        static readonly int TransitionId = Shader.PropertyToID("_TransitionParams");
        static readonly int SplitId = Shader.PropertyToID("_SplitEnabled");
        static readonly int PlaneId = Shader.PropertyToID("_SplitPlane");
        static readonly Vector3[] Corners = new Vector3[8];
        Renderer[] targets;
        MaterialPropertyBlock block;
        Transform planeRoot;
        bool initialized;
        float startRecovery;
        float startBaselineRecovery;
        float elapsed;
        float duration;
        float progress = 1f;
        public float DisplayedRecovery { get; private set; }
        public float TargetRecovery { get; private set; }
        public float DisplayedBaselineRecovery { get; private set; }
        public float TargetBaselineRecovery { get; private set; }
        public bool IsTransitioning => initialized && elapsed < duration;
        /// <summary>True while animating towards a healthier state (the glow is shown only then).</summary>
        public bool IsHealing => IsTransitioning && (TargetRecovery > startRecovery || TargetBaselineRecovery > startBaselineRecovery);
        /// <summary>Current glow strength sent to the shader; exactly 0 whenever the leaf is settled.</summary>
        public float TransitionGlow => IsHealing ? healingGlow : 0f;
        public float TransitionSeconds => transitionSeconds;
        public float ResetSeconds => resetSeconds;

        // Week 1 preserves the supplied diseased texture. Week 6 reaches an illustrative green.
        public static float RecoveryAmount(RecoveryWeek current, RecoveryWeek baseline, RecoveryWeek final)
            => Mathf.InverseLerp(baseline.leafHealth, final.leafHealth, current.leafHealth);

        /// <param name="quick">Use the shorter reset duration (Reset back to Week 1).</param>
        public void Show(RecoveryWeek current, RecoveryWeek baseline, RecoveryWeek final, Transform splitRoot, bool quick = false)
        {
            targets ??= GetComponentsInChildren<Renderer>(true);
            block ??= new MaterialPropertyBlock();
            planeRoot = splitRoot;
            float next = RecoveryAmount(current, baseline, final);
            float nextBaseline = current.week == final.week ? 1f : 0f;
            float seconds = quick ? resetSeconds : transitionSeconds;
            if (!initialized || seconds <= 0f)
            {
                initialized = true;
                DisplayedRecovery = TargetRecovery = startRecovery = next;
                DisplayedBaselineRecovery = TargetBaselineRecovery = startBaselineRecovery = nextBaseline;
                elapsed = duration = 0f;
                progress = 1f;
            }
            else if (!Mathf.Approximately(next, TargetRecovery) || !Mathf.Approximately(nextBaseline, TargetBaselineRecovery))
            {
                startRecovery = DisplayedRecovery;
                startBaselineRecovery = DisplayedBaselineRecovery;
                TargetRecovery = next;
                TargetBaselineRecovery = nextBaseline;
                elapsed = 0f;
                duration = seconds;
                progress = 0f;
            }
            Apply();
        }

        void Update() => Advance(Time.unscaledDeltaTime);

        // The viewer-aligned comparison must follow the moving mesh even between week changes.
        void LateUpdate()
        {
            if (initialized && planeRoot != null) Apply();
        }

        internal void Advance(float seconds)
        {
            if (!initialized || planeRoot == null || !IsTransitioning)
                return;
            elapsed = Mathf.Min(elapsed + Mathf.Max(0f, seconds), duration);
            float t = duration > 0f ? elapsed / duration : 1f;
            // SmootherStep: gentle start and settle, steady sweep through the middle.
            progress = t * t * t * (t * (t * 6f - 15f) + 10f);
            DisplayedRecovery = Mathf.Lerp(startRecovery, TargetRecovery, progress);
            DisplayedBaselineRecovery = Mathf.Lerp(startBaselineRecovery, TargetBaselineRecovery, progress);
            if (!IsTransitioning)
            {
                // Settled: collapse the sweep so static weeks render exactly as a uniform amount.
                startRecovery = DisplayedRecovery = TargetRecovery;
                startBaselineRecovery = DisplayedBaselineRecovery = TargetBaselineRecovery;
                progress = 1f;
            }
            Apply();
        }

        void Apply()
        {
            bool moving = IsTransitioning;
            float glow = TransitionGlow;
            foreach (var target in targets)
            {
                if (target == null) continue;
                var normal = target.transform.localToWorldMatrix.transpose.MultiplyVector(planeRoot.right).normalized;
                var point = target.transform.InverseTransformPoint(planeRoot.position);
                float offset = Vector3.Dot(normal, point);
                // Farthest mesh point from the divider (≈ midrib), so the front reaches the leaf edge at progress 1.
                var bounds = target.localBounds;
                float extent = 0.0001f;
                for (int i = 0; i < 8; i++)
                {
                    Corners[i] = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    extent = Mathf.Max(extent, Mathf.Abs(Vector3.Dot(normal, Corners[i]) - offset));
                }
                target.GetPropertyBlock(block);
                block.SetFloat(RecoveryFromId, moving ? startRecovery : TargetRecovery);
                block.SetFloat(RecoveryId, TargetRecovery);
                block.SetFloat(BaselineFromId, moving ? startBaselineRecovery : TargetBaselineRecovery);
                block.SetFloat(BaselineRecoveryId, TargetBaselineRecovery);
                block.SetVector(TransitionId, new Vector4(moving ? progress : 1f, glow, 1f / extent, 0f));
                block.SetFloat(SplitId, 1f);
                block.SetVector(PlaneId, new Vector4(normal.x, normal.y, normal.z, offset));
                target.SetPropertyBlock(block);
            }
        }
    }
}
