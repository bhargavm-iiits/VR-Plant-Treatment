using System;
using System.Collections.Generic;
using System.Linq;

namespace BTP.Treatment
{
    /// <summary>An attempted learner action: a held tool used on a scene target, or a panel choice.</summary>
    public sealed class ActionContext
    {
        /// <summary>Set only for panel actions (choose, advance). Scene actions are matched by tool and target.</summary>
        public string Action;
        /// <summary>The held tool key, or null for an empty (gloved) hand.</summary>
        public string ToolKey;
        public string TargetId;
        public string TargetTag;
        public string OptionId;

        public static ActionContext OnTarget(string toolKey, string targetId, string targetTag) =>
            new ActionContext { ToolKey = toolKey, TargetId = targetId, TargetTag = targetTag };

        public static ActionContext Choose(string optionId) => new ActionContext { Action = TreatmentActions.Choose, OptionId = optionId };

        public static ActionContext Advance() => new ActionContext { Action = TreatmentActions.Advance };
    }

    public sealed class ActionResult
    {
        public bool Accepted;
        public string Message;
        public TreatmentStep Step;
        public bool StepCompleted;
        public bool LessonCompleted;

        internal static ActionResult Reject(string message) => new ActionResult { Accepted = false, Message = message };
    }

    /// <summary>
    /// Runs one lesson from database content. It validates every attempted action against the
    /// current step's tool, target, prerequisites and route, explains rejected actions without
    /// changing anything, and records accepted actions in <see cref="CaseProgress"/>.
    /// No disease-specific rule lives here: every rule comes from the case record.
    /// </summary>
    public sealed class TreatmentSession
    {
        public const string CarriedKey = "carried";
        const string ToolPrefix = "tool:";
        const string Contaminated = "contaminated";

        public TreatmentSession(TreatmentCase content, CaseProgress progress)
        {
            Case = content ?? throw new ArgumentNullException(nameof(content));
            Progress = progress ?? new CaseProgress(content.Key, content.ContentVersion);
        }

        public TreatmentCase Case { get; }
        public CaseProgress Progress { get; }

        /// <summary>Raised after every accepted action (instructions, tools, specimen and progress update).</summary>
        public event Action StateChanged;

        public bool IsApplicable(TreatmentStep step) => string.IsNullOrEmpty(step.Route) || step.Route == Progress.Route;

        public bool IsComplete(TreatmentStep step) => Progress.CompletedSteps.Contains(step.StepId);

        public IEnumerable<TreatmentStep> ApplicableSteps => Case.Steps.Where(IsApplicable);

        public int TotalSteps => ApplicableSteps.Count();

        public int CompletedSteps => ApplicableSteps.Count(IsComplete);

        /// <summary>The first unfinished step, shown as the current task; null when the lesson is finished.</summary>
        public TreatmentStep CurrentStep => ApplicableSteps.FirstOrDefault(step => !IsComplete(step));

        public bool IsAvailable(TreatmentStep step)
        {
            if (!IsApplicable(step) || IsComplete(step))
                return false;
            if (step.Prerequisites.Count > 0)
            {
                foreach (var id in step.Prerequisites)
                {
                    var required = Case.FindStep(id);
                    // A prerequisite on another route does not block this learner.
                    if (required != null && IsApplicable(required) && !IsComplete(required))
                        return false;
                }

                return true;
            }

            TreatmentStep previous = null;
            foreach (var candidate in ApplicableSteps)
            {
                if (candidate == step)
                    break;
                previous = candidate;
            }

            return previous == null || IsComplete(previous);
        }

        public int CountFor(TreatmentStep step) =>
            Progress.StepTargets.TryGetValue(step.StepId, out var set) ? set.Count : (IsComplete(step) ? step.RequiredCount : 0);

        public bool IsToolContaminated(string toolKey) =>
            !string.IsNullOrEmpty(toolKey) && Progress.GetEquipment(ToolPrefix + toolKey) == Contaminated;

