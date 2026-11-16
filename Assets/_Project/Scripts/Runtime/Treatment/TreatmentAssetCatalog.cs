using System;
using System.Collections.Generic;
using SQLite;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Resolves the asset keys stored in the treatment database ("env.greenhouse", "target.leaf",
    /// "leafmat.tomato.early_blight", "tool.shears", ...) into Unity prefabs and materials. It holds
    /// references only: every instruction, rule and outcome lives in the SQLite content.
    /// </summary>
    [CreateAssetMenu(menuName = "BTP/Treatment Asset Catalog", fileName = "TreatmentAssetCatalog")]
    public sealed class TreatmentAssetCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string key;
            public UnityEngine.Object asset;
        }

        [Tooltip("The bundled read-only content database (imported from treatment_content.sql).")]
        [SerializeField] SQLiteAsset content;
        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, UnityEngine.Object> lookup;

        public SQLiteAsset Content => content;
        public IReadOnlyList<Entry> Entries => entries;

        public UnityEngine.Object Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            if (lookup == null)
            {
                lookup = new Dictionary<string, UnityEngine.Object>();
                foreach (var entry in entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.key) && entry.asset != null)
                        lookup[entry.key] = entry.asset;
            }

            return lookup.TryGetValue(key, out var asset) ? asset : null;
        }

        public T Resolve<T>(string key) where T : UnityEngine.Object => Resolve(key) as T;

        public bool Has(string key) => Resolve(key) != null;

        void OnValidate() => lookup = null;

        internal void Set(SQLiteAsset database, IEnumerable<Entry> values)
        {
            content = database;
            entries = new List<Entry>(values);
            lookup = null;
        }
    }
}