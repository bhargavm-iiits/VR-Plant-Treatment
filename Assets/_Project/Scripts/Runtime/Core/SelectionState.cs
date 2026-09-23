using System;
using BTP.Plants;
using UnityEngine;

namespace BTP.Core
{
    /// <summary>Scene-facing host of the shared plant selection. Lives in XRBootstrap.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SelectionState : MonoBehaviour
    {
        [SerializeField] PlantCatalog catalog;

        PlantSelection selection;

        public static SelectionState Instance { get; private set; }

        public PlantCatalog Catalog => catalog;

        /// <summary>The selected plant, or null before anything is selected.</summary>
        public PlantDefinition Current => selection?.Current;

        /// <summary>Raised after the selected plant changes.</summary>
        public event Action<PlantDefinition> PlantChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        internal void Bind(PlantCatalog plantCatalog) => catalog = plantCatalog;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[BTP] A second SelectionState was created; only XRBootstrap should contain one.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            selection = new PlantSelection(catalog);
            selection.Changed += plant => PlantChanged?.Invoke(plant);

            if (catalog == null)
            {
                Debug.LogError("[BTP] SelectionState has no PlantCatalog assigned.", this);
                return;
            }

            foreach (var problem in catalog.Validate())
                Debug.LogError($"[BTP] Plant catalog setup error: {problem}", catalog);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void SelectPlant(string plantId) => selection.Select(plantId);

        /// <summary>Selects Tomato if nothing is selected yet, then returns the selection.</summary>
        public PlantDefinition EnsureSelection() => selection.EnsureSelection();
    }
}