        public IReadOnlyList<string> CarriedTargets
        {
            get
            {
                var value = Progress.GetEquipment(CarriedKey);
                return string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split(',');
            }
        }

        /// <summary>Whether a specimen target or fixture should currently be visible in the scene.</summary>
        public bool IsTargetVisible(TreatmentTargetDef target)
        {
            if (!string.IsNullOrEmpty(target.ShowAfterStep) && !Progress.CompletedSteps.Contains(target.ShowAfterStep))
                return false;
            if (!string.IsNullOrEmpty(target.HideAfterStep) && Progress.CompletedSteps.Contains(target.HideAfterStep))
                return false;
            var state = Progress.GetTargetState(target.TargetId);
            return state != "disposed" && state != "removed_plant";
        }

        public ActionResult TryPerform(ActionContext context)
        {
            if (context == null)
                return ActionResult.Reject("No action.");
            if (Progress.Completed)
                return ActionResult.Reject("This lesson is complete. Use Reset case to practise it again.");

            var current = CurrentStep;
            if (current == null)
                return ActionResult.Reject("Every task in this lesson is done.");

            var matches = new List<TreatmentStep>();
            foreach (var step in ApplicableSteps)
            {
                if (IsComplete(step) || !Matches(step, context))
                    continue;
                matches.Add(step);
            }

            var chosen = matches.FirstOrDefault(IsAvailable);
            if (chosen == null)
            {
                // Working on the current task's target with the wrong tool: explain the current task
                // rather than a later step that happens to use the same target.
                // Likewise for the right tool on the wrong target (e.g. spraying an old lesion).
                var currentTool = !string.IsNullOrEmpty(current.ToolKey) && context.ToolKey == current.ToolKey;
                if (string.IsNullOrEmpty(context.Action) && IsAvailable(current) && (context.TargetTag == current.TargetTag || currentTool))
                    return ActionResult.Reject(Explain(current, context));
                if (matches.Count > 0)
                    return ActionResult.Reject(matches[0].PrerequisiteFeedback ?? $"Not yet. First: {current.Title}.");
                return ActionResult.Reject(Explain(current, context));
            }

            return Apply(chosen, context);
        }

        bool Matches(TreatmentStep step, ActionContext context)
        {
            if (!string.IsNullOrEmpty(context.Action))
                return step.Action == context.Action;
            if (TreatmentActions.IsPanelAction(step.Action))
                return false;
            if (step.TargetTag != context.TargetTag)
                return false;
            return string.IsNullOrEmpty(step.ToolKey) || step.ToolKey == context.ToolKey;
        }

        string Explain(TreatmentStep current, ActionContext context)
        {
            if (TreatmentActions.IsPanelAction(current.Action) && string.IsNullOrEmpty(context.Action))
                return $"Use the panel: {current.Title}.";
            var toolMismatch = !string.IsNullOrEmpty(current.ToolKey) && context.ToolKey != current.ToolKey;
            if (context.TargetTag == current.TargetTag && toolMismatch)
                return current.WrongToolFeedback ?? "That tool is not right for this task.";
            return current.WrongTargetFeedback ?? (toolMismatch ? current.WrongToolFeedback : null) ?? $"That is not part of this task: {current.Title}.";
        }

