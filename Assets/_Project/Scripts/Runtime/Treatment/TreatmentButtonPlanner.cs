using System;
using System.Collections.Generic;
using System.Linq;

namespace BTP.Treatment
{
    /// <summary>What an action-panel button does when pressed.</summary>
    public enum TreatmentButtonKind
    {
        /// <summary>Picks one of the current step's options.</summary>
        Choose,
        /// <summary>The simulated time skip.</summary>
        Advance,
        /// <summary>Uses a rack tool (or the gloved hand) on a scene target.</summary>
        UseOnTarget,
        /// <summary>Restarts a finished lesson.</summary>
        PractiseAgain
    }

    /// <summary>One action-panel button: its label and the exact action it sends to the lesson engine.</summary>
    public sealed class TreatmentButtonAction
    {
        public TreatmentButtonKind Kind;
        public string Label;
        /// <summary>The action sent to <see cref="TreatmentSession.TryPerform"/>; null for <see cref="TreatmentButtonKind.PractiseAgain"/>.</summary>
        public ActionContext Context;

        /// <summary>Tool used for a scene action; null for the gloved hand.</summary>
        public string ToolKey => Context?.ToolKey;
        public string TargetId => Context?.TargetId;
    }

    /// <summary>
    /// Turns the current lesson step into action-panel buttons, so every task can be done with buttons alone
    /// (controller ray, hand poke or pinch, desktop mouse). A button only builds the same
    /// <see cref="ActionContext"/> a tool-on-target interaction builds, and the lesson engine decides the result;
    /// no rule is duplicated here.
    /// <list type="bullet">
    /// <item>A choice lists every option, correct or not.</item>
    /// <item>A step that needs a tool lists every tool on the case's rack, used on the step's next marked target.
    /// Choosing the tool stays the learner's decision and a wrong tool gets the content's own feedback.</item>
    /// <item>A gloved-hand step has one button. A room-control step lists each room control in the case.</item>
    /// </list>
    /// </summary>
    public static class TreatmentButtonPlanner
    {
        /// <summary>Most buttons a step can need (the largest tool rack in the content has 7 tools).</summary>
        public const int MaxButtons = 8;

        /// <summary>Wall controls an environment offers; a room-control step lists all of the enabled ones.</summary>
        static readonly string[] RoomControlTags = { "irrigation_valve", "vent_control", "fan_control" };

        static readonly Dictionary<string, string> ToolVerbs = new Dictionary<string, string>
        {
            { "magnifier", "Inspect with magnifier" },
            { "shears", "Cut with pruning shears" },
            { "sprayer", "Apply spray (training)" },
            { "water_wand", "Wash with water wand" },
            { "drip_emitter", "Water with drip emitter" },
            { "stake", "Fit plant stake" },
            { "mulch", "Spread mulch" },
            { "rake", "Rake up" },
            { "trellis_tie", "Tie to the trellis" },
            { "predator_release", "Release predatory mites" },
            { "containment_bag", "Enclose in containment bag" },
            { "spade", "Dig out with spade" },
            { "transplant", "Plant clean transplant" },
            { "insect_mesh", "Fit insect mesh" },
            { "sticky_card", "Hang monitoring card" }
        };

        /// <summary>The button label for using a tool, e.g. "Cut with pruning shears".</summary>
        public static string ToolVerb(string toolKey, string toolName = null)
        {
            if (string.IsNullOrEmpty(toolKey))
                return "Use gloved hand";
            if (ToolVerbs.TryGetValue(toolKey, out var verb))
                return verb;
            return "Use " + (string.IsNullOrEmpty(toolName) ? toolKey : toolName.ToLowerInvariant());
        }

        public static bool HasToolVerb(string toolKey) => ToolVerbs.ContainsKey(toolKey);

        /// <summary>
        /// The target a button press acts on for <paramref name="step"/>: the first visible target with the step's tag
        /// that this step has not counted yet and that is not being carried. Null when none is left.
        /// </summary>
        public static TreatmentTargetDef NextTarget(TreatmentSession session, TreatmentStep step)
        {
            if (session == null || step == null || string.IsNullOrEmpty(step.TargetTag))
                return null;
            session.Progress.StepTargets.TryGetValue(step.StepId, out var counted);
            var carried = session.CarriedTargets;
            TreatmentTargetDef fallback = null;
            foreach (var target in session.Case.SceneTargets())
            {
                if (target.Tag != step.TargetTag || !session.IsTargetVisible(target) || carried.Contains(target.TargetId))
                    continue;
                if (counted != null && counted.Contains(target.TargetId))
                    continue;
                // Prefer targets nothing has changed yet (e.g. a lesion that was not cut).
                if (session.Progress.GetTargetState(target.TargetId) == "removed")
                {
                    fallback = fallback ?? target;
                    continue;
                }

                return target;
            }

            return fallback;
        }

