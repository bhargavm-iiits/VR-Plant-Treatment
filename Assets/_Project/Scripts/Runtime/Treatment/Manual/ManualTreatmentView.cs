using System;
using System.Collections.Generic;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The Manual treatment mode's world-space panels (built by TreatmentSceneBuilder): the mode switch and learner
    /// picker on the case selector, Disease information, Treatment adjustment, Treatment progress, Leaf health
    /// monitoring and Final results. It only displays what <see cref="ManualTreatmentController"/> gives it and
    /// forwards button presses; every interaction is a UI button (XR ray / poke or desktop click).
    /// </summary>
    public sealed class ManualTreatmentView : MonoBehaviour
    {
        [Serializable]
        public sealed class ControlRow
        {
            public GameObject root;
            public TMP_Text name;
            public TMP_Text description;
            public TMP_Text values;
            public TMP_Text status;
            public Button minus;
            public Button plus;
        }

        [Header("Mode and learner (case selector panel)")]
        [SerializeField] internal Button stepModeButton;
        [SerializeField] internal SelectedStateVisual stepModeState;
        [SerializeField] internal Button manualModeButton;
        [SerializeField] internal SelectedStateVisual manualModeState;
        [SerializeField] internal TMP_Text learnerName;
        [SerializeField] internal Button learnerPrevious;
        [SerializeField] internal Button learnerNext;
        [SerializeField] internal Button learnerNew;

        [Header("Panels shown only in one mode")]
        [SerializeField] internal List<GameObject> manualPanels = new List<GameObject>();
        [Tooltip("The specimen chamber's control panel replaces the manual panels: they are never shown (the controller keeps running).")]
        [SerializeField] internal bool panelsSuppressed;
        [SerializeField] internal List<GameObject> stepPanels = new List<GameObject>();

        [Header("2 Disease information")]
        [SerializeField] internal TMP_Text infoTitle;
        [SerializeField] internal TMP_Text infoStatus;
        [SerializeField] internal TMP_Text infoBody;
        [SerializeField] internal Button infoPrimary;
        [SerializeField] internal Button infoSecondary;

        [Header("3 Treatment adjustment")]
        [SerializeField] internal GameObject adjustmentPanel;
        [SerializeField] internal TMP_Text adjustmentHint;
        [SerializeField] internal List<ControlRow> rows = new List<ControlRow>();
        [SerializeField] internal Button applyButton;
        [SerializeField] internal TMP_Text adjustmentFeedback;

        [Header("4 Treatment progress")]
        [SerializeField] internal TMP_Text clock;
        [SerializeField] internal List<SelectedStateVisual> stageChips = new List<SelectedStateVisual>();
        [SerializeField] internal List<TMP_Text> stageLabels = new List<TMP_Text>();
        [SerializeField] internal TMP_Text guidance;
        [SerializeField] internal Button stageButton;
        [SerializeField] internal TMP_Text history;

        [Header("5 Leaf health monitoring")]
        [SerializeField] internal MetricBar healthBar;
        [SerializeField] internal MetricBar severityBar;
        [SerializeField] internal Button checkHealthButton;
        [SerializeField] internal TMP_Text healthNote;

        [Header("6 Final results")]
        [SerializeField] internal GameObject resultsPanel;
        [SerializeField] internal TMP_Text resultsBody;
        [SerializeField] internal Button newSessionButton;
        [Tooltip("Optional: saves the assessment of a connected scan. Created next to New session at runtime when empty.")]
        [SerializeField] internal Button submitButton;

        public event Action<bool> ModeRequested;
        public event Action<int> LearnerStep;
        public event Action NewLearner;
        public event Action InfoPrimary;
        public event Action InfoSecondary;
        public event Action<int, int> Adjust;
        public event Action Apply;
        public event Action StageAction;
        public event Action CheckHealth;
        public event Action NewSession;
        public event Action SubmitRequested;

        bool manualVisible;
        bool submitLayout;
        // Room the submit button takes from the results text (one button row plus a gap, in canvas millimetres).
        const float SubmitRow = 98f;

        public int RowCount => rows.Count;

        void Awake()
        {
            void On(Button button, Action action)
            {
                if (button == null)
                    return;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => action());
            }

            On(stepModeButton, () => ModeRequested?.Invoke(false));
            On(manualModeButton, () => ModeRequested?.Invoke(true));
            On(learnerPrevious, () => LearnerStep?.Invoke(-1));
            On(learnerNext, () => LearnerStep?.Invoke(+1));
            On(learnerNew, () => NewLearner?.Invoke());
            On(infoPrimary, () => InfoPrimary?.Invoke());
            On(infoSecondary, () => InfoSecondary?.Invoke());
            On(applyButton, () => Apply?.Invoke());
            On(stageButton, () => StageAction?.Invoke());
            On(checkHealthButton, () => CheckHealth?.Invoke());
            On(newSessionButton, () => NewSession?.Invoke());
            On(submitButton, () => SubmitRequested?.Invoke());
            if (submitButton != null)
                submitButton.gameObject.SetActive(false);
            for (var i = 0; i < rows.Count; i++)
            {
                var index = i;
                On(rows[i].minus, () => Adjust?.Invoke(index, -1));
                On(rows[i].plus, () => Adjust?.Invoke(index, +1));
            }
        }

        // ------------------------------------------------------------------ mode and learner

        public void ShowMode(bool manual)
        {
            manualVisible = manual;
            if (stepModeState != null)
                stepModeState.Selected = !manual;
            if (manualModeState != null)
                manualModeState.Selected = manual;
            foreach (var panel in manualPanels)
                if (panel != null)
                    panel.SetActive(manual && !panelsSuppressed);
            foreach (var panel in stepPanels)
                if (panel != null)
                    panel.SetActive(!manual);
        }

        public void ShowLearner(string name, bool canBrowse)
        {
            if (learnerName != null)
                learnerName.text = string.IsNullOrEmpty(name) ? "No learner yet: press + New" : name;
            if (learnerPrevious != null)
                learnerPrevious.interactable = canBrowse;
            if (learnerNext != null)
                learnerNext.interactable = canBrowse;
        }

        // ------------------------------------------------------------------ 2 disease information

        public void ShowInfo(string title, string status, bool ok, string body)
        {
            infoTitle.text = title ?? "";
            infoStatus.text = status ?? "";
            infoStatus.color = ok ? UIPalette.Cyan : UIPalette.Warning;
            infoBody.text = body ?? "";
        }

        public void SetInfoButtons(string primary, bool primaryEnabled, string secondary)
        {
            SetButton(infoPrimary, primary, primaryEnabled);
            SetButton(infoSecondary, secondary, true);
        }

        // ------------------------------------------------------------------ 3 adjustment and 6 results

        /// <summary>True when the chamber panel replaces the manual panels.</summary>
        public bool PanelsSuppressed => panelsSuppressed;

        public void ShowResults(bool visible, string body)
        {
            if (!manualVisible || panelsSuppressed)
            {
                // Step-lesson mode: neither manual panel shows, whatever the session state.
                if (resultsPanel != null)
                    resultsPanel.SetActive(false);
                if (adjustmentPanel != null)
                    adjustmentPanel.SetActive(false);
                return;
            }

            if (resultsPanel != null)
                resultsPanel.SetActive(visible);
            if (adjustmentPanel != null)
                adjustmentPanel.SetActive(!visible);
            if (visible && resultsBody != null)
                resultsBody.text = body ?? "";
        }

        /// <summary>
        /// The connected-scan save button on the results panel ("Update Treatment in Lab", "Saving treatment", "Retry").
        /// A null label hides it and gives the space back to the results text.
        /// </summary>
        public void SetSubmit(string label, bool enabled)
        {
            var visible = !string.IsNullOrEmpty(label);
            if (visible && submitButton == null)
                CreateSubmitButton();
            if (submitButton == null)
                return;
            SetSubmitLayout(visible);
            SetButton(submitButton, label, enabled);
        }

        void CreateSubmitButton()
        {
            if (newSessionButton == null)
                return;
            submitButton = Instantiate(newSessionButton, newSessionButton.transform.parent, false);
            submitButton.name = "UpdateTreatmentInLab";
            var rect = (RectTransform)submitButton.transform;
            rect.anchoredPosition += new Vector2(0f, SubmitRow);
            submitButton.onClick.RemoveAllListeners();
            submitButton.onClick.AddListener(() => SubmitRequested?.Invoke());
            // The highlight colour marks the primary action.
            var colors = submitButton.colors;
            colors.normalColor = UIPalette.ButtonSelected;
            submitButton.colors = colors;
        }

        void SetSubmitLayout(bool withButton)
        {
            if (withButton == submitLayout || resultsBody == null)
                return;
            submitLayout = withButton;
            var size = resultsBody.rectTransform.sizeDelta;
            size.y += withButton ? -SubmitRow : SubmitRow;
            resultsBody.rectTransform.sizeDelta = size;
        }

        public void ShowAdjustmentHint(string text) => adjustmentHint.text = text ?? "";

        public void ShowRow(int index, string name, string description, string values, string status, Color statusColor, bool canAdjust, bool canDecrease,
            bool canIncrease)
        {
            if (index < 0 || index >= rows.Count)
                return;
            var row = rows[index];
            row.root.SetActive(true);
            row.name.text = name;
            row.description.text = description;
            row.values.text = values;
            row.status.text = status;
            row.status.color = statusColor;
            row.minus.interactable = canAdjust && canDecrease;
            row.plus.interactable = canAdjust && canIncrease;
        }

        public void HideRowsFrom(int index)
        {
            for (var i = Mathf.Max(0, index); i < rows.Count; i++)
                rows[i].root.SetActive(false);
        }

        public void SetApply(string label, bool enabled) => SetButton(applyButton, label, enabled);

        public void ShowFeedback(string message, bool accepted)
        {
            adjustmentFeedback.text = message ?? "";
            adjustmentFeedback.color = accepted ? UIPalette.Cyan : UIPalette.Warning;
        }

        // ------------------------------------------------------------------ 4 progress

        public void ShowClock(string text) => clock.text = text;

        public void ShowStages(int current, IReadOnlyList<string> labels, IReadOnlyList<bool> done)
        {
            for (var i = 0; i < stageChips.Count; i++)
            {
                stageChips[i].Selected = i + 1 == current;
                if (i < stageLabels.Count && i < labels.Count)
                    stageLabels[i].text = (done != null && i < done.Count && done[i] ? "<color=#34D399>(done)</color> " : "") + labels[i];
            }
        }

        public void ShowGuidance(string text) => guidance.text = text ?? "";

        public void SetStageButton(string label, bool visible, bool enabled)
        {
            if (stageButton == null)
                return;
            if (!visible)
            {
                stageButton.gameObject.SetActive(false);
                return;
            }

            SetButton(stageButton, label, enabled);
        }

        public void ShowHistory(string text) => history.text = text ?? "";

        // ------------------------------------------------------------------ 5 health

        public void ShowHealth(float health, float severity)
        {
            healthBar.SetValue(TreatmentSessionManager.PercentValue(health));
            severityBar.SetValue(TreatmentSessionManager.PercentValue(severity));
        }

        public void SetCheckHealth(bool enabled, string note)
        {
            if (checkHealthButton != null)
                checkHealthButton.interactable = enabled;
            if (healthNote != null)
                healthNote.text = note ?? "";
        }

        static void SetButton(Button button, string label, bool enabled)
        {
            if (button == null)
                return;
            var visible = !string.IsNullOrEmpty(label);
            button.gameObject.SetActive(visible);
            button.interactable = enabled;
            var text = button.GetComponentInChildren<TMP_Text>();
            if (text != null && visible)
                text.text = label;
        }
    }
}
