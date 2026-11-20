using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The visible granules of one <see cref="GranuleSimulation"/> (a scoop's pour, a spill, the packet applicator): each
    /// simulated granule (the unit the guide counts and judges) is drawn as a small cluster of <see cref="PerGranule"/>
    /// real-size granules, tumbling with it while it falls and lying in a little scatter where it settled, sunk a third
    /// of their size into the soil.
    /// </summary>
    public sealed class GranulePour : IGranuleSource
    {
        public const int DefaultPerGranule = 7;
        /// <summary>Scatter radius of a settled cluster (m) and how far the drawn granules sink into the soil (share of size).</summary>
        const float Scatter = 0.011f, Sink = 0.4f;

        public GranuleSimulation Simulation { get; set; }
        public GranuleLook Look { get; set; }
        public int PerGranule { get; set; } = DefaultPerGranule;
        public uint Key { get; set; }
        public bool Visible { get; set; } = true;

        public GranulePour(GranuleSimulation simulation, GranuleLook look, uint key, int perGranule = DefaultPerGranule)
        {
            Simulation = simulation;
            Look = look;
            Key = key;
            PerGranule = Mathf.Max(1, perGranule);
        }

        public void CollectGranules(GranuleRenderer sink)
        {
            if (!Visible || Simulation == null || Look == null)
                return;
            var granules = Simulation.Granules;
            var soil = Simulation.Band.SoilY;
            for (var i = 0; i < granules.Count; i++)
            {
                var g = granules[i];
                if (!g.Released)
                    continue;
                var speed = g.Velocity.magnitude;
                var trail = speed > 1e-3f ? -g.Velocity / speed : Vector3.zero;
                for (var j = 0; j < PerGranule; j++)
                {
                    var key = GranuleMeshes.Hash(g.Id ^ Key ^ (uint)(j * 0x9E3779B1u + 0x51u));
                    Look.Sample(key, out var colour, out var variant, out var radius, out var rotation, out var seed);
                    var h = GranuleMeshes.Hash(key ^ 0xA511E9B3u);
                    var angle = (h & 0xFFFF) / 65535f * Mathf.PI * 2f;
                    var distance = Mathf.Sqrt(((h >> 16) & 0xFFFF) / 65535f);
                    var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                    Vector3 position;
                    float ground;
                    if (g.Settled)
                    {
                        position = new Vector3(g.Position.x + offset.x * Scatter, soil + radius * (1f - Sink * 2f), g.Position.z + offset.z * Scatter);
                        ground = soil;
                    }
                    else
                    {
                        // Airborne: a loose clump strung out along the fall (later members lag behind).
                        position = g.Position + offset * (Scatter * 0.45f) + trail * Mathf.Min(speed * 0.012f, 0.03f) * j / PerGranule;
                        position.y = Mathf.Max(position.y, soil + radius * (1f - Sink * 2f));
                        ground = soil;
                    }

                    sink.Add(position, g.Rotation * rotation, radius, colour, variant, Look.Speckle, seed, ground);
                }
            }
        }
    }
}
