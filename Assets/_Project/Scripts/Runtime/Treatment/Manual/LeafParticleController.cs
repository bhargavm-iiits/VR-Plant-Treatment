using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Small green motes that travel along the leaf's veins while it recovers from a well-chosen treatment
    /// (vein segments from <see cref="LeafAnchors.VeinSegments"/>). One world-space particle system with manual
    /// emission and a hard particle cap keeps it cheap on Quest. Purely illustrative.
    /// </summary>
    public sealed class LeafParticleController : MonoBehaviour
    {
        [SerializeField] ParticleSystem system;
        [SerializeField, Min(0f)] float ratePerSecond = 28f;
        [SerializeField, Min(0f)] float speed = 0.06f;
        [SerializeField, Min(0)] int burstCount = 24;

        LeafAnchors anchors;
        List<(LeafAnchors.Point start, LeafAnchors.Point end)> segments;
        float pending;
        uint seed = 1;

        internal void Bind(ParticleSystem particleSystem) => system = particleSystem;

        public void Attach(LeafAnchors leafAnchors)
        {
            anchors = leafAnchors;
            segments = leafAnchors != null ? leafAnchors.VeinSegments() : null;
            if (system != null)
                system.Clear();
        }

        public void Detach()
        {
            anchors = null;
            segments = null;
            if (system != null)
                system.Clear();
        }

        public void Burst() => Emit(burstCount);

        /// <summary>Emits along the veins while <paramref name="active"/> (the leaf is recovering).</summary>
        public void Tick(bool active, float seconds)
        {
            if (!active)
            {
                pending = 0f;
                return;
            }

            pending += ratePerSecond * seconds;
            var count = (int)pending;
            pending -= count;
            Emit(count);
        }

        void Emit(int count)
        {
            if (system == null || anchors == null || segments == null || segments.Count == 0 || count <= 0)
                return;
            var t = anchors.transform;
            for (var i = 0; i < count; i++)
            {
                var (start, end) = segments[(int)(Next() * segments.Count) % segments.Count];
                var u = Next();
                var local = Vector3.Lerp(start.position, end.position, u);
                var normal = t.TransformDirection(Vector3.Lerp(start.normal, end.normal, u)).normalized;
                var direction = t.TransformVector(end.position - start.position);
                var emit = new ParticleSystem.EmitParams
                {
                    position = t.TransformPoint(local) + normal * 0.006f,
                    velocity = (direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up) * speed,
                    applyShapeToPosition = false,
                };
                system.Emit(emit, 1);
            }
        }

        // Small deterministic generator (xorshift), so no garbage and no shared UnityEngine.Random state.
        float Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }
    }
}
