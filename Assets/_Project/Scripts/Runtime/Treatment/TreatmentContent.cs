using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>How a lesson ends. Values are stored in SQLite by name.</summary>
    public enum TreatmentOutcome
    {
        None,
        /// <summary>Old damage remains; remaining foliage is protected and healthy new growth develops.</summary>
        Managed,
        /// <summary>Pest activity falls and later growth shows less damage.</summary>
        PestSuppressed,
        /// <summary>The infected original is contained or removed and a distinct healthy plant is established.</summary>
        Replaced
    }

    /// <summary>
    /// The action vocabulary the lesson engine understands. Each value is a reusable capability;
    /// which tool, target, order and feedback apply is authored per case in SQLite.
    /// </summary>
    public static class TreatmentActions
    {
        public const string Inspect = "inspect";
        public const string Choose = "choose";
        public const string Cut = "cut";
        public const string Collect = "collect";
        public const string Dispose = "dispose";
        public const string CleanTool = "clean_tool";
        public const string Irrigate = "irrigate";
        public const string Control = "control";
        public const string Spray = "spray";
        public const string Place = "place";
        public const string Release = "release";
        public const string Contain = "contain";
        public const string RemovePlant = "remove_plant";
        public const string Plant = "plant";
        public const string Advance = "advance";
        public const string Observe = "observe";

        /// <summary>Actions performed with a panel button rather than on a scene target.</summary>
        public static bool IsPanelAction(string action) => action == Choose || action == Advance;

        public static readonly HashSet<string> All = new HashSet<string>
        {
            Inspect, Choose, Cut, Collect, Dispose, CleanTool, Irrigate, Control, Spray, Place, Release,
            Contain, RemovePlant, Plant, Advance, Observe
        };
    }

    /// <summary>One plant/disease case as authored in the content database.</summary>
    public sealed class TreatmentCase
    {
        public CaseKey Key;
        public string PlantName;
        public string DiseaseName;
        public string Category;
        public string Scenario;
        public TreatmentOutcome Outcome;
        public string EnvironmentKey;
        public string SpecimenAsset;
        public float SpecimenScale = 1f;
        public string Summary;
        public string ResultText;
        public int ContentVersion;

        public readonly List<string> Modules = new List<string>();
        public readonly List<string> RackTools = new List<string>();
        public readonly List<TreatmentTargetDef> Targets = new List<TreatmentTargetDef>();
        public readonly List<TreatmentMethod> Methods = new List<TreatmentMethod>();
        public readonly List<TreatmentStep> Steps = new List<TreatmentStep>();
        public readonly List<TreatmentFollowUp> FollowUps = new List<TreatmentFollowUp>();
        public readonly List<TreatmentEvidence> Evidence = new List<TreatmentEvidence>();

        /// <summary>Environment profile shared by every case in this setting (architecture and fixtures).</summary>
        public TreatmentEnvironment Environment;

        /// <summary>Manual-treatment mode data (case_profile, treatment_controls, control_rules); null when missing.</summary>
        public ManualTreatmentProfile Manual;
        /// <summary>Why <see cref="Manual"/> could not be read; null when it loaded (it may still fail validation).</summary>
        public string ManualError;

        public string Title => PlantName + " \u2022 " + DiseaseName;

        public TreatmentStep FindStep(string stepId)
        {
            foreach (var step in Steps)
                if (step.StepId == stepId)
                    return step;
            return null;
        }

        /// <summary>Every target in the scene for this case: specimen targets plus enabled fixtures.</summary>
        public IEnumerable<TreatmentTargetDef> SceneTargets()
        {
            foreach (var target in Targets)
                yield return target;
            if (Environment == null)
                yield break;
            foreach (var fixture in Environment.Fixtures)
                if (FixtureEnabled(fixture))
                    yield return fixture;
        }

        public TreatmentTargetDef FindTarget(string targetId)
        {
            foreach (var target in SceneTargets())
                if (target.TargetId == targetId)
                    return target;
            return null;
        }

        public TreatmentFollowUp FindFollowUp(string checkpointId)
        {
            foreach (var followUp in FollowUps)
                if (followUp.CheckpointId == checkpointId)
                    return followUp;
            return null;
        }

        public HashSet<string> AvailableTags()
        {
            var tags = new HashSet<string>();
            foreach (var target in SceneTargets())
                tags.Add(target.Tag);
            return tags;
        }

        public bool FixtureEnabled(TreatmentTargetDef fixture) =>
            string.IsNullOrEmpty(fixture.Module) || Modules.Contains(fixture.Module);
    }

    /// <summary>
    /// A selectable scene object: a leaf, fruit mummy, root zone, vent switch, disposal bin.
    /// Specimen targets come from the case; fixtures come from the environment profile.
    /// </summary>
    public sealed class TreatmentTargetDef
    {
        public string TargetId;
        public string Tag;
        public string AssetKey;
        public string MaterialKey;
        public string Slot;
        public string Label;
        public string Module;
        /// <summary>Step whose completion makes this target appear (e.g. new growth after the simulated wait).</summary>
        public string ShowAfterStep;
        /// <summary>Step whose completion hides this target (e.g. whiteflies after vector control).</summary>
        public string HideAfterStep;
    }

    public sealed class TreatmentMethod
    {
        public string MethodId;
        public string Name;
        public string Purpose;
        public string Limitations;
    }

    public sealed class TreatmentStep
    {
        public string StepId;
        public int Order;
        public string Phase;
        public string Title;
        public string Instruction;
        public string Action;
        public string ToolKey;
        public string TargetTag;
        public int RequiredCount = 1;
        /// <summary>Null when every learner does this step; otherwise only learners who chose this route.</summary>
        public string Route;
        public string MethodId;
        public string CheckpointId;
        /// <summary>Doing this step leaves the tool contaminated until it is cleaned.</summary>
        public bool Contaminates;
        /// <summary>This step is refused while the held tool is contaminated.</summary>
        public bool RequiresCleanTool;
        public string SuccessText;
        public string WrongToolFeedback;
        public string WrongTargetFeedback;
        public string PrerequisiteFeedback;
        /// <summary>Explicit prerequisites. When empty, the previous applicable step is the prerequisite.</summary>
        public readonly List<string> Prerequisites = new List<string>();
        public readonly List<TreatmentOption> Options = new List<TreatmentOption>();

        public string[] InstructionLines =>
            string.IsNullOrEmpty(Instruction) ? Array.Empty<string>() : Instruction.Split('|');
    }

    public sealed class TreatmentOption
    {
        public string OptionId;
        public int Order;
        public string Label;
        public bool Correct;
        public string Route;
        public string Feedback;
    }

    public sealed class TreatmentFollowUp
    {
        public string CheckpointId;
        public string Prompt;
        public string Finding;
        public string Escalation;
    }

    public sealed class TreatmentEvidence
    {
        public string Publisher;
        public string Url;
        public string Reviewed;
        public string Region;
        public string Limitations;
    }

    public sealed class TreatmentEnvironment
    {
        public string EnvironmentKey;
        public string DisplayName;
        public string PrefabKey;
        public string Description;
        public readonly List<TreatmentTargetDef> Fixtures = new List<TreatmentTargetDef>();
    }

    public sealed class TreatmentTool
    {
        public string ToolKey;
        public string Name;
        public string AssetKey;
        public string Description;
    }

    /// <summary>A case entry for selectors: enough to list it without loading every step.</summary>
    public sealed class CaseSummary
    {
        public CaseKey Key;
        public string PlantName;
        public string DiseaseName;
        public string Category;
        public string EnvironmentKey;
    }
}