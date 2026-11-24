using System.Collections.Generic;
using BTP.Core;
using BTP.Farm;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Lab
{
    /// <summary>
    /// Recovery Demo panel and week timeline for every plant. It runs the illustrative six-week
    /// prototype: the same fixed example metrics, the timeline and a diseased (Week 1) / current-week
    /// comparison on the pedestal plant. The disease named on the panel is only an example from the
    /// plant's PlantVillage classes (the user can cycle through them); it is not a diagnosis, and the
    /// values are not measurements or advice.
    /// </summary>
    public sealed class RecoveryDemoPanel : MonoBehaviour
    {
        [SerializeField] TomatoRecoveryData data;
        [SerializeField] LabPlantDisplay display;

        [Header("Demo state")]
        [SerializeField] GameObject demoContent;
        [SerializeField] TMP_Text weekTitle;
        [SerializeField] TMP_Text weekCaption;
        [SerializeField] MetricBar leafHealthBar;
        [SerializeField] MetricBar chlorophyllBar;
        [SerializeField] MetricBar severityBar;

        [Header("Example disease")]
        [SerializeField] DiseaseGalleryCatalog diseases;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] GameObject diseaseRow;
        [SerializeField] TMP_Text diseaseName;
        [SerializeField] RawImage diseaseLeaf;
        [SerializeField] Button previousDiseaseButton;
        [SerializeField] Button nextDiseaseButton;

        [Header("Timeline")]
        [SerializeField] GameObject timeline;
        [SerializeField] Button previousButton;
        [SerializeField] Button nextButton;
        [SerializeField] Button resetButton;
        [SerializeField] List<Button> weekButtons = new List<Button>();
        [SerializeField] List<SelectedStateVisual> weekSelectedVisuals = new List<SelectedStateVisual>();

        [Header("Comparison around the pedestal")]
        [SerializeField] GameObject comparisonMarker;
        [SerializeField] TMP_Text baselineLabel;
        [SerializeField] TMP_Text currentLabel;
        [SerializeField] Image currentLabelBackground;

        // The demo week survives leaving and re-entering the Lab; only Reset returns to Week 1.
        static int sessionWeek = RecoveryTimeline.FirstWeek;
        // The after-treatment story (RecoveryFlow) this panel last introduced; a newer one starts again at Week 1.
        static int introducedStory;

        public const string StoryIntroTitle = "Weekly changes";
        public const string StoryIntroBody = "Your treated leaf is on the stand. Step from Week 1 (as treated) to Week 6 with < and > on the timeline below it.";
        public const string SeeFarmLabel = "See changes in the Farm";

        RecoveryTimeline timelineState;
        LeafRecoveryVisual visual;
        PlantDefinition currentPlant;
        int diseaseIndex;
        bool demoActive;
        float metricRecovery = float.NaN;
        TMP_Text comparisonHint;
        bool quickTransition;
        bool showingTransitionState;
        bool farmPromptPending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sessionWeek = RecoveryTimeline.FirstWeek;
            introducedStory = 0;
        }

        /// <summary>The week shown on the timeline.</summary>
        public int CurrentWeek => timelineState != null ? timelineState.CurrentWeek : sessionWeek;

        /// <summary>Selects a week as the timeline buttons do (tests, verification).</summary>
        public void SelectWeek(int week) => timelineState?.SetWeek(week);

        internal void BindData(TomatoRecoveryData recoveryData, LabPlantDisplay plantDisplay)
        {
            data = recoveryData;
            display = plantDisplay;
        }

        internal void BindDemo(GameObject content, TMP_Text title, TMP_Text caption, MetricBar leaf, MetricBar chlorophyll, MetricBar severity)
        {
            demoContent = content;
            weekTitle = title;
            weekCaption = caption;
            leafHealthBar = leaf;
            chlorophyllBar = chlorophyll;
            severityBar = severity;
        }

        internal void BindDisease(DiseaseGalleryCatalog catalog, TMP_Text subtitleText, GameObject row, TMP_Text nameText,
            RawImage leafImage, Button previous, Button next)
        {
            diseases = catalog;
            subtitle = subtitleText;
            diseaseRow = row;
            diseaseName = nameText;
            diseaseLeaf = leafImage;
            previousDiseaseButton = previous;
            nextDiseaseButton = next;
        }

        internal void BindTimeline(GameObject root, Button previous, Button next, Button reset, List<Button> weeks, List<SelectedStateVisual> weekVisuals)
        {
            timeline = root;
            previousButton = previous;
            nextButton = next;
            resetButton = reset;
            weekButtons = weeks;
            weekSelectedVisuals = weekVisuals;
        }

        internal void BindComparison(GameObject marker, TMP_Text baseline, TMP_Text current, Image currentBackground)
        {
            comparisonMarker = marker;
            baselineLabel = baseline;
            currentLabel = current;
            currentLabelBackground = currentBackground;
        }

        void Awake()
        {
            foreach (var text in demoContent.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "Hint") comparisonHint = text;
            timelineState = new RecoveryTimeline(data);
            timelineState.SetWeek(sessionWeek);
            timelineState.WeekChanged += OnWeekChanged;

            previousButton.onClick.AddListener(timelineState.Previous);
            nextButton.onClick.AddListener(timelineState.Next);
            resetButton.onClick.AddListener(() =>
            {
                quickTransition = true;
                try { timelineState.Reset(); }
                finally { quickTransition = false; }
            });
            for (var i = 0; i < weekButtons.Count; i++)
            {
                var week = i + 1;
                weekButtons[i].onClick.AddListener(() => timelineState.SetWeek(week));
            }

            previousDiseaseButton.onClick.AddListener(() => StepDisease(-1));
            nextDiseaseButton.onClick.AddListener(() => StepDisease(1));
        }

        void OnEnable()
        {
            if (display == null)
                return;
            display.ModelChanged += OnModelChanged;
            if (display.CurrentPlant != null)
                OnModelChanged(display.CurrentPlant, display.CurrentModel);
        }

        void OnDisable()
        {
            if (display != null)
                display.ModelChanged -= OnModelChanged;
        }

        void OnModelChanged(PlantDefinition plant, GameObject model)
        {
            demoActive = plant != null && plant.HasRecoveryDemo && model != null;
            currentPlant = plant;
            diseaseIndex = 0;
            if (plant != null)
            {
                var entries = DiseaseClasses.For(plant.PlantId);
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i].DiseaseId == display.CurrentDiseaseId)
                        diseaseIndex = i;
            }
            visual = null;
            if (demoActive)
            {
                visual = model.GetComponent<LeafRecoveryVisual>();
                if (visual == null)
                    visual = model.AddComponent<LeafRecoveryVisual>();
            }

            demoContent.SetActive(demoActive);
            timeline.SetActive(demoActive);
            comparisonMarker.SetActive(demoActive);
            BeginStoryIfNew();
            Refresh();
        }

        /// <summary>True when the leaf on the stand is the case the after-treatment story is about.</summary>
        bool IsStoryCase() => demoActive && currentPlant != null && RecoveryFlow.IsFor(currentPlant.PlantId, display.CurrentDiseaseId);

        /// <summary>A newly treated case on the stand: back to Week 1 (the leaf as treated) and a short how-to pop-up.</summary>
        void BeginStoryIfNew()
        {
            if (!IsStoryCase() || introducedStory == RecoveryFlow.Generation)
                return;
            introducedStory = RecoveryFlow.Generation;
            if (RecoveryFlow.Stage != RecoveryFlowStage.LabPending)
                return;
            quickTransition = true;
            try { timelineState.SetWeek(RecoveryTimeline.FirstWeek); }
            finally { quickTransition = false; }
            Debug.Log($"[BTP] Recovery story: weekly changes of {RecoveryFlow.PlantId}/{RecoveryFlow.DiseaseId} in the Lab.");
            if (Application.isPlaying)
                FlowPopup.Show(gameObject.scene, StoryIntroTitle, StoryIntroBody, UIPalette.Cyan, new FlowPopup.Choice("OK", null));
        }

        /// <summary>Week 6 of the treated case (once the leaf has finished healing): offers the Farm.</summary>
        void ShowFarmPrompt()
        {
            var full = RecoveryFlow.FullyHealed;
            var title = full ? "Week 6: recovered" : "Week 6: partly recovered";
            if (RecoveryFlow.Specimen == null)
            {
                FlowPopup.Show(gameObject.scene, title, "Six weeks after the treatment the leaf has healed. (This crop is not planted in the Farm.)",
                    UIPalette.HealthGood, new FlowPopup.Choice("Close", null));
                return;
            }

            FlowPopup.Show(gameObject.scene, title,
                (full ? "Six weeks after the treatment the leaf has healed." : "Six weeks after the treatment the leaf has improved.") +
                " See the Farm plants with this disease recover.", UIPalette.HealthGood,
                new FlowPopup.Choice(SeeFarmLabel, SeeFarm), new FlowPopup.Choice("Close", null, false));
        }

        /// <summary>See changes in the Farm: the Farm loads with the user beside the treated plant, which is shown healed.</summary>
        public static void SeeFarm()
        {
            var environment = EnvironmentController.Instance;
            if (environment == null)
            {
                Debug.LogError("[BTP] See changes in the Farm: no EnvironmentController is loaded; start from XRBootstrap.");
                return;
            }

            RecoveredPlantHighlight.PrepareArrival(environment);
            environment.ReturnToFarm();
        }

        void StepDisease(int delta)
        {
            if (currentPlant == null || diseases == null)
                return;
            var entries = DiseaseClasses.For(currentPlant.PlantId);
            diseaseIndex = DiseaseClasses.Step(diseaseIndex, entries.Count, delta);
            if (entries.Count > 0)
                display.ShowDisease(entries[diseaseIndex].DiseaseId);
        }

        /// <summary>Names the example disease (from the PlantVillage classes) in the subtitle and shows its sample leaf.</summary>
        void RefreshDisease()
        {
            var entries = diseases != null && currentPlant != null ? diseases.GetDiseases(currentPlant.PlantId) : null;
            if (entries == null || entries.Count == 0)
            {
                subtitle.text = currentPlant != null ? $"{currentPlant.DisplayName} · illustrative demo" : string.Empty;
                diseaseRow.SetActive(false);
                return;
            }

            diseaseIndex = Mathf.Clamp(diseaseIndex, 0, entries.Count - 1);
            var entry = entries[diseaseIndex];
            subtitle.text = $"{currentPlant.DisplayName} · {entry.displayName}";
            diseaseName.text = string.IsNullOrEmpty(entry.note)
                ? entry.displayName
                : entry.displayName + "\n<size=60%><color=#8FD3E8>" + entry.note + "</color></size>";
            diseaseLeaf.texture = entry.leaf;
            diseaseRow.SetActive(true);
            var cycle = entries.Count > 1;
            previousDiseaseButton.gameObject.SetActive(cycle);
            nextDiseaseButton.gameObject.SetActive(cycle);
        }

        void OnWeekChanged(int week)
        {
            sessionWeek = week;
            // The treated case's last week unlocks the Farm (and offers it again whenever Week 6 is shown).
            if (currentPlant != null && demoActive &&
                (RecoveryFlow.WeekReached(currentPlant.PlantId, display.CurrentDiseaseId, week, timelineState.WeekCount) ||
                 (week == timelineState.WeekCount && IsStoryCase() && RecoveryFlow.FarmUnlocked)))
                farmPromptPending = true;
            Refresh();
        }

        void Update()
        {
            if (demoActive && visual != null)
            {
                UpdateAnimatedMetrics();
                UpdateTransitionState();
            }

            // The Farm prompt waits until the leaf has finished healing into Week 6.
            if (farmPromptPending && (visual == null || !visual.IsTransitioning))
            {
                farmPromptPending = false;
                if (Application.isPlaying && IsStoryCase() && timelineState.CurrentWeek == timelineState.WeekCount)
                    ShowFarmPrompt();
            }
        }

        // While the leaf animates, the right-hand label says so ("Healing… → Week N"), and at Week 6
        // the divider stays until the whole leaf has finished turning healthy.
        void UpdateTransitionState()
        {
            bool moving = visual.IsTransitioning;
            if (!moving && !showingTransitionState) return;
            var current = timelineState.Current;
            bool fullyHealthy = current.week == data.WeekCount;
            if (moving)
            {
                comparisonMarker.SetActive(true);
                currentLabel.text = visual.IsHealing ? $"Healing… → Week {current.week}" : $"Rewinding… → Week {current.week}";
                showingTransitionState = true;
                return;
            }
            showingTransitionState = false;
            comparisonMarker.SetActive(!fullyHealthy);
            currentLabel.text = CurrentLabel(current.week, fullyHealthy);
        }

        void UpdateAnimatedMetrics()
        {
            float amount = visual.DisplayedRecovery;
            if (Mathf.Approximately(metricRecovery, amount)) return;
            metricRecovery = amount;
            var first = data.GetWeek(1);
            var final = data.GetWeek(data.WeekCount);
            float health = Mathf.Lerp(first.leafHealth, final.leafHealth, amount);
            var low = first;
            var high = final;
            for (int i = 2; i <= data.WeekCount; i++)
            {
                high = data.GetWeek(i);
                if (health <= high.leafHealth) break;
                low = high;
            }
            float t = Mathf.InverseLerp(low.leafHealth, high.leafHealth, health);
            leafHealthBar.SetValue(Mathf.RoundToInt(health));
            chlorophyllBar.SetValue(Mathf.RoundToInt(Mathf.Lerp(low.chlorophyll, high.chlorophyll, t)));
            severityBar.SetValue(Mathf.RoundToInt(Mathf.Lerp(low.diseaseSeverity, high.diseaseSeverity, t)));
        }

        string CurrentLabel(int week, bool lastWeek) =>
            !lastWeek ? $"Current · Week {week}" : IsStoryCase() && !RecoveryFlow.FullyHealed ? $"Improved · Week {week}" : $"Healthy · Week {week}";

        /// <summary>The timeline caption for the treated case: Week 1 is the leaf as treated, then the weeks after the treatment.</summary>
        internal static string StoryCaption(int week, int weekCount)
        {
            if (week <= RecoveryTimeline.FirstWeek)
                return "As treated: your treatment was just applied";
            if (week >= weekCount)
                return RecoveryFlow.FullyHealed ? "Recovered after your treatment" : "Partly recovered after your treatment";
            var after = week - RecoveryTimeline.FirstWeek;
            return after == 1 ? "1 week after your treatment" : $"{after} weeks after your treatment";
        }

        void Refresh()
        {
            if (!demoActive)
                return;

            RefreshDisease();
            var current = timelineState.Current;
            var baseline = data.GetWeek(RecoveryTimeline.FirstWeek);
            bool fullyHealthy = current.week == data.WeekCount;
            comparisonMarker.SetActive(!fullyHealthy);

            weekTitle.text = $"Week {current.week} of {timelineState.WeekCount}";
            weekCaption.text = IsStoryCase() ? StoryCaption(current.week, timelineState.WeekCount)
                : fullyHealthy ? "Complete recovery (illustrative)" : string.IsNullOrEmpty(current.caption) ? string.Empty : current.caption;
            if (comparisonHint != null)
                comparisonHint.text = fullyHealthy ? "Week 6: the whole leaf appears healthy." : "Left: supplied diseased leaf.\nRight: selected week (illustrative).";
            leafHealthBar.SetValue(current.leafHealth);
            chlorophyllBar.SetValue(current.chlorophyll);
            severityBar.SetValue(current.diseaseSeverity);

            previousButton.interactable = timelineState.CanGoBack;
            nextButton.interactable = timelineState.CanGoForward;
            for (var i = 0; i < weekSelectedVisuals.Count; i++)
                weekSelectedVisuals[i].Selected = i + 1 == current.week;

            baselineLabel.text = $"Diseased · Week {baseline.week}";
            currentLabel.text = CurrentLabel(current.week, fullyHealthy);
            if (currentLabelBackground != null)
            {
                var health = Color.Lerp(UIPalette.HealthBad, UIPalette.HealthGood, current.leafHealth / 100f);
                currentLabelBackground.color = new Color(health.r * 0.45f, health.g * 0.45f, health.b * 0.45f, 0.9f);
            }

            if (visual != null)
            {
                var root = display.ModelRoot;
                visual.Show(current, baseline, data.GetWeek(data.WeekCount), root, quickTransition,
                    currentPlant != null ? RecoveryFlow.LeafHealing(currentPlant.PlantId, display.CurrentDiseaseId) : 1f);
                metricRecovery = float.NaN;
                UpdateAnimatedMetrics();
                showingTransitionState = visual.IsTransitioning;
                UpdateTransitionState();
            }
        }
    }
}
