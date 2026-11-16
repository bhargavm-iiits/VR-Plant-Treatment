using System;
using System.Collections.Generic;
using System.Linq;

namespace BTP.Treatment
{
    /// <summary>A crop and its diseases, as listed by the content database.</summary>
    public sealed class CropListing
    {
        public string PlantId;
        public string PlantName;
        public readonly List<CaseSummary> Diseases = new List<CaseSummary>();
    }

    /// <summary>
    /// Thin read layer for the manual treatment mode over the one content repository (<see cref="ITreatmentRepository"/>):
    /// crop → disease listing, and loading a case together with its validated manual profile. Missing or invalid data
    /// makes only that case unavailable; another disease is never substituted.
    /// </summary>
    public sealed class TreatmentDataManager
    {
        readonly ITreatmentRepository content;
        readonly Func<CaseKey, (TreatmentCase content, string error)> load;

        /// <param name="load">Optional cached loader (TreatmentServices.GetCase); defaults to the repository.</param>
        public TreatmentDataManager(ITreatmentRepository content, Func<CaseKey, (TreatmentCase, string)> load = null)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.load = load ?? (key =>
            {
                var c = content.GetCase(key, out var error);
                return (c, error);
            });
        }

        /// <summary>Crops in database order (plant name), each with its diseases.</summary>
        public List<CropListing> ListCrops()
        {
            var crops = new List<CropListing>();
            foreach (var summary in content.ListCases())
            {
                var crop = crops.FirstOrDefault(c => c.PlantId == summary.Key.PlantId);
                if (crop == null)
                    crops.Add(crop = new CropListing { PlantId = summary.Key.PlantId, PlantName = summary.PlantName });
                crop.Diseases.Add(summary);
            }

            return crops;
        }

        public IReadOnlyList<CaseSummary> DiseasesOf(string plantId) =>
            ListCrops().FirstOrDefault(c => c.PlantId == plantId)?.Diseases ?? new List<CaseSummary>();

        /// <summary>Loads a case for the manual mode. Returns null with the reasons when the case or its manual data is unusable.</summary>
        public TreatmentCase LoadManualCase(CaseKey key, out List<string> problems)
        {
            problems = new List<string>();
            if (!content.IsReady)
            {
                problems.Add(content.Error ?? "Treatment content is not ready.");
                return null;
            }

            var (c, error) = load(key);
            if (c == null)
            {
                problems.Add(error ?? $"No treatment record exists for {key}.");
                return null;
            }

            problems.AddRange(TreatmentCaseValidator.ValidateManual(c));
            return problems.Count == 0 ? c : null;
        }
    }
}
