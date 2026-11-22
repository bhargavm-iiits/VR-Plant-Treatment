using System;
using BTP.Plants;
using UnityEngine;

namespace BTP.Core
{
    /// <summary>
    /// Scene-facing host of the shared plant and disease selection. Lives in XRBootstrap, so the
    /// Farm, visiting Lab and Treatment lab always agree on the selected teaching case.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SelectionState : MonoBehaviour
    {
        [SerializeField] PlantCatalog catalog;

        PlantSelection selection;

        public static SelectionState Instance { get; private set; }

        public PlantCatalog Catalog => catalog;

        /// <summary>The selected plant, or null before anything is selected.</summary>
        public PlantDefinition Current => selection?.Current;

        /// <summary>The selected disease of the current plant, or null before a plant is selected.</summary>
        public string CurrentDiseaseId { get; private set; }

        /// <summary>Raised after the selected plant changes (the disease has already been reset to the plant's default).</summary>
        public event Action<PlantDefinition> PlantChanged;

        /// <summary>Raised after the selected plant or disease changes: (plantId, diseaseId).</summary>
        public event Action<string, string> CaseChanged;

        /// <summary>
        /// The exact Farm plant the player selected for the current case, or null when the case was chosen without one
        /// (a Lab or Treatment selector, or a fresh start). Cleared whenever the case changes to a different one.
        /// </summary>
        public SpecimenRef SelectedSpecimen { get; private set; }

        /// <summary>Raised after <see cref="SelectedSpecimen"/> changes (null when it was cleared).</summary>
        public event Action<SpecimenRef> SpecimenChanged;

        /// <summary>
        /// While a connected XR session is being treated its case is locked: choosing another plant or disease is refused
        /// (with <see cref="SelectionRefused"/>) until the session is released. Offline use never locks.
        /// </summary>
        public bool IsLocked => lockedPlant != null;

        /// <summary>Why the selection is locked (shown when a change is refused), or null.</summary>
        public string LockReason { get; private set; }

        /// <summary>Raised with <see cref="LockReason"/> when a selection change is refused by the lock.</summary>
        public event Action<string> SelectionRefused;

        string lockedPlant;
        string lockedDisease;

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
            selection.Changed += OnPlantChanged;

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

        void OnPlantChanged(PlantDefinition plant)
        {
            CurrentDiseaseId = plant != null ? DiseaseClasses.DefaultDiseaseId(plant.PlantId) : null;
            ClearSpecimenUnless(plant?.PlantId, CurrentDiseaseId);
            PlantChanged?.Invoke(plant);
            CaseChanged?.Invoke(plant?.PlantId, CurrentDiseaseId);
        }

        public void SelectPlant(string plantId)
        {
            if (IsLocked && plantId != lockedPlant)
            {
                Refuse();
                return;
            }

            selection.Select(plantId);
            // Re-selecting the locked plant must not leave a different disease selected.
            if (IsLocked && CurrentDiseaseId != lockedDisease)
                SelectDisease(lockedDisease);
        }

        /// <summary>Whether the lock allows selecting this case (always true when unlocked).</summary>
        public bool CanSelect(string plantId, string diseaseId) => !IsLocked || (plantId == lockedPlant && diseaseId == lockedDisease);

        /// <summary>Selects a case and locks the selection to it. False when the case is unknown.</summary>
        public bool LockCase(string plantId, string diseaseId, string reason)
        {
            UnlockCase();
            if (!SelectCase(plantId, diseaseId))
                return false;
            lockedPlant = plantId;
            lockedDisease = diseaseId;
            LockReason = reason;
            return true;
        }

        public void UnlockCase()
        {
            lockedPlant = null;
            lockedDisease = null;
            LockReason = null;
        }

        void Refuse()
        {
            Debug.Log("[BTP] Selection change refused: " + LockReason);
            SelectionRefused?.Invoke(LockReason);
        }

        /// <summary>Selects one of the current plant's diseases. Unknown diseases for this plant are rejected.</summary>
        public bool SelectDisease(string diseaseId)
        {
            var plant = Current;
            if (plant == null || !IsDiseaseOf(plant.PlantId, diseaseId))
                return false;
            if (!CanSelect(plant.PlantId, diseaseId))
            {
                Refuse();
                return false;
            }
            if (diseaseId == CurrentDiseaseId)
                return true;
            CurrentDiseaseId = diseaseId;
            ClearSpecimenUnless(plant.PlantId, diseaseId);
            CaseChanged?.Invoke(plant.PlantId, diseaseId);
            return true;
        }

        /// <summary>
        /// Selects one Farm plant: its case becomes the selection (respecting the lock) and the specimen is remembered so
        /// the Treatment lab shows that exact plant. False when the case cannot be selected.
        /// </summary>
        public bool SelectSpecimen(SpecimenRef specimen)
        {
            if (specimen == null)
                return false;
            var selected = string.IsNullOrEmpty(specimen.DiseaseId) || !IsDiseaseOf(specimen.PlantId, specimen.DiseaseId)
                ? SelectPlantChecked(specimen.PlantId)
                : SelectCase(specimen.PlantId, specimen.DiseaseId);
            if (!selected)
                return false;
            // A plant specimen (no row disease) carries the disease that is now selected for its plant.
            if (string.IsNullOrEmpty(specimen.DiseaseId) || specimen.DiseaseId != CurrentDiseaseId)
                specimen = new SpecimenRef(specimen.SpecimenId, specimen.PlantId, CurrentDiseaseId, specimen.PlantIndex);
            if (specimen.Equals(SelectedSpecimen))
                return true;
            SelectedSpecimen = specimen;
            SpecimenChanged?.Invoke(specimen);
            return true;
        }

        bool SelectPlantChecked(string plantId)
        {
            SelectPlant(plantId);
            return Current != null && Current.PlantId == plantId;
        }

        void ClearSpecimenUnless(string plantId, string diseaseId)
        {
            if (SelectedSpecimen == null || SelectedSpecimen.IsCase(plantId, diseaseId))
                return;
            SelectedSpecimen = null;
            SpecimenChanged?.Invoke(null);
        }

        /// <summary>Selects a plant and one of its diseases together.</summary>
        public bool SelectCase(string plantId, string diseaseId)
        {
            if (!IsDiseaseOf(plantId, diseaseId))
                return false;
            if (!CanSelect(plantId, diseaseId))
            {
                Refuse();
                return false;
            }

            if (!selection.Select(plantId))
                return false;
            return SelectDisease(diseaseId);
        }

        /// <summary>Selects Tomato if nothing is selected yet, then returns the selection.</summary>
        public PlantDefinition EnsureSelection() => selection.EnsureSelection();

        static bool IsDiseaseOf(string plantId, string diseaseId)
        {
            foreach (var info in DiseaseClasses.For(plantId))
                if (info.DiseaseId == diseaseId)
                    return true;
            return false;
        }
    }
}