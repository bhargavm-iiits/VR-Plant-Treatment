using System.Collections.Generic;
using UnityEngine;

namespace BTP.Plants
{
    /// <summary>The seven selectable plants, in selector order.</summary>
    [CreateAssetMenu(menuName = "BTP/Plant Catalog", fileName = "PlantCatalog")]
    public sealed class PlantCatalog : ScriptableObject
    {
        public const int ExpectedPlantCount = 7;
        public const string DefaultPlantId = "tomato";

        /// <summary>Requested species with no supplied model. They must never be selectable.</summary>
        public static readonly IReadOnlyList<string> MissingPlantIds = new[] { "grape", "peach" };

        [SerializeField] List<PlantDefinition> plants = new List<PlantDefinition>();

        public IReadOnlyList<PlantDefinition> Plants => plants;

        public bool TryGet(string plantId, out PlantDefinition plant)
        {
            foreach (var candidate in plants)
            {
                if (candidate != null && candidate.PlantId == plantId)
                {
                    plant = candidate;
                    return true;
                }
            }

            plant = null;
            return false;
        }

        /// <summary>Lists setup problems; an empty list means the catalog is ready.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (plants.Count != ExpectedPlantCount)
                problems.Add($"Catalog lists {plants.Count} plants; expected {ExpectedPlantCount}.");

            var seen = new HashSet<string>();
            foreach (var plant in plants)
            {
                if (plant == null)
                {
                    problems.Add("Catalog contains an empty entry.");
                    continue;
                }

                if (string.IsNullOrEmpty(plant.PlantId))
                {
                    problems.Add($"{plant.name} has no plant ID.");
                    continue;
                }

                if (!seen.Add(plant.PlantId))
                    problems.Add($"Duplicate plant ID '{plant.PlantId}'.");
                if (plant.FarmPrefab == null)
                    problems.Add($"{plant.DisplayName} has no farm model.");
                if (plant.LabPrefab == null)
                    problems.Add($"{plant.DisplayName} has no lab model.");
                foreach (var missingId in MissingPlantIds)
                {
                    if (plant.PlantId == missingId)
                        problems.Add($"{plant.DisplayName} has no supplied model and must not be selectable.");
                }

                if (plant.HasRecoveryDemo && plant.PlantId != DefaultPlantId)
                    problems.Add($"Only Tomato has the recovery demo, but {plant.DisplayName} is marked as having one.");
            }

            if (!seen.Contains(DefaultPlantId))
                problems.Add("Catalog has no Tomato, the default plant.");
            return problems;
        }

        internal void SetPlants(IEnumerable<PlantDefinition> definitions)
        {
            plants = new List<PlantDefinition>(definitions);
        }
    }
}
