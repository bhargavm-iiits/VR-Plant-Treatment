using System.Collections.Generic;
using UnityEngine;

namespace BTP.Plants
{
    /// <summary>
    /// The selectable plants, in selector order. <see cref="Plants"/> is the nine PlantVillage plants (Farm and
    /// Lab models; the only ones connected CropScan XR knows). <see cref="LabOnlyPlants"/> is the ten field and
    /// tropical crops added after them: a labelled leaf specimen as their Lab preview and, since the Farm was
    /// extended, a whole-plant Farm model too (FarmCropBuilder). The list keeps its name from when they had none.
    /// <see cref="AllPlants"/> is both (19), the order the Lab and Treatment selectors show and the crops the
    /// Farm rows cover.
    /// </summary>
    [CreateAssetMenu(menuName = "BTP/Plant Catalog", fileName = "PlantCatalog")]
    public sealed class PlantCatalog : ScriptableObject
    {
        public const int ExpectedPlantCount = 9;
        public const int ExpectedLabOnlyCount = 10;
        public const string DefaultPlantId = "tomato";

        [SerializeField] List<PlantDefinition> plants = new List<PlantDefinition>();
        [Tooltip("The ten field and tropical crops: leaf specimens in the Lab and Treatment, whole plants in the Farm; never listed by CropScan XR.")]
        [SerializeField] List<PlantDefinition> labOnlyPlants = new List<PlantDefinition>();

        /// <summary>The nine PlantVillage plants.</summary>
        public IReadOnlyList<PlantDefinition> Plants => plants;

        /// <summary>The ten field and tropical crops (named Lab-only from when they had no Farm model).</summary>
        public IReadOnlyList<PlantDefinition> LabOnlyPlants => labOnlyPlants;

        /// <summary>Farm plants then Lab-only crops: the Lab and Treatment selector order.</summary>
        public IReadOnlyList<PlantDefinition> AllPlants
        {
            get
            {
                var all = new List<PlantDefinition>(plants);
                all.AddRange(labOnlyPlants);
                return all;
            }
        }

        public bool TryGet(string plantId, out PlantDefinition plant)
        {
            foreach (var list in new[] { plants, labOnlyPlants })
            foreach (var candidate in list)
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
            // A Farm-only catalog (no Lab-only list) is still valid; once listed, all ten Lab-only crops are required.
            if (labOnlyPlants.Count != 0 && labOnlyPlants.Count != ExpectedLabOnlyCount)
                problems.Add($"Catalog lists {labOnlyPlants.Count} Lab-only crops; expected {ExpectedLabOnlyCount}.");

            var seen = new HashSet<string>();
            foreach (var plant in plants)
            {
                if (!CheckIdentity(plant, seen, problems))
                    continue;
                if (plant.FarmPrefab == null)
                    problems.Add($"{plant.DisplayName} has no farm model.");
                if (plant.LabPrefab == null)
                    problems.Add($"{plant.DisplayName} has no lab model.");
                if (!plant.HasRecoveryDemo)
                    problems.Add($"{plant.DisplayName} has no recovery demo; every plant has the illustrative demo.");
            }

            foreach (var plant in labOnlyPlants)
            {
                if (!CheckIdentity(plant, seen, problems))
                    continue;
                if (!DiseaseClasses.IsLabOnlyPlant(plant.PlantId))
                    problems.Add($"{plant.DisplayName} is listed as Lab-only but is not one of the Lab-only crops.");
                // Their Farm model comes from a later step (BTP/Setup/27 - Build Farm Crops), so it is not required here.
                if (plant.LabPrefab == null)
                    problems.Add($"{plant.DisplayName} has no leaf preview.");
                if (!plant.HasRecoveryDemo)
                    problems.Add($"{plant.DisplayName} has no recovery demo; every plant has the illustrative demo.");
            }

            if (!seen.Contains(DefaultPlantId))
                problems.Add("Catalog has no Tomato, the default plant.");
            return problems;
        }

        static bool CheckIdentity(PlantDefinition plant, HashSet<string> seen, List<string> problems)
        {
            if (plant == null)
            {
                problems.Add("Catalog contains an empty entry.");
                return false;
            }

            if (string.IsNullOrEmpty(plant.PlantId))
            {
                problems.Add($"{plant.name} has no plant ID.");
                return false;
            }

            if (!seen.Add(plant.PlantId))
                problems.Add($"Duplicate plant ID '{plant.PlantId}'.");
            return true;
        }

        internal void SetPlants(IEnumerable<PlantDefinition> definitions)
        {
            plants = new List<PlantDefinition>(definitions);
        }

        internal void SetLabOnlyPlants(IEnumerable<PlantDefinition> definitions)
        {
            labOnlyPlants = new List<PlantDefinition>(definitions);
        }
    }
}
