using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTP.Plants;
using BTP.Treatment;
using NUnit.Framework;
using SQLite;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Tests
{
    /// <summary>
    /// Button-only Treatment lab: every task of every case can be completed with the action-panel buttons
    /// (<see cref="TreatmentButtonPlanner"/>), wrong choices are explained without progress, and the scene shows the
    /// Manual treatment panels (the old task windows were removed). Uses the real SQL content in an in-memory SQLite database.
    /// </summary>
    public sealed class TreatmentButtonTests
    {
        const string SqlPath = "Assets/_Project/Data/Treatment/treatment_content.sql";
        const string ScenePath = "Assets/Scenes/Treatment.unity";

        static string sql;
        SqliteTreatmentRepository repository;

        [OneTimeSetUp]
        public void LoadSql() => sql = File.ReadAllText(SqlPath);

        [SetUp]
        public void Open()
        {
            repository = new SqliteTreatmentRepository(() =>
            {
                var db = new SQLiteConnection(":memory:");
                db.ExecuteScript(sql);
                return db;
            });
            repository.InitializeAsync().Wait();
            Assert.IsTrue(repository.IsReady, repository.Error);
        }

        [TearDown]
        public void Close() => repository.Dispose();

        IEnumerable<(TreatmentCase content, int route)> CasesAndRoutes()
        {
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = repository.GetCase(new CaseKey(info.PlantId, info.DiseaseId), out var error);
                Assert.IsNotNull(c, error);
                var routes = c.Steps.SelectMany(s => s.Options).Where(o => o.Correct && o.Route != null).Select(o => o.Route).Distinct().Count();
                for (var route = 0; route < System.Math.Max(1, routes); route++)
                    yield return (c, route);
            }
        }

        string ToolName(string key) => repository.GetTool(key)?.Name;

        /// <summary>The button a learner who knows the answer presses for the current step.</summary>
        static TreatmentButtonAction CorrectButton(TreatmentSession session, List<TreatmentButtonAction> buttons, int route)
        {
            var step = session.CurrentStep;
            switch (step.Action)
            {
                case TreatmentActions.Choose:
                    var correct = step.Options.Where(o => o.Correct).ToList();
                    var option = correct[System.Math.Min(route, correct.Count - 1)];
                    return buttons.Single(b => b.Kind == TreatmentButtonKind.Choose && b.Context.OptionId == option.OptionId);
                case TreatmentActions.Advance:
                    return buttons.Single(b => b.Kind == TreatmentButtonKind.Advance);
                default:
                    return buttons.Single(b => b.Kind == TreatmentButtonKind.UseOnTarget && b.Context.TargetTag == step.TargetTag &&
                                               (string.IsNullOrEmpty(step.ToolKey) || b.ToolKey == step.ToolKey));
            }
        }

        /// <summary>Wrong buttons for the current step: wrong options and wrong tools on the step's target.</summary>
        static IEnumerable<TreatmentButtonAction> WrongButtons(TreatmentSession session, List<TreatmentButtonAction> buttons)
        {
            var step = session.CurrentStep;
            if (step.Action == TreatmentActions.Choose)
                return buttons.Where(b => !step.Options.First(o => o.OptionId == b.Context.OptionId).Correct);
            if (!string.IsNullOrEmpty(step.ToolKey))
                return buttons.Where(b => b.ToolKey != step.ToolKey);
            return Enumerable.Empty<TreatmentButtonAction>();
        }

        static string Snapshot(CaseProgress p) =>
            string.Join(",", p.CompletedSteps.OrderBy(x => x)) + "|" + string.Join(",", p.StepTargets.SelectMany(x => x.Value.Select(v => x.Key + ":" + v)).OrderBy(x => x)) +
            "|" + string.Join(",", p.TargetStates.Select(x => x.Key + "=" + x.Value).OrderBy(x => x)) + "|" + p.Route + "|" +
            string.Join(",", p.Equipment.Select(x => x.Key + "=" + x.Value).OrderBy(x => x)) + "|" + p.Completed;

        [Test]
        public void EveryTaskOfEveryCaseCanBeCompletedWithButtonsAlone()
        {
            var lessons = 0;
            foreach (var (c, route) in CasesAndRoutes())
            {
                var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
                for (var guard = 0; guard < 120 && session.CurrentStep != null; guard++)
                {
                    var step = session.CurrentStep;
                    var buttons = TreatmentButtonPlanner.Plan(session, ToolName);
                    Assert.IsNotEmpty(buttons, $"{c.Key} step {step.StepId} has no buttons.");
                    Assert.LessOrEqual(buttons.Count, TreatmentButtonPlanner.MaxButtons, $"{c.Key} step {step.StepId}");
                    Assert.IsTrue(buttons.All(b => !string.IsNullOrEmpty(b.Label)), $"{c.Key} step {step.StepId} has an unlabelled button.");
                    Assert.IsFalse(buttons.Any(b => b.Label.ToLowerInvariant().Contains("grab") || b.Label.ToLowerInvariant().Contains("pick up")),
                        $"{c.Key} step {step.StepId}: a button asks to grab something.");
                    if (!string.IsNullOrEmpty(step.ToolKey))
                        Assert.AreEqual(c.RackTools.Count, buttons.Count, $"{c.Key} step {step.StepId}: one button per rack tool.");

                    var result = session.TryPerform(CorrectButton(session, buttons, route).Context);
                    Assert.IsTrue(result.Accepted, $"{c.Key} step {step.StepId} was refused: {result.Message}");
                }

                Assert.IsTrue(session.Progress.Completed, $"{c.Key} route {route} did not finish with buttons.");
                Assert.AreEqual(session.TotalSteps, session.CompletedSteps, $"{c.Key}: Task N of N complete");
                Assert.AreEqual(c.Outcome, session.Progress.Outcome);
                Assert.AreEqual(0, session.CarriedTargets.Count, $"{c.Key} finished while still carrying removed material.");
                var done = TreatmentButtonPlanner.Plan(session, ToolName);
                Assert.AreEqual(TreatmentButtonKind.PractiseAgain, done.Single().Kind);
                lessons++;
            }

            Assert.GreaterOrEqual(lessons, 58);
        }

        [Test]
        public void WrongChoiceAndWrongToolButtonsGiveFeedbackAndDoNotAdvance()
        {
            var wrongPresses = 0;
            foreach (var (c, route) in CasesAndRoutes())
            {
                var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
                for (var guard = 0; guard < 120 && session.CurrentStep != null; guard++)
                {
                    var step = session.CurrentStep;
                    var buttons = TreatmentButtonPlanner.Plan(session, ToolName);
                    foreach (var wrong in WrongButtons(session, buttons))
                    {
                        var before = Snapshot(session.Progress);
                        var result = session.TryPerform(wrong.Context);
                        Assert.IsFalse(result.Accepted, $"{c.Key} step {step.StepId}: wrong button '{wrong.Label}' was accepted.");
                        Assert.IsNotEmpty(result.Message, $"{c.Key} step {step.StepId}: '{wrong.Label}' gave no feedback.");
                        Assert.AreEqual(before, Snapshot(session.Progress), $"{c.Key} step {step.StepId}: '{wrong.Label}' changed progress.");
                        Assert.AreSame(step, session.CurrentStep);
                        wrongPresses++;
                    }

                    Assert.IsTrue(session.TryPerform(CorrectButton(session, buttons, route).Context).Accepted);
                }
            }

            Assert.Greater(wrongPresses, 100);
        }

        [Test]
        public void WrongToolButtonGivesTheSameFeedbackAsHoldingTheWrongTool()
        {
            var c = repository.GetCase(new CaseKey("tomato", "early_blight"), out _);
            var session = new TreatmentSession(c, new CaseProgress(c.Key, c.ContentVersion));
            var buttons = TreatmentButtonPlanner.Plan(session, ToolName);
            Assert.AreEqual("Inspect with magnifier", buttons.Single(b => b.ToolKey == "magnifier").Label);
            var shears = buttons.Single(b => b.ToolKey == "shears");
            Assert.AreEqual("Use the magnifier for this task.", session.TryPerform(shears.Context).Message);
            Assert.AreEqual(0, session.CompletedSteps);
        }

        [Test]
        public void NoTaskRequiresAGrabOrPickUpInstruction()
        {
            foreach (var info in DiseaseClasses.AllCases)
            {
                var c = repository.GetCase(new CaseKey(info.PlantId, info.DiseaseId), out _);
                foreach (var step in c.Steps)
                {
                    // The lesson engine has no grab action: scene actions are tool + target selections, which buttons send too.
                    Assert.IsTrue(TreatmentActions.All.Contains(step.Action));
                    if (!string.IsNullOrEmpty(step.ToolKey))
                        Assert.IsTrue(TreatmentButtonPlanner.HasToolVerb(step.ToolKey), $"{c.Key} step {step.StepId}: tool '{step.ToolKey}' has no button label.");
                    foreach (var text in new[] { step.Title, step.Instruction, step.SuccessText, step.WrongToolFeedback, step.WrongTargetFeedback, step.PrerequisiteFeedback })
                    {
                        if (string.IsNullOrEmpty(text))
                            continue;
                        var lower = text.ToLowerInvariant();
                        foreach (var phrase in new[] { "grab", "pick up", "carry it", "take the removed", "go to the" })
                            Assert.IsFalse(lower.Contains(phrase), $"{c.Key} step {step.StepId} tells the learner to '{phrase}': {text}");
                    }
                }

                foreach (var tool in c.RackTools)
                    Assert.IsTrue(TreatmentButtonPlanner.HasToolVerb(tool), $"{c.Key}: rack tool '{tool}' has no button label.");
            }
        }

        /// <summary>
        /// The step-lesson task and case-notes windows were removed from the lab: the scene's UI is the Manual treatment
        /// panels, each a world-space canvas reachable by ray, poke and mouse, with buttons large enough to hit.
        /// </summary>
        [Test]
        public void TreatmentSceneShowsOnlyTheManualTreatmentPanels()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var transforms = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
                Assert.IsEmpty(roots.SelectMany(r => r.GetComponentsInChildren<TreatmentPanel>(true)), "the old task panel is gone");
                foreach (var old in new[] { "TaskPanel", "CaseNotesPanel", "ResetCase", "Mode_Step", "Mode_Manual" })
                    Assert.IsFalse(transforms.Any(t => t.name == old), $"old window '{old}' is still in the scene");

                var controller = roots.SelectMany(r => r.GetComponentsInChildren<TreatmentController>(true)).Single();
                Assert.IsNull(controller.Panel);
                var manual = roots.SelectMany(r => r.GetComponentsInChildren<ManualTreatmentController>(true)).Single();
                Assert.IsNotNull(manual);
                Assert.IsEmpty(roots.SelectMany(r => r.GetComponentsInChildren<XRGrabInteractable>(true)), "nothing in the lab needs grabbing");

                foreach (var name in new[] { "CaseSelectorPanel", "DiseaseInfoPanel", "TreatmentAdjustmentPanel", "TreatmentProgressPanel", "LeafHealthPanel",
                             "FinalResultsPanel", "NavigationPanel" })
                {
                    var panel = transforms.SingleOrDefault(t => t.name == name);
                    Assert.IsNotNull(panel, name);
                    var canvas = panel.GetComponent<Canvas>();
                    Assert.IsNotNull(canvas, name);
                    Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode, name);
                    Assert.IsNotNull(panel.GetComponent<TrackedDeviceGraphicRaycaster>(), $"{name}: ray, poke and mouse reach the buttons");
                    foreach (var button in panel.GetComponentsInChildren<Button>(true).Where(b => b.targetGraphic.raycastTarget && !b.name.StartsWith("Stage")))
                        Assert.GreaterOrEqual(((RectTransform)button.transform).rect.height, 52f, $"{name}/{button.name}");
                }

                var adjustment = transforms.Single(t => t.name == "TreatmentAdjustmentPanel");
                Assert.AreEqual(5, adjustment.GetComponentsInChildren<Button>(true).Count(b => b.name == "Plus"));
                Assert.AreEqual(5, adjustment.GetComponentsInChildren<Button>(true).Count(b => b.name == "Minus"));
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
