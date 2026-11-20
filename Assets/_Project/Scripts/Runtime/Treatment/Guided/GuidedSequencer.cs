using System;
using System.Collections.Generic;

namespace BTP.Treatment
{
    /// <summary>
    /// The guided treatment's step and instruction-popup state machine (pure; unit tested). Each step starts with ONE
    /// instruction popup that stays up until the player presses OK (<see cref="Acknowledge"/>); OK only acknowledges, the
    /// player then works the step by hand (<see cref="Phase.Working"/>). A dismissed instruction never reopens while its
    /// step is worked (<see cref="RequestInstruction"/> returns false; warnings use a toast instead). Only a successful step
    /// (<see cref="CompleteStep"/>) advances and shows the next instruction; after the last step the treatment is
    /// <see cref="Phase.Completed"/>. A step may play a response first (<see cref="BeginResponse"/>), during which further
    /// attempts are ignored.
    /// </summary>
    public sealed class GuidedSequencer
    {
        public enum Phase
        {
            /// <summary>No case loaded.</summary>
            Idle,
            /// <summary>The current step's instruction popup is up; tools and Apply are blocked until OK.</summary>
            Instruction,
            /// <summary>OK pressed: the player does the step by hand.</summary>
            Working,
            /// <summary>The step succeeded and its response (lights, tick) is playing.</summary>
            Responding,
            /// <summary>Every step is done.</summary>
            Completed,
        }

        readonly List<GuidedStepPlan> steps = new List<GuidedStepPlan>();
        readonly List<string> summaries = new List<string>();

        public Phase Current { get; private set; } = Phase.Idle;
        public int Index { get; private set; } = -1;
        public IReadOnlyList<GuidedStepPlan> Steps => steps;
        public GuidedStepPlan Step => Index >= 0 && Index < steps.Count ? steps[Index] : null;
        public bool InstructionVisible => Current == Phase.Instruction;
        public bool Completed => Current == Phase.Completed;
        /// <summary>How many steps succeeded so far.</summary>
        public int DoneCount => Current == Phase.Completed ? steps.Count : Math.Max(0, Index);
        /// <summary>Times an instruction popup was shown (one per step; tests check nothing reopens).</summary>
        public int InstructionsShown { get; private set; }

        /// <summary>Raised whenever the phase or the step changes.</summary>
        public event Action Changed;
        /// <summary>A step's instruction popup should appear (the step started).</summary>
        public event Action<GuidedStepPlan> InstructionShown;
        public event Action<GuidedStepPlan> StepCompleted;
        public event Action AllCompleted;

        /// <summary>Starts at the first step with its instruction showing. Empty plans: idle.</summary>
        public void Start(IEnumerable<GuidedStepPlan> plans)
        {
            steps.Clear();
            summaries.Clear();
            InstructionsShown = 0;
            if (plans != null)
                steps.AddRange(plans);
            Index = steps.Count > 0 ? 0 : -1;
            Current = steps.Count > 0 ? Phase.Instruction : Phase.Idle;
            Changed?.Invoke();
            if (Current == Phase.Instruction)
                ShowInstruction();
        }

        /// <summary>Back to idle (case changed).</summary>
        public void Stop()
        {
            steps.Clear();
            summaries.Clear();
            Index = -1;
            Current = Phase.Idle;
            Changed?.Invoke();
        }

        void ShowInstruction()
        {
            InstructionsShown++;
            InstructionShown?.Invoke(Step);
        }

        /// <summary>OK on the instruction popup: hides it and hands control back. False when no instruction was showing.</summary>
        public bool Acknowledge()
        {
            if (Current != Phase.Instruction)
                return false;
            Current = Phase.Working;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Something asked to show the current instruction again (the lamp, a tool): a dismissed instruction never reopens
        /// while its step is worked, so this only reports whether it is already up.
        /// </summary>
        public bool RequestInstruction() => Current == Phase.Instruction;

        /// <summary>Whether the player may act on <paramref name="kind"/> now (its step is current and being worked).</summary>
        public bool CanWork(GuidedStepKind kind) => Current == Phase.Working && Step != null && Step.Kind == kind;

        /// <summary>The step succeeded and a response plays before it completes. False unless the step is being worked.</summary>
        public bool BeginResponse(GuidedStepKind kind)
        {
            if (!CanWork(kind))
                return false;
            Current = Phase.Responding;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// The current step succeeded (<paramref name="summary"/>: what was applied, for the Farm recap): the next step's
        /// instruction shows, or the treatment completes. False when <paramref name="kind"/> is not the step being worked.
        /// </summary>
        public bool CompleteStep(GuidedStepKind kind, string summary = null)
        {
            if ((Current != Phase.Working && Current != Phase.Responding) || Step == null || Step.Kind != kind)
                return false;
            var done = Step;
            summaries.Add(summary ?? done.TargetText);
            Index++;
            StepCompleted?.Invoke(done);
            if (Index >= steps.Count)
            {
                Current = Phase.Completed;
                Changed?.Invoke();
                AllCompleted?.Invoke();
                return true;
            }

            Current = Phase.Instruction;
            Changed?.Invoke();
            ShowInstruction();
            return true;
        }

        /// <summary>What each completed step applied, in order.</summary>
        public IReadOnlyList<string> Summaries => summaries;
    }
}
