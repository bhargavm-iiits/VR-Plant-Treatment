using System.Collections.Generic;
using BTP.Core;
using BTP.Plants;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Lab
{
    /// <summary>
    /// Recovery Demo panel and week timeline. For Tomato it runs the illustrative six-week
    /// Early Blight demo: metrics, timeline and a diseased (Week 1) / current-week comparison
    /// on the pedestal plant. Every other plant shows a "Tomato demo only" state instead, so no
    /// disease history is invented for it.
    /// </summary>
    public sealed class RecoveryDemoPanel : MonoBehaviour
    {
        [SerializeField] TomatoRecoveryData data;
        [SerializeField] LabPlantDisplay display;

        [Header("Demo state (Tomato)")]
        [SerializeField] GameObject demoContent;
        [SerializeField] TMP_Text weekTitle;
        [SerializeField] TMP_Text weekCaption;
        [SerializeField] MetricBar leafHealthBar;
        [SerializeField] MetricBar chlorophyllBar;
        [SerializeField] MetricBar severityBar;

        [Header("Other plants")]
        [SerializeField] GameObject unavailableContent;
        [SerializeField] Button viewTomatoDemoButton;

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

        RecoveryTimeline timelineState;
        LeafDiseaseVisual visual;
        bool demoActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => sessionWeek = RecoveryTimeline.FirstWeek;

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

        internal void BindUnavailable(GameObject content, Button viewTomato)
        {
            unavailableContent = content;
            viewTomatoDemoButton = viewTomato;
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
            timelineState = new RecoveryTimeline(data);
            timelineState.SetWeek(sessionWeek);
            timelineState.WeekChanged += OnWeekChanged;

            previousButton.onClick.AddListener(timelineState.Previous);
            nextButton.onClick.AddListener(timelineState.Next);
            resetButton.onClick.AddListener(timelineState.Reset);
            for (var i = 0; i < weekButtons.Count; i++)
            {
                var week = i + 1;
                weekButtons[i].onClick.AddListener(() => timelineState.SetWeek(week));
            }

            viewTomatoDemoButton.onClick.AddListener(() =>
            {
                if (SelectionState.Instance != null)
                    SelectionState.Instance.SelectPlant(data.PlantId);
            });
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
            visual = null;
            if (demoActive)
            {
                visual = model.GetComponent<LeafDiseaseVisual>();
                if (visual == null)
                    visual = model.AddComponent<LeafDiseaseVisual>();
            }

            demoContent.SetActive(demoActive);
            timeline.SetActive(demoActive);
            comparisonMarker.SetActive(demoActive);
            unavailableContent.SetActive(!demoActive);
            Refresh();
        }

        void OnWeekChanged(int week)
        {
            sessionWeek = week;
            Refresh();
        }

        void Refresh()
        {
            if (!demoActive)
                return;

            var current = timelineState.Current;
            var baseline = data.GetWeek(RecoveryTimeline.FirstWeek);

            weekTitle.text = $"Week {current.week} of {timelineState.WeekCount}";
            weekCaption.text = string.IsNullOrEmpty(current.caption) ? string.Empty : current.caption;
            leafHealthBar.SetValue(current.leafHealth);
            chlorophyllBar.SetValue(current.chlorophyll);
            severityBar.SetValue(current.diseaseSeverity);

            previousButton.interactable = timelineState.CanGoBack;
            nextButton.interactable = timelineState.CanGoForward;
            for (var i = 0; i < weekSelectedVisuals.Count; i++)
                weekSelectedVisuals[i].Selected = i + 1 == current.week;

            baselineLabel.text = $"Diseased · Week {baseline.week}";
            currentLabel.text = $"Current · Week {current.week}";
            if (currentLabelBackground != null)
            {
                var health = Color.Lerp(UIPalette.HealthBad, UIPalette.HealthGood, current.leafHealth / 100f);
                currentLabelBackground.color = new Color(health.r * 0.45f, health.g * 0.45f, health.b * 0.45f, 0.9f);
            }

            if (visual != null)
            {
                var root = display.ModelRoot;
                visual.ShowComparison(current, baseline, root.right, root.position);
            }
        }
    }
}
