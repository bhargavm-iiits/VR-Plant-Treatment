using System.Collections.Generic;
using System.Text;
using BTP.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// The Treatment lab's world-space panels. The task panel shows the case, the Inspect - Prepare - Treat -
    /// Recheck phase chips with the task count, the current task (numbered step and bullet instructions), the
    /// action buttons that perform it, corrective feedback and what the learner is holding. Case notes list the
    /// scenario, methods and their limitations, and sources. All text comes from the loaded case; nothing here
    /// is disease-specific. Every task can be completed with the action buttons alone
    /// (see <see cref="TreatmentButtonPlanner"/>).
    /// </summary>
    public sealed class TreatmentPanel : MonoBehaviour
    {
        public static readonly string[] Phases = { "Inspect", "Prepare", "Treat", "Recheck" };

        const float Gap = 10f;
        const float RowHeight = 72f;
        const float ChoiceHeight = 80f;
        const float SingleHeight = 96f;
        static readonly Color PrimaryText = new Color(0.02f, 0.11f, 0.14f);

        [Header("Task panel")]
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text subtitle;
        [SerializeField] TMP_Text stepNumber;
        [SerializeField] TMP_Text stepTitle;
        [SerializeField] TMP_Text bullets;
        [SerializeField] TMP_Text feedback;
        [SerializeField] TMP_Text held;
        [SerializeField] Image status;
        [SerializeField] TMP_Text statusText;
        [SerializeField] Button retry;

        [Header("Action buttons")]
        [SerializeField] TMP_Text actionHeader;
        [SerializeField] RectTransform actionArea;
        [SerializeField] List<Button> actions = new List<Button>();

        [Header("Phases")]
        [SerializeField] List<SelectedStateVisual> phaseChips = new List<SelectedStateVisual>();
        [SerializeField] TMP_Text progress;

        [Header("Case notes")]
        [SerializeField] TMP_Text notes;

        TreatmentController controller;
        readonly List<TreatmentButtonAction> plan = new List<TreatmentButtonAction>();
        ColorBlock normalColors;
        bool busy;

        /// <summary>The actions the visible buttons perform, in button order.</summary>
        public IReadOnlyList<TreatmentButtonAction> Plan => plan;

        /// <summary>The action button pool; the first <see cref="Plan"/>.Count are visible.</summary>
        public IReadOnlyList<Button> ActionButtons => actions;

        public bool IsBusy => busy;

        internal void Bind(TMP_Text titleText, TMP_Text subtitleText, TMP_Text number, TMP_Text stepTitleText, TMP_Text bulletText,
            TMP_Text feedbackText, TMP_Text heldText, Image statusImage, TMP_Text statusLabel, Button retryButton,
            TMP_Text header, RectTransform area, List<Button> actionButtons, List<SelectedStateVisual> chips, TMP_Text progressText, TMP_Text notesText)
        {
            title = titleText; subtitle = subtitleText; stepNumber = number; stepTitle = stepTitleText; bullets = bulletText;
            feedback = feedbackText; held = heldText; status = statusImage; statusText = statusLabel; retry = retryButton;
            actionHeader = header; actionArea = area; actions = actionButtons; phaseChips = chips; progress = progressText; notes = notesText;
        }

        internal void Init(TreatmentController owner)
        {
            controller = owner;
            if (actions.Count > 0)
                normalColors = actions[0].colors;
            for (var i = 0; i < actions.Count; i++)
            {
                var index = i;
                actions[i].onClick.RemoveAllListeners();
                actions[i].onClick.AddListener(() => Press(index));
            }

            retry.onClick.RemoveAllListeners();
            retry.onClick.AddListener(() => controller.Retry());
        }

        /// <summary>Presses action button <paramref name="index"/> as a ray, poke or mouse click would.</summary>
        public void Press(int index)
        {
            if (busy || controller == null || index < 0 || index >= plan.Count)
                return;
            controller.PerformButton(plan[index]);
        }

        /// <summary>Locks the action buttons while a tool animation plays.</summary>
        public void SetBusy(bool value)
        {
            busy = value;
            for (var i = 0; i < actions.Count; i++)
                actions[i].interactable = !value;
        }

        public void ShowUnavailable(string caseTitle, IReadOnlyList<string> problems)
        {
            title.text = caseTitle;
            subtitle.text = "Treatment data unavailable";
            stepNumber.text = "!";
            stepTitle.text = "This lesson cannot start";
            var sb = new StringBuilder();
            sb.AppendLine("The treatment record for this case is missing or incomplete, so no other disease or generic treatment is shown.");
            for (var i = 0; i < problems.Count && i < 3; i++)
                sb.AppendLine("• " + problems[i]);
            if (problems.Count > 3)
                sb.AppendLine($"• ...and {problems.Count - 3} more");
            bullets.text = sb.ToString();
            feedback.text = "Choose another case, return to the visiting Lab, or retry.";
            feedback.color = UIPalette.Warning;
            held.text = "";
            plan.Clear();
            LayoutButtons(0f);
            actionHeader.text = "";
            retry.gameObject.SetActive(true);
            SetStatus(null, default);
            for (var i = 0; i < phaseChips.Count; i++)
                phaseChips[i].Selected = false;
            progress.text = "Lesson disabled";
            notes.text = "";
        }

        public void Show(TreatmentSession session, string heldLine, IReadOnlyList<TreatmentButtonAction> buttons)
        {
            var c = session.Case;
            retry.gameObject.SetActive(false);
            title.text = c.Title;
            subtitle.text = $"{c.Category} • {(c.Environment != null ? c.Environment.DisplayName : c.EnvironmentKey)}";

            var step = session.CurrentStep;
            var applicable = new List<TreatmentStep>(session.ApplicableSteps);
            var top = 0f;
            if (step == null)
            {
                stepNumber.text = "OK";
                stepTitle.text = "Lesson complete";
                bullets.text = c.ResultText + "\n<size=80%><color=#B8C7D4>Simulated outcome of a training case. Time was compressed.</color></size>";
                SetStatus(TreatmentServices.OutcomeLabel(session.Progress.Outcome), UIPalette.HealthGood);
                top = SingleHeight * 0.85f + Gap;
            }
            else
            {
                stepNumber.text = (applicable.IndexOf(step) + 1).ToString();
                stepTitle.text = step.Title;
                var sb = new StringBuilder();
                foreach (var line in step.InstructionLines)
                    sb.AppendLine("• " + line);
                var count = session.CountFor(step);
                if (step.RequiredCount > 1)
                    sb.AppendLine($"<color=#22D4ED>{count} of {step.RequiredCount} done</color>");
                bullets.text = sb.ToString();
                SetStatus(null, default);
            }

            actionHeader.text = TreatmentButtonPlanner.Header(session);
            plan.Clear();
            if (buttons != null)
                plan.AddRange(buttons);
            LayoutButtons(top);

            held.text = heldLine ?? "";

            var phase = step != null ? System.Array.IndexOf(Phases, step.Phase) : Phases.Length - 1;
            for (var i = 0; i < phaseChips.Count; i++)
                phaseChips[i].Selected = i == phase;
            progress.text = session.Progress.Completed
                ? $"Task {session.TotalSteps} of {session.TotalSteps} complete"
                : $"Task {Mathf.Min(session.CompletedSteps + 1, session.TotalSteps)} of {session.TotalSteps}";
            notes.text = BuildNotes(c);
        }

        public void ShowHeld(string heldLine) => held.text = heldLine ?? "";

        public void ShowFeedback(string message, bool accepted)
        {
            feedback.text = message ?? "";
            feedback.color = accepted ? UIPalette.Cyan : UIPalette.Warning;
        }

        /// <summary>
        /// Places the visible buttons in the action area: one large button for a single action, full-width rows
        /// for a choice (long labels), otherwise two columns.
        /// </summary>
        void LayoutButtons(float top)
        {
            var width = actionArea != null ? actionArea.rect.width : 560f;
            var single = plan.Count == 1;
            var rows = plan.Count > 0 && plan[0].Kind == TreatmentButtonKind.Choose;
            for (var i = 0; i < actions.Count; i++)
            {
                var button = actions[i];
                var visible = i < plan.Count;
                button.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                float x, y, w, h;
                if (single)
                {
                    x = 0f; y = top; w = width; h = SingleHeight;
                }
                else if (rows)
                {
                    x = 0f; y = top + i * (ChoiceHeight + Gap); w = width; h = ChoiceHeight;
                }
                else
                {
                    w = (width - Gap) * 0.5f;
                    x = (i % 2) * (w + Gap); y = top + (i / 2) * (RowHeight + Gap); h = RowHeight;
                }

                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, -y);
                rect.sizeDelta = new Vector2(w, h);

                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = plan[i].Label;
                    label.fontSizeMax = single ? 30f : rows ? 22f : 24f;
                    label.color = single ? PrimaryText : UIPalette.TextPrimary;
                    label.fontStyle = single ? FontStyles.Bold : FontStyles.Normal;
                }

                if (normalColors.colorMultiplier > 0f)
                {
                    var colors = normalColors;
                    if (single)
                        colors.normalColor = colors.selectedColor = UIPalette.Cyan;
                    button.colors = colors;
                }

                button.interactable = !busy;
            }
        }

        void SetStatus(string text, Color color)
        {
            var visible = !string.IsNullOrEmpty(text);
            status.gameObject.SetActive(visible);
            if (!visible)
                return;
            statusText.text = text;
            status.color = new Color(color.r * 0.35f, color.g * 0.45f, color.b * 0.35f, 0.95f);
        }

        static string BuildNotes(TreatmentCase c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>Case notes</b>");
            sb.AppendLine(c.Scenario);
            sb.AppendLine();
            sb.AppendLine(c.Summary);
            sb.AppendLine();
            sb.AppendLine("<b>Management methods</b>");
            foreach (var method in c.Methods)
                sb.AppendLine($"• <b>{method.Name}</b>: {method.Purpose}. <color=#B8C7D4>Limit: {method.Limitations}</color>");
            sb.AppendLine();
            sb.AppendLine("<b>Sources</b>");
            foreach (var source in c.Evidence)
                sb.AppendLine($"• {source.Publisher} ({source.Region}) <size=80%>{source.Url}</size>");
            sb.AppendLine("<size=85%><color=#FBBF24>Training simulation. Product choice, dose and intervals follow the local label.</color></size>");
            return sb.ToString();
        }
    }
}