        /// <summary>Buttons for the session's current step (or the finished lesson), in display order.</summary>
        /// <param name="toolName">Display name of a tool key, or null when unknown.</param>
        public static List<TreatmentButtonAction> Plan(TreatmentSession session, Func<string, string> toolName = null)
        {
            var buttons = new List<TreatmentButtonAction>();
            if (session == null)
                return buttons;
            var step = session.CurrentStep;
            if (step == null)
            {
                buttons.Add(new TreatmentButtonAction { Kind = TreatmentButtonKind.PractiseAgain, Label = "Practise this case again" });
                return buttons;
            }

            if (step.Action == TreatmentActions.Choose)
            {
                foreach (var option in step.Options.OrderBy(o => o.Order))
                    buttons.Add(new TreatmentButtonAction { Kind = TreatmentButtonKind.Choose, Label = option.Label, Context = ActionContext.Choose(option.OptionId) });
                return buttons;
            }

            if (step.Action == TreatmentActions.Advance)
            {
                buttons.Add(new TreatmentButtonAction { Kind = TreatmentButtonKind.Advance, Label = step.Title, Context = ActionContext.Advance() });
                return buttons;
            }

            var target = NextTarget(session, step);
            if (target == null)
                return buttons;

            if (!string.IsNullOrEmpty(step.ToolKey))
            {
                // Every rack tool on the same target: the tool choice stays a learning decision.
                var tools = new List<string>(session.Case.RackTools);
                if (!tools.Contains(step.ToolKey))
                    tools.Insert(0, step.ToolKey);
                foreach (var tool in tools.Take(MaxButtons))
                    buttons.Add(UseOn(tool, target, ToolVerb(tool, toolName?.Invoke(tool))));
                return buttons;
            }

            if (Array.IndexOf(RoomControlTags, step.TargetTag) >= 0)
            {
                // Room controls: every enabled wall control, each labelled by what it does in this case.
                foreach (var control in RoomControls(session))
                    buttons.Add(UseOn(null, control, ControlLabel(session, control)));
                if (buttons.Count > 0)
                    return buttons;
            }

            buttons.Add(UseOn(null, target, step.Title));
            return buttons;
        }

        static TreatmentButtonAction UseOn(string tool, TreatmentTargetDef target, string label) => new TreatmentButtonAction
        {
            Kind = TreatmentButtonKind.UseOnTarget,
            Label = label,
            Context = ActionContext.OnTarget(tool, target.TargetId, target.Tag)
        };

        static IEnumerable<TreatmentTargetDef> RoomControls(TreatmentSession session)
        {
            foreach (var tag in RoomControlTags)
            {
                var control = session.Case.SceneTargets().FirstOrDefault(t => t.Tag == tag && session.IsTargetVisible(t));
                if (control != null)
                    yield return control;
            }
        }

        /// <summary>A room control's own step title in this case (e.g. "Open the roof vent"), or "Switch on ...".</summary>
        static string ControlLabel(TreatmentSession session, TreatmentTargetDef control)
        {
            var step = session.ApplicableSteps.FirstOrDefault(s => s.TargetTag == control.Tag && s.Action == TreatmentActions.Control);
            if (step != null)
                return step.Title;
            return "Switch on " + (string.IsNullOrEmpty(control.Label) ? control.Tag : control.Label.ToLowerInvariant());
        }

        /// <summary>One line above the buttons saying what they act on.</summary>
        public static string Header(TreatmentSession session)
        {
            var step = session?.CurrentStep;
            if (step == null)
                return "Outcome";
            if (step.Action == TreatmentActions.Choose)
                return "Choose one";
            if (step.Action == TreatmentActions.Advance)
                return "Simulation";
            var target = NextTarget(session, step);
            if (target == null)
                return "Nothing left to act on for this task";
            if (!string.IsNullOrEmpty(step.ToolKey))
                return "Choose a tool for: " + target.Label;
            if (Array.IndexOf(RoomControlTags, step.TargetTag) >= 0)
                return "Room controls";
            return "Action: " + target.Label;
        }
    }
}
