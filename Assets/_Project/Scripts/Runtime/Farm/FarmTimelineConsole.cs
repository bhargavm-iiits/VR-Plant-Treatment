using System.Collections.Generic;
using BTP.Core;
using BTP.Lab;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Farm
{
    /// <summary>
    /// The Farm six-week timeline console. It opens with a plant's disease ring, stands between the plant and the
    /// viewer below the cards, tilted up and turned to face the viewer, and sets that crop's week
    /// (<see cref="FarmRecoveryState"/>): &lt; Week 1..6 &gt; and Reset, the same controls as the Lab bar.
    /// The values are the shared illustrative example table, not measurements or advice.
    /// </summary>
    public sealed class FarmTimelineConsole : MonoBehaviour
    {
        [SerializeField] TomatoRecoveryData data;
        [SerializeField] GameObject root;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text status;
        [SerializeField] Button previousButton;
        [SerializeField] Button nextButton;
        [SerializeField] Button resetButton;
        [SerializeField] List<Button> weekButtons = new List<Button>();
        [SerializeField] List<SelectedStateVisual> weekVisuals = new List<SelectedStateVisual>();
        [SerializeField] MetricBar leafBar;
        [SerializeField] MetricBar chlorophyllBar;
        [SerializeField] MetricBar severityBar;
        [Tooltip("Height of the console centre above the plant's base, in metres (below the card ring).")]
        [SerializeField] float heightAbovePlant = 0.5f;
        [SerializeField] float tiltDegrees = 45f;

        string plantId;
        Transform plant;
        Camera viewer;

        public bool IsVisible => root != null && root.activeSelf;
        public string PlantId => IsVisible ? plantId : null;
        public int WeekCount => data != null ? data.WeekCount : FarmRecoveryState.DefaultWeekCount;

        internal void Bind(TomatoRecoveryData recoveryData, GameObject consoleRoot, TMP_Text titleText, TMP_Text statusText,
            Button previous, Button next, Button reset, List<Button> weeks, List<SelectedStateVisual> visuals,
            MetricBar leaf, MetricBar chlorophyll, MetricBar severity)
        {
            data = recoveryData;
            root = consoleRoot;
            title = titleText;
            status = statusText;
            previousButton = previous;
            nextButton = next;
            resetButton = reset;
            weekButtons = weeks;
            weekVisuals = visuals;
            leafBar = leaf;
            chlorophyllBar = chlorophyll;
            severityBar = severity;
        }

        void Awake()
        {
            previousButton.onClick.AddListener(() => FarmRecoveryState.Step(plantId, -1, WeekCount));
            nextButton.onClick.AddListener(() => FarmRecoveryState.Step(plantId, 1, WeekCount));
            resetButton.onClick.AddListener(() => FarmRecoveryState.Reset(plantId));
            for (var i = 0; i < weekButtons.Count; i++)
            {
                var week = i + 1;
                weekButtons[i].onClick.AddListener(() => FarmRecoveryState.SetWeek(plantId, week, WeekCount));
            }

            root.SetActive(false);
        }

        void OnEnable() => FarmRecoveryState.WeekChanged += OnWeekChanged;

        void OnDisable() => FarmRecoveryState.WeekChanged -= OnWeekChanged;

        /// <summary>Opens the console for one specimen, placed for the current viewer position.</summary>
        public void Show(string id, Transform specimen, float ringRadius)
        {
            plantId = id;
            plant = specimen;
            var eye = Viewer();
            var eyePosition = eye != null ? eye.transform.position : specimen.position + Vector3.back * 2.5f;
            root.transform.position = FarmRecoveryMath.ConsolePosition(specimen.position, eyePosition, ringRadius,
                specimen.position.y + heightAbovePlant);
            root.SetActive(true);
            Face();
            Refresh();
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
            plant = null;
        }

        /// <summary>True when <paramref name="target"/> belongs to the console UI.</summary>
        public bool Contains(Transform target) => target != null && root != null && target.IsChildOf(root.transform);

        void LateUpdate()
        {
            if (IsVisible)
                Face();
        }

        void Face()
        {
            var eye = Viewer();
            if (eye == null)
                return;
            var away = root.transform.position - eye.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f)
                root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up) * Quaternion.Euler(tiltDegrees, 0f, 0f);
        }

        void OnWeekChanged(string id, int week)
        {
            if (IsVisible && id == plantId)
                Refresh();
        }

        void Refresh()
        {
            var weekNumber = FarmRecoveryState.GetWeek(plantId);
            var week = data.GetWeek(weekNumber);
            var name = plantId;
            if (SelectionState.Instance != null && SelectionState.Instance.Catalog != null &&
                SelectionState.Instance.Catalog.TryGet(plantId, out var definition))
                name = definition.DisplayName;

            title.text = $"{name} · six-week timeline";
            status.text = FarmRecoveryMath.StatusLine(week, WeekCount);
            leafBar.SetValue(week.leafHealth);
            chlorophyllBar.SetValue(week.chlorophyll);
            severityBar.SetValue(week.diseaseSeverity);
            previousButton.interactable = weekNumber > FarmRecoveryState.FirstWeek;
            nextButton.interactable = weekNumber < WeekCount;
            for (var i = 0; i < weekVisuals.Count; i++)
                weekVisuals[i].Selected = i + 1 == weekNumber;
        }

        Camera Viewer()
        {
            if (viewer == null || !viewer.isActiveAndEnabled)
                viewer = Camera.main;
            return viewer;
        }
    }
}
