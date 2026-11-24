using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Sampled surface points (mesh space) of every Farm crop mesh a specimen can be made of, baked in the Editor
    /// (BTP.Editor.SpecimenSurfaceBaker, run by every Quest build): the crop meshes are imported without CPU read access, so
    /// a player cannot read their vertices, and the chamber specimen has no colliders. The sprayer wets the leaves at these
    /// points (<see cref="SpecimenGeometry.SurfacePoints"/>).
    /// </summary>
    public sealed class SpecimenSurfacePoints : ScriptableObject
    {
        public const string ResourcePath = "BTP/SpecimenSurfacePoints";
        /// <summary>Points kept per mesh at most (evenly strided over its vertices).</summary>
        public const int MaxPointsPerMesh = 900;

        [Serializable]
        public sealed class Entry
        {
            public Mesh mesh;
            public Vector3[] points;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        static SpecimenSurfacePoints loaded;
        static bool tried;
        readonly Dictionary<Mesh, Vector3[]> lookup = new Dictionary<Mesh, Vector3[]>();

        public IReadOnlyList<Entry> Entries => entries;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded = null;
            tried = false;
        }

        /// <summary>The baked asset, or null when it has not been baked.</summary>
        public static SpecimenSurfacePoints Load()
        {
            if (loaded != null || tried)
                return loaded;
            tried = true;
            loaded = Resources.Load<SpecimenSurfacePoints>(ResourcePath);
            return loaded;
        }

        internal void SetEntries(List<Entry> baked)
        {
            entries = baked ?? new List<Entry>();
            lookup.Clear();
        }

        public bool TryGet(Mesh mesh, out Vector3[] points)
        {
            points = null;
            if (mesh == null)
                return false;
            if (lookup.Count == 0)
                foreach (var entry in entries)
                    if (entry != null && entry.mesh != null && entry.points != null)
                        lookup[entry.mesh] = entry.points;
            return lookup.TryGetValue(mesh, out points);
        }
    }
}
