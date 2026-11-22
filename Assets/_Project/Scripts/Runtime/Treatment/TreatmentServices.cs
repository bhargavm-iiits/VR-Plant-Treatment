using System;
using System.Collections.Generic;
using System.IO;
using BTP.Core;
using BTP.Plants;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// Owns the treatment content database (read-only, bundled) and the learner progress database
    /// (writable, persistentDataPath). Lives in XRBootstrap so the Farm, visiting Lab and
    /// Treatment lab share one view of the selected case and its saved result. It also restores
    /// the last selected case when the application starts.
    /// </summary>
    [DefaultExecutionOrder(-95)]
    public sealed class TreatmentServices : MonoBehaviour
    {
        public const string ProgressFileName = "treatment_progress.db";
        const string LastCaseSetting = "last_case";

        [SerializeField] TreatmentAssetCatalog catalog;

        readonly Dictionary<CaseKey, TreatmentCase> cache = new Dictionary<CaseKey, TreatmentCase>();
        readonly Dictionary<CaseKey, TreatmentCase> overrides = new Dictionary<CaseKey, TreatmentCase>();

        public static TreatmentServices Instance { get; private set; }

        public TreatmentAssetCatalog Catalog => catalog;
        public ITreatmentRepository Content { get; private set; }
        public IProgressRepository Progress { get; private set; }

        /// <summary>Raised after a case's progress is saved or reset.</summary>
        public event Action<CaseKey> ProgressChanged;

        /// <summary>Receives finished manual assessments of a connected XR session's case; null offline.</summary>
        public ITreatmentResultSink ResultSink { get; set; }

        /// <summary>
        /// Serves <paramref name="content"/> for <paramref name="key"/> instead of the bundled record (a connected session's
        /// pinned recipe, validated by the XR client). Cleared with <see cref="ClearCaseOverride"/>.
        /// </summary>
        public void SetCaseOverride(CaseKey key, TreatmentCase content)
        {
            if (content == null)
            {
                ClearCaseOverride(key);
                return;
            }

            overrides[key] = content;
        }

        public void ClearCaseOverride(CaseKey key) => overrides.Remove(key);

        public bool HasCaseOverride(CaseKey key) => overrides.ContainsKey(key);

        /// <summary>The bundled record, ignoring any override (the XR client validates recipes against it).</summary>
        public TreatmentCase GetBundledCase(CaseKey key, out string error)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                error = null;
                return cached;
            }

            var loaded = Content.GetCase(key, out error);
            if (loaded != null)
                cache[key] = loaded;
            return loaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        internal void Bind(TreatmentAssetCatalog assetCatalog) => catalog = assetCatalog;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            Content = new SqliteTreatmentRepository(() => catalog != null && catalog.Content != null ? catalog.Content.CreateConnection() : null);
            Content.InitializeAsync();
            if (!Content.IsReady)
                Debug.LogError("[BTP] " + Content.Error, this);

            Progress = new SqliteProgressRepository(Path.Combine(Application.persistentDataPath, ProgressFileName));
            if (!Progress.IsReady)
                Debug.LogError("[BTP] " + Progress.Error, this);

            RestoreSelection();
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged += RememberSelection;

            // Carries the selected Farm plant into the treatment chamber (snapshot on Farm exit).
            if (GetComponent<FarmSpecimenSnapshot>() == null)
                gameObject.AddComponent<FarmSpecimenSnapshot>();
        }

        void OnDestroy()
        {
            if (Instance != this)
                return;
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged -= RememberSelection;
            Content?.Dispose();
            Progress?.Dispose();
            Instance = null;
        }

        void RestoreSelection()
        {
            var state = SelectionState.Instance;
            if (state == null || state.Current != null)
                return;
            if (CaseKey.TryParse(Progress.GetSetting(LastCaseSetting), out var key))
                state.SelectCase(key.PlantId, key.DiseaseId);
        }

        void RememberSelection(string plantId, string diseaseId)
        {
            if (!string.IsNullOrEmpty(plantId) && !string.IsNullOrEmpty(diseaseId))
                Progress.SetSetting(LastCaseSetting, new CaseKey(plantId, diseaseId).ToString());
        }

        /// <summary>The shared selected case; Tomato / Early blight for a fresh session.</summary>
        public CaseKey SelectedCase()
        {
            var state = SelectionState.Instance;
            if (state == null)
                return new CaseKey(PlantCatalog.DefaultPlantId, DiseaseClasses.DefaultDiseaseId(PlantCatalog.DefaultPlantId));
            var plant = state.EnsureSelection();
            var disease = state.CurrentDiseaseId ?? DiseaseClasses.DefaultDiseaseId(plant?.PlantId);
            return new CaseKey(plant != null ? plant.PlantId : PlantCatalog.DefaultPlantId, disease);
        }

        /// <summary>Loads (and caches) one case's content; null with <paramref name="error"/> when unavailable.</summary>
        public TreatmentCase GetCase(CaseKey key, out string error)
        {
            if (overrides.TryGetValue(key, out var pinned))
            {
                error = null;
                return pinned;
            }

            return GetBundledCase(key, out error);
        }

        /// <summary>Reopens the content database and forgets cached cases (used by Retry).</summary>
        public void ReloadContent()
        {
            cache.Clear();
            Content.InitializeAsync();
            if (!Content.IsReady)
                Debug.LogError("[BTP] " + Content.Error, this);
        }

        public void NotifyProgressChanged(CaseKey key) => ProgressChanged?.Invoke(key);

        /// <summary>Plain-language saved result of a case, e.g. "Disease managed (lesson complete)".</summary>
        public string DescribeProgress(CaseKey key)
        {
            var content = GetCase(key, out _);
            if (content == null)
                return "Treatment lesson unavailable";
            var progress = Progress.Load(key, content.ContentVersion);
            if (progress.Completed)
                return OutcomeLabel(progress.Outcome) + " (lesson complete)";
            if (!progress.HasStarted)
                return "Treatment not started";
            var session = new TreatmentSession(content, progress);
            return $"Treatment in progress: {session.CompletedSteps} of {session.TotalSteps} tasks";
        }

        public static string OutcomeLabel(TreatmentOutcome outcome)
        {
            switch (outcome)
            {
                case TreatmentOutcome.Managed: return "Disease managed";
                case TreatmentOutcome.PestSuppressed: return "Pest suppressed";
                case TreatmentOutcome.Replaced: return "Healthy replacement established";
                default: return "Not finished";
            }
        }
    }
}