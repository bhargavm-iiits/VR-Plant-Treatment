using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTP.Plants
{
    [CreateAssetMenu(menuName = "BTP/Leaf Catalog")]
    public sealed class LeafCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string plantId;
            public string diseaseId;
            public GameObject prefab;
        }
        [SerializeField] List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;
        public bool TryGet(string plantId, string diseaseId, out GameObject prefab)
        {
            foreach (var entry in entries)
                if (entry.plantId == plantId && entry.diseaseId == diseaseId)
                {
                    prefab = entry.prefab;
                    return prefab != null;
                }
            prefab = null;
            return false;
        }
        internal void SetEntries(IEnumerable<Entry> values) => entries = new List<Entry>(values);
    }
}
