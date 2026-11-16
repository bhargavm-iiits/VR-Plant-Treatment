using System;

namespace BTP.Treatment
{
    /// <summary>The five stages of a manual treatment session, plus not started and completed.</summary>
    public enum ManualStage
    {
        NotStarted = 0,
        Diagnosis = 1,
        Selection = 2,
        Application = 3,
        Monitoring = 4,
        FinalAssessment = 5,
        Completed = 6
    }

    /// <summary>
    /// The 1-minute session clock. Each stage has a 12-second guidance window (Diagnosis 0-12 s, Selection 12-24,
    /// Application 24-36, Monitoring 36-48, Final assessment 48-60). Windows are guidance only: stages advance on the
    /// learner's actions. The clock only counts time; it never changes the leaf. It pauses with the application
    /// and resumes from the saved elapsed time after a restart.
    /// </summary>
    public sealed class TreatmentTimer
    {
        public const float SessionSeconds = 60f;
        public const float StageSeconds = SessionSeconds / StageCount;
        public const int StageCount = 5;

        public float Elapsed { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }

        public float Remaining => Math.Max(0f, SessionSeconds - Elapsed);
        public bool TimeUp => Elapsed >= SessionSeconds;

        public void Start(float elapsed = 0f)
        {
            Elapsed = Math.Max(0f, Math.Min(SessionSeconds, elapsed));
            Running = true;
            Paused = false;
        }

        public void Pause() => Paused = Running;

        public void Resume() => Paused = false;

        public void Stop() => Running = false;

        /// <summary>Adds real time while running and not paused. Returns true the moment the session time runs out.</summary>
        public bool Tick(float seconds)
        {
            if (!Running || Paused || seconds <= 0f || TimeUp)
                return false;
            Elapsed = Math.Min(SessionSeconds, Elapsed + seconds);
            return TimeUp;
        }

        /// <summary>The stage whose guidance window contains the elapsed time.</summary>
        public ManualStage GuidanceStage => GuidanceStageAt(Elapsed);

        public static ManualStage GuidanceStageAt(float elapsed)
        {
            var index = (int)Math.Floor(Math.Max(0f, elapsed) / StageSeconds);
            return (ManualStage)(Math.Min(StageCount - 1, index) + 1);
        }

        /// <summary>Start and end of a stage's guidance window, in seconds.</summary>
        public static (float start, float end) Window(ManualStage stage)
        {
            var i = Math.Max(1, Math.Min(StageCount, (int)stage)) - 1;
            return (i * StageSeconds, (i + 1) * StageSeconds);
        }

        /// <summary>"(0-12 s)" for a stage's guidance window; minutes once a window is a whole number of minutes.</summary>
        public static string WindowLabel(ManualStage stage)
        {
            var (start, end) = Window(stage);
            if (StageSeconds >= 60f && Math.Abs(StageSeconds % 60f) < 1e-3f)
                return $"({start / 60f:0}-{end / 60f:0} min)";
            return $"({start:0}-{end:0} s)";
        }

        public static string Clock(float seconds)
        {
            var s = (int)Math.Ceiling(Math.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
