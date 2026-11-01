using System;
using BTP.Plants;
using UnityEngine;

namespace BTP.Core
{
    /// <summary>
    /// The shared plant selection. Plain C# so it can be unit tested; SelectionState hosts it
    /// in XRBootstrap so it survives switching between the Farm and the Lab.
    /// </summary>
    public sealed class PlantSelection
    {
        readonly PlantCatalog catalog;

        public PlantSelection(PlantCatalog catalog)
        {
            this.catalog = catalog;
        }

        /// <summary>The selected plant, or null before anything is selected.</summary>
        public PlantDefinition Current { get; private set; }

        public event Action<PlantDefinition> Changed;

        /// <summary>Selects a plant by ID. Unknown IDs, including Grape and Peach, are rejected.</summary>
        public bool Select(string plantId)
        {
            if (catalog == null || !catalog.TryGet(plantId, out var plant))
            {
                Debug.LogError($"[BTP] Cannot select unknown plant '{plantId}'.");
                return false;
            }

            if (plant == Current)
                return true;

            Current = plant;
            Changed?.Invoke(plant);
            return true;
        }

        /// <summary>Selects Tomato if nothing is selected yet, then returns the selection.</summary>
        public PlantDefinition EnsureSelection()
        {
            if (Current == null)
                Select(PlantCatalog.DefaultPlantId);
            return Current;
        }
    }
}
