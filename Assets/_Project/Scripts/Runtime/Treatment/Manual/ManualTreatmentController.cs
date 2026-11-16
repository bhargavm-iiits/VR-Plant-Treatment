using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BTP.Core;
using BTP.UI;
using UnityEngine;

namespace BTP.Treatment
{
    /// <summary>
    /// The Manual treatment mode of the Treatment lab, alongside the step lessons. Crop and disease come from the
    /// shared selection (TreatmentCaseSelector); the case and its manual profile are fetched from the bundled SQLite
    /// content through TreatmentServices; sessions and learners are saved in the progress database. The learner sets
    /// each treatment level with − / + buttons, applies the treatment, monitors the leaf and finishes with a final
    /// assessment inside a 1-minute, five-stage session (TreatmentTimer). The leaf changes only after a validated Apply.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class ManualTreatmentController : MonoBehaviour, IEnvironmentExit
    {
        [SerializeField] TreatmentController lesson;
        [SerializeField] ManualTreatmentView view;
        [SerializeField] LeafRecoveryController recovery;

        TreatmentServices services;
        TreatmentDataManager data;
        ProgressSaveManager saves;
        TreatmentCase content;
        List<string> problems = new List<string>();
        TreatmentSessionManager session;
        ManualSessionRecord resumable;
        LearnerRecord learner;
        bool manual;
        bool started;
        // The case belongs to a connected XR session: its learner profile is the session's, and results go to the sink.
        bool connected;
        string lastFeedback;
        bool lastAccepted = true;
        int shownSecond = -1;

        public bool IsManualMode => manual;
        public TreatmentSessionManager Session => session;
        public LearnerRecord Learner => learner;

        internal void Bind(TreatmentController lessonController, ManualTreatmentView manualView, LeafRecoveryController leafRecovery)
        {
            lesson = lessonController;
            view = manualView;
            recovery = leafRecovery;
        }

        void Start()
        {
            services = TreatmentServices.Instance;
            view.ModeRequested += SetMode;
            view.LearnerStep += StepLearner;
            view.NewLearner += CreateLearner;
            view.InfoPrimary += OnInfoPrimary;
            view.InfoSecondary += OnInfoSecondary;
            view.Adjust += OnAdjust;
            view.Apply += OnApply;
            view.StageAction += OnStageAction;
            view.CheckHealth += OnCheckHealth;
            view.NewSession += () => StartNewSession(true);
            view.SubmitRequested += OnSubmit;
            if (lesson != null)
                lesson.CaseLoaded += OnCaseLoaded;

            if (services == null)
            {
                view.ShowMode(false);
                view.ShowInfo("Manual treatment", "Treatment services are not running. Start the application from XRBootstrap.", false, "");
                return;
            }

            data = new TreatmentDataManager(services.Content, key => (services.GetCase(key, out var error), error));
            saves = new ProgressSaveManager(services.Progress);
            learner = saves.LastLearner();
            if (services.ResultSink != null)
                services.ResultSink.Changed += OnSinkChanged;
            started = true;
            // The step-lesson controller loads its case in its own Start (earlier), so pick that case up now.
            OnCaseLoaded();
            // The step-lesson windows were removed from the lab, so Manual treatment is always the active mode.
            SetMode(true, true);
        }

        void OnDestroy()
        {
            if (lesson != null)
                lesson.CaseLoaded -= OnCaseLoaded;
            if (services != null && services.ResultSink != null)
                services.ResultSink.Changed -= OnSinkChanged;
            SaveSession();
        }

        void OnSinkChanged()
        {
            if (started)
                RefreshAll();
        }

        public void PrepareForExit()
        {
            session?.Timer.Pause();
            SaveSession();
        }

        void OnApplicationPause(bool paused)
        {
            if (session == null)
                return;
            if (paused)
            {
                session.Timer.Pause();
                SaveSession();
            }
            else
            {
                session.Timer.Resume();
            }
        }

        void OnApplicationFocus(bool focused) => OnApplicationPause(!focused);

        void OnApplicationQuit() => SaveSession();

        // ------------------------------------------------------------------ mode

        public void SetMode(bool manualMode) => SetMode(manualMode, false);

        void SetMode(bool manualMode, bool force)
        {
            if (!started || (manualMode == manual && !force))
                return;
            manual = manualMode;
            saves.RememberMode(manual);
            if (!manual)
            {
                session?.Timer.Pause();
                SaveSession();
                recovery.Detach();
            }

            lesson?.SetManualMode(manual);
            view.ShowMode(manual);
            if (manual)
            {
                AttachLeaf();
                session?.Timer.Resume();
            }

            RefreshAll();
        }

        // ------------------------------------------------------------------ case

        /// <summary>A case was loaded by the Treatment lab (selection change, retry or reset): database fetch for the manual mode.</summary>
        void OnCaseLoaded()
        {
            if (!started)
                return;
            if (session != null)
            {
                session.Timer.Pause();
                SaveSession();
                session = null;
            }

            view.ShowInfo(TitleFor(lesson != null ? lesson.CaseKey : default), "Fetching treatment data from the offline database...", true, "");
            content = data.LoadManualCase(lesson != null ? lesson.CaseKey : default, out problems);
            if (content == null)
                foreach (var problem in problems)
                    Debug.LogWarning($"[BTP] Manual treatment unavailable for {lesson?.CaseKey}: {problem}");
            ApplyLearnerPolicy();
            FindResumable();
            lastFeedback = null;
            if (manual)
                AttachLeaf();
            RefreshAll();
        }

        void AttachLeaf()
        {
            if (lesson == null || lesson.Specimen == null || content == null)
            {
                recovery.Detach();
                return;
            }

            recovery.Attach(lesson.Specimen, session != null ? session.Target : LeafRecoveryState.Initial(content.Manual));
        }

        /// <summary>
        /// A connected scan keeps its drafts and progress under its own learner profile, so two scans of the same disease
        /// never share a saved assessment. Offline the remembered learner is used, as before.
        /// </summary>
        void ApplyLearnerPolicy()
        {
            var sink = services.ResultSink;
            var bound = sink != null && content != null && sink.Binds(content.Key);
            if (bound)
            {
                var sessionLearner = services.Progress.CreateLearner(sink.LearnerName);
                if (sessionLearner != null)
                {
                    connected = true;
                    learner = sessionLearner;
                    return;
                }

                Debug.LogError("[BTP] The connected session's learner profile could not be created: " + services.Progress.Error, this);
            }

            if (connected)
            {
                connected = false;
                learner = saves.LastLearner();
            }
        }

        void OnSubmit()
        {
            var sink = services?.ResultSink;
            if (!connected || sink == null || content == null || session == null || session.Stage != ManualStage.Completed)
                return;
            SaveSession();
            sink.Submit(content, session.Record);
            RefreshAll();
        }

        void FindResumable() =>
            resumable = content != null && learner != null ? saves.FindResumable(learner.LearnerId, content.Key) : null;

        static string TitleFor(CaseKey key)
        {
            foreach (var info in BTP.Plants.DiseaseClasses.For(key.PlantId))
                if (info.DiseaseId == key.DiseaseId)
                    return $"{key.PlantId} • {info.DisplayName}";
            return key.IsValid ? key.ToString() : "Manual treatment";
        }

        // ------------------------------------------------------------------ learners

        void StepLearner(int direction)
        {
            if (connected)
                return;
            var list = saves.Learners();
            if (list.Count == 0)
                return;
            var index = learner == null ? -1 : list.ToList().FindIndex(l => l.LearnerId == learner.LearnerId);
            index = ((index + direction) % list.Count + list.Count) % list.Count;
            SelectLearner(list[index]);
        }

        void CreateLearner()
        {
            if (connected)
                return;
            var created = saves.CreateNextLearner();
            if (created == null)
            {
                Feedback(saves.Error ?? "The learner could not be created.", false);
                RefreshAll();
                return;
            }

            SelectLearner(created);
            Feedback($"Learner '{created.Name}' created on this headset.", true);
        }

        void SelectLearner(LearnerRecord next)
        {
            if (session != null)
            {
                session.Timer.Pause();
                SaveSession();
                session = null;
            }

            learner = next;
            saves.RememberLearner(learner);
            FindResumable();
            if (manual)
                AttachLeaf();
            RefreshAll();
        }

        // ------------------------------------------------------------------ session actions

        void OnInfoPrimary()
        {
            if (content == null)
                return;
            if (session == null)
            {
                if (resumable != null)
                    ResumeSession();
                else
                    StartNewSession(false);
                return;
            }

            if (session.Stage == ManualStage.Diagnosis)
                Handle(session.ConfirmDiagnosis());
            else if (session.Stage == ManualStage.Completed)
                StartNewSession(false);
        }

        /// <summary>"Start over": the unfinished session is kept as abandoned (never deleted) and a new one begins.</summary>
        void OnInfoSecondary() => StartNewSession(true);

        void StartNewSession(bool abandonCurrent)
        {
            if (content == null)
                return;
            if (learner == null)
            {
                // Button-only VR: the first learner is created automatically.
                learner = saves.Learners().FirstOrDefault() ?? saves.CreateNextLearner();
                if (!connected)
                    saves.RememberLearner(learner);
                if (learner == null)
                {
                    Feedback(saves.Error ?? "No learner profile could be created.", false);
                    RefreshAll();
                    return;
                }
            }

            var now = DateTime.UtcNow;
            if (abandonCurrent)
            {
                if (session != null && session.IsActive)
                    saves.Abandon(session.Record, now);
                else if (resumable != null)
                    saves.Abandon(resumable, now);
            }

            resumable = null;
            session = new TreatmentSessionManager(content, TreatmentSessionManager.NewRecord(content, learner.LearnerId, now));
            Handle(session.Start());
            recovery.SetTarget(session.Target, false);
        }

        void ResumeSession()
        {
            session = new TreatmentSessionManager(content, resumable);
            resumable = null;
            recovery.SetTarget(session.Target, false);
            Handle(session.Resume());
        }

        void OnAdjust(int row, int direction)
        {
            if (session == null || content == null || row >= content.Manual.Controls.Count)
                return;
            Handle(session.Adjust(content.Manual.Controls[row].ControlId, direction));
        }

        void OnApply()
        {
            if (session == null)
                return;
            var result = session.Apply();
            if (result.TargetChanged)
                recovery.SetTarget(session.Target, true);
            Handle(result);
        }

        void OnStageAction()
        {
            if (session == null)
                return;
            switch (session.Stage)
            {
                case ManualStage.Application:
                    Handle(session.ContinueToMonitoring());
                    break;
                case ManualStage.Monitoring:
                    Handle(session.ProceedToFinal());
                    break;
                case ManualStage.FinalAssessment:
                    Handle(session.Finish());
                    break;
            }
        }

        void OnCheckHealth()
        {
            if (session != null)
                Handle(session.CheckHealth(recovery.Monitor != null ? recovery.Monitor.Current : session.Target));
        }

        void Handle(ManualActionResult result)
        {
            Feedback(result.Message, result.Accepted);
            SaveSession();
            RefreshAll();
        }

        void Feedback(string message, bool accepted)
        {
            lastFeedback = message;
            lastAccepted = accepted;
        }

        void SaveSession()
        {
            // At shutdown TreatmentServices may already have closed the progress database.
            if (session == null || saves == null || !saves.IsReady)
                return;
            if (!saves.Save(session))
                Debug.LogError("[BTP] " + saves.Error, this);
        }

        void Update()
        {
            if (!manual || session == null)
                return;
            var tick = session.Tick(Time.unscaledDeltaTime);
            if (tick.StageChanged)
                Handle(tick);
            else
                saves.SaveIfDue(session, Time.unscaledDeltaTime);

            var second = (int)session.Timer.Elapsed;
            if (second != shownSecond)
            {
                shownSecond = second;
                RefreshProgress();
            }

            if (recovery.Monitor != null && recovery.Monitor.IsTransitioning)
                RefreshHealth();
        }

        // ------------------------------------------------------------------ display

        void RefreshAll()
        {
            if (!started)
                return;
            if (connected)
                view.ShowLearner($"{learner?.Name} (connected scan)", false);
            else
                view.ShowLearner(learner?.Name, saves.Learners().Count > 1);
            RefreshInfo();
            RefreshAdjustment();
            RefreshProgress();
            RefreshHealth();
        }

        void RefreshInfo()
        {
            var key = lesson != null ? lesson.CaseKey : default;
            if (content == null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("The manual treatment data for this case is missing or incomplete, so no other disease or generic treatment is shown.");
                foreach (var problem in problems.Take(4))
                    sb.AppendLine("• " + problem);
                view.ShowInfo(TitleFor(key), "Manual treatment unavailable", false, sb.ToString());
                view.SetInfoButtons(null, false, null);
                return;
            }

            view.ShowInfo(content.Title, $"Loaded from the offline database (content v{content.ContentVersion})", true, BuildInfo(content));
            if (session == null)
            {
                if (resumable != null)
                    view.SetInfoButtons($"Resume session ({TreatmentSessionManager.StageName(resumable.CurrentStage)}, {TreatmentTimer.Clock(resumable.ElapsedSeconds)})",
                        true, "Start over");
                else
                    view.SetInfoButtons("Start manual session", true, null);
            }
            else if (session.Stage == ManualStage.Diagnosis)
                view.SetInfoButtons("Diagnosis reviewed", true, "Start over");
            else if (session.Stage == ManualStage.Completed)
                view.SetInfoButtons("Start a new session", true, null);
            else
                view.SetInfoButtons($"Session in progress: {TreatmentSessionManager.StageName(session.Stage)}", false, "Start over");
        }

        static string BuildInfo(TreatmentCase c)
        {
            var m = c.Manual;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{c.Category}</b> • {SeverityWord(m.InitialSeverity)} severity ({TreatmentSessionManager.Percent(m.InitialSeverity)} of the leaf)");
            sb.AppendLine(c.Summary);
            sb.AppendLine($"<color=#B8C7D4>{m.Description}</color>");
            sb.AppendLine();
            sb.AppendLine("<b>Symptoms</b>");
            foreach (var symptom in m.Symptoms)
                sb.AppendLine("• " + symptom);
            sb.AppendLine();
            sb.AppendLine("<b>Management methods</b>");
            foreach (var method in c.Methods)
                sb.AppendLine($"• <b>{method.Name}</b>: {method.Purpose}. <color=#B8C7D4>Limit: {method.Limitations}</color>");
            sb.AppendLine();
            sb.Append("<b>Source</b>: ");
            sb.AppendLine(string.Join("; ", c.Evidence.Select(e => $"{e.Publisher} ({e.Region})").Distinct()));
            sb.AppendLine($"<size=85%><color=#FBBF24>{TreatmentResultManager.Disclaimer} Levels are abstract 0-10 teaching scales: no products or doses.</color></size>");
            return sb.ToString();
        }

        static string SubmitLabel(TreatmentSubmitState state)
        {
            switch (state)
            {
                case TreatmentSubmitState.Saving: return "Saving treatment...";
                case TreatmentSubmitState.Saved: return "Saved: view it in the Lab";
                case TreatmentSubmitState.Failed: return "Retry: Update Treatment in Lab";
                case TreatmentSubmitState.Conflict: return "Save again over the newer result";
                case TreatmentSubmitState.Rejected: return "This assessment cannot be saved";
                default: return "Update Treatment in Lab";
            }
        }

        static string SeverityWord(float severity) => severity < 0.3f ? "Mild" : severity < 0.5f ? "Moderate" : "Severe";

        void RefreshAdjustment()
        {
            if (content == null)
            {
                view.ShowResults(false, null);
                view.ShowAdjustmentHint("Choose a case with manual treatment data.");
                view.HideRowsFrom(0);
                view.SetApply(null, false);
                view.ShowFeedback(lastFeedback, lastAccepted);
                return;
            }

            if (session != null && session.Stage == ManualStage.Completed)
            {
                var body = TreatmentResultManager.BuildResult(content, session.Record, session.AppliedAssessment());
                var sink = services.ResultSink;
                if (connected && sink != null)
                {
                    var state = sink.StateFor(session.Record);
                    if (!string.IsNullOrEmpty(sink.StatusText))
                        body = $"<color=#22D3EE>{sink.StatusText}</color>\n" + body;
                    view.ShowResults(true, body);
                    view.SetSubmit(SubmitLabel(state), state != TreatmentSubmitState.Saving && state != TreatmentSubmitState.Rejected);
                }
                else
                {
                    view.ShowResults(true, body);
                    view.SetSubmit(null, false);
                }

                return;
            }

            view.SetSubmit(null, false);
            view.ShowResults(false, null);
            var canAdjust = session != null && session.CanAdjust;
            view.ShowAdjustmentHint(session == null ? "Start a manual session on the Disease information panel." :
                session.Stage == ManualStage.Diagnosis ? "Review the disease information, then press 'Diagnosis reviewed'." :
                session.Stage == ManualStage.FinalAssessment ? "Final assessment: adjustments are closed." :
                "Set every level with - and +. Nothing is pre-selected; the recommended range is shown for learning.");

            var controls = content.Manual.Controls;
            var buttons = session?.Buttons;
            for (var i = 0; i < controls.Count && i < view.RowCount; i++)
            {
                var k = controls[i];
                var value = buttons != null ? buttons.ValueOf(k.ControlId) : k.Start;
                var touched = buttons != null && buttons.IsTouched(k.ControlId);
                var status = TreatmentValidationManager.StatusOf(k, value, touched);
                string statusText;
                Color color;
                switch (status)
                {
                    case ControlStatus.WithinRecommended: statusText = "Set: within the recommended range"; color = UIPalette.HealthGood; break;
                    case ControlStatus.BelowRecommended: statusText = "Set: below the recommended range"; color = UIPalette.Warning; break;
                    case ControlStatus.AboveRecommended: statusText = "Set: above the recommended range"; color = UIPalette.Warning; break;
                    default: statusText = k.Required ? "Not set yet (required): press - or +" : "Not set (optional)"; color = UIPalette.TextSecondary; break;
                }

                var f = (Func<float, string>)TreatmentButtonController.Format;
                view.ShowRow(i, k.Name, k.Description,
                    $"Current <b>{f(value)}</b>   Recommended {f(k.RecommendedMin)}-{f(k.RecommendedMax)}   Range {f(k.Min)}-{f(k.Max)}",
                    statusText, color, canAdjust, value > k.Min + 1e-4f || !touched, value < k.Max - 1e-4f || !touched);
            }

            view.HideRowsFrom(Mathf.Min(controls.Count, view.RowCount));
            if (controls.Count > view.RowCount)
                Debug.LogWarning($"[BTP] {content.Key} has {controls.Count} controls; the panel shows the first {view.RowCount}.");

            var pending = session != null && session.HasPendingChanges;
            var label = session == null || !session.HasApplied ? "Apply treatment" : pending ? "Re-apply adjusted treatment" : "Re-apply treatment";
            view.SetApply(label, session != null && session.CanApply);
            view.ShowFeedback(lastFeedback, lastAccepted);
        }

        // Derived from TreatmentTimer, so the labels always match the session length.
        static readonly string[] StageLabels = Enumerable.Range(1, TreatmentTimer.StageCount)
            .Select(i => $"{i}  {TreatmentSessionManager.StageName((ManualStage)i)}  {TreatmentTimer.WindowLabel((ManualStage)i)}").ToArray();

        void RefreshProgress()
        {
            if (session == null)
            {
                view.ShowClock($"{TreatmentTimer.Clock(0f)} / {TreatmentTimer.Clock(TreatmentTimer.SessionSeconds)}");
                view.ShowStages(0, StageLabels, null);
                view.ShowGuidance(content == null ? "" : learner == null ? "Choose or create a learner, then start a session." : $"Learner: {learner.Name}");
                view.SetStageButton(null, false, false);
                view.ShowHistory("");
                return;
            }

            var timer = session.Timer;
            view.ShowClock($"{TreatmentTimer.Clock(timer.Elapsed)} / {TreatmentTimer.Clock(TreatmentTimer.SessionSeconds)}   " +
                           $"<size=70%>{TreatmentTimer.Clock(timer.Remaining)} left{(timer.Paused && session.IsActive ? " (paused)" : "")}</size>");
            var done = new List<bool>();
            for (var s = ManualStage.Diagnosis; s <= ManualStage.FinalAssessment; s++)
                done.Add(session.Record.StagesCompleted.ContainsKey(s));
            view.ShowStages((int)session.Stage, StageLabels, done);

            var suggested = timer.GuidanceStage;
            string guidance;
            if (session.Stage == ManualStage.Completed)
                guidance = $"Session complete. Learner: {learner?.Name}";
            else if (session.Stage < suggested)
                guidance = $"Suggested now: {TreatmentSessionManager.StageName(suggested)}. Stages move on when you act.";
            else
                guidance = $"Stage {(int)session.Stage} of 5: {TreatmentSessionManager.StageName(session.Stage)}. Learner: {learner?.Name}";
            view.ShowGuidance(guidance);

            switch (session.Stage)
            {
                case ManualStage.Application: view.SetStageButton("Continue to monitoring", true, true); break;
                case ManualStage.Monitoring: view.SetStageButton("Proceed to final assessment", true, true); break;
                case ManualStage.FinalAssessment: view.SetStageButton("Finish session", true, true); break;
                default: view.SetStageButton(null, false, false); break;
            }

            var sb = new StringBuilder();
            // Repeated refused presses show once with a count; the database keeps every action.
            foreach (var (a, repeats) in session.RecentHistory(6))
                sb.AppendLine($"<color={(a.Accepted ? "#B8C7D4" : "#FBBF24")}>{a.Utc.ToLocalTime():HH:mm:ss}</color>  {a.Message}" +
                              (repeats > 1 ? $" <color=#FBBF24>\u00D7{repeats}</color>" : ""));
            view.ShowHistory(sb.ToString());
        }

        void RefreshHealth()
        {
            if (content == null)
            {
                view.ShowHealth(0f, 0f);
                view.SetCheckHealth(false, "");
                return;
            }

            var state = recovery.Monitor != null ? recovery.Monitor.Current : session != null ? session.Target : LeafRecoveryState.Initial(content.Manual);
            view.ShowHealth(state.Health, state.Severity);
            var monitoring = session != null && session.Stage == ManualStage.Monitoring;
            view.SetCheckHealth(monitoring, monitoring ? $"Health checks: {session.Record.HealthChecks}. {TreatmentResultManager.Disclaimer}" :
                TreatmentResultManager.Disclaimer);
        }
    }
}