        ActionResult Apply(TreatmentStep step, ActionContext context)
        {
            // Engine-level checks that hold for every case.
            if (step.RequiresCleanTool && IsToolContaminated(context.ToolKey))
                return ActionResult.Reject("Clean and disinfect this tool at the sanitation station before using it again.");
            if (step.Action == TreatmentActions.Dispose && CarriedTargets.Count == 0)
                return ActionResult.Reject("Nothing to dispose of yet: remove the marked material first.");

            string message = step.SuccessText;
            if (step.Action == TreatmentActions.Choose)
            {
                var option = step.Options.FirstOrDefault(o => o.OptionId == context.OptionId);
                if (option == null)
                    return ActionResult.Reject("Choose one of the listed options.");
                if (!option.Correct)
                    return ActionResult.Reject(option.Feedback ?? "That choice does not fit this case. Read the case notes again.");
                if (!string.IsNullOrEmpty(option.Route))
                    Progress.Route = option.Route;
                message = option.Feedback ?? message;
            }

            var counted = context.TargetId ?? context.OptionId ?? step.StepId;
            if (!Progress.StepTargets.TryGetValue(step.StepId, out var set))
                Progress.StepTargets[step.StepId] = set = new HashSet<string>();
            if (!set.Add(counted))
                return ActionResult.Reject("Already done here. Choose another marked target.");

            ApplyEffects(step, context);

            var result = new ActionResult { Accepted = true, Step = step, Message = message };
            if (set.Count >= step.RequiredCount)
            {
                Progress.CompletedSteps.Add(step.StepId);
                result.StepCompleted = true;
            }
            else
            {
                result.Message = $"{set.Count} of {step.RequiredCount} done.";
            }

            if (CurrentStep == null)
            {
                Progress.Completed = true;
                Progress.Outcome = Case.Outcome;
                result.LessonCompleted = true;
            }

            StateChanged?.Invoke();
            return result;
        }

        void ApplyEffects(TreatmentStep step, ActionContext context)
        {
            var target = context.TargetId;
            switch (step.Action)
            {
                case TreatmentActions.Cut:
                case TreatmentActions.Collect:
                    SetTarget(target, "removed");
                    AddCarried(target);
                    break;
                case TreatmentActions.Dispose:
                    foreach (var carried in CarriedTargets)
                        SetTarget(carried, "disposed");
                    Progress.Equipment.Remove(CarriedKey);
                    break;
                case TreatmentActions.CleanTool:
                    foreach (var key in Progress.Equipment.Keys.Where(k => k.StartsWith(ToolPrefix)).ToList())
                        Progress.Equipment.Remove(key);
                    break;
                case TreatmentActions.Observe:
                    SetTarget(target, "observed");
                    var followUp = Case.FindFollowUp(step.CheckpointId);
                    if (followUp != null)
                        Progress.Observations[followUp.CheckpointId] = followUp.Finding;
                    break;
                case TreatmentActions.Inspect: SetTarget(target, "inspected", onlyIfEmpty: true); break;
                case TreatmentActions.Spray: SetTarget(target, "covered"); break;
                case TreatmentActions.Contain: SetTarget(target, "contained"); break;
                case TreatmentActions.RemovePlant: SetTarget(target, "removed_plant"); break;
                case TreatmentActions.Plant: SetTarget(target, "planted"); break;
                case TreatmentActions.Place: SetTarget(target, "placed"); break;
                case TreatmentActions.Release: SetTarget(target, "released"); break;
                case TreatmentActions.Irrigate: SetTarget(target, "irrigated"); break;
                case TreatmentActions.Control:
                    SetTarget(target, "on");
                    if (!string.IsNullOrEmpty(context.TargetTag))
                        Progress.Equipment[context.TargetTag] = "on";
                    break;
            }

            if (step.Contaminates && !string.IsNullOrEmpty(context.ToolKey))
                Progress.Equipment[ToolPrefix + context.ToolKey] = Contaminated;
        }

        void SetTarget(string targetId, string state, bool onlyIfEmpty = false)
        {
            if (string.IsNullOrEmpty(targetId))
                return;
            if (onlyIfEmpty && Progress.TargetStates.ContainsKey(targetId))
                return;
            Progress.TargetStates[targetId] = state;
        }

        void AddCarried(string targetId)
        {
            if (string.IsNullOrEmpty(targetId))
                return;
            var carried = new List<string>(CarriedTargets);
            if (!carried.Contains(targetId))
                carried.Add(targetId);
            Progress.Equipment[CarriedKey] = string.Join(",", carried);
        }
    }
}