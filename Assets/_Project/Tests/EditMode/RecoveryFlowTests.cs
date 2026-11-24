using BTP.Core;
using BTP.Lab;
using BTP.Treatment;
using BTP.UI;
using NUnit.Framework;
using UnityEngine;

namespace BTP.Tests
{
    /// <summary>The after-treatment story: Treatment applied → Lab weeks → Farm healed (<see cref="RecoveryFlow"/>).</summary>
    public class RecoveryFlowTests
    {
        static readonly SpecimenRef Specimen = new SpecimenRef(null, "tomato", "early_blight", 2);

        [SetUp]
        public void SetUp() => RecoveryFlow.Clear();

        [TearDown]
        public void TearDown()
        {
            RecoveryFlow.Clear();
            if (FlowPopup.Current != null)
                FlowPopup.Current.Close();
        }

        [Test]
        public void Story_GoesFromAppliedToLabWeeksToFarmHealed()
        {
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.None));
            Assert.That(RecoveryFlow.FarmShown(), Is.False, "nothing to show before a treatment");

            Assert.That(RecoveryFlow.TreatmentApplied("tomato", "early_blight", Specimen, 0.9f, 1f, "Managed"), Is.True);
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.LabPending));
            Assert.That(RecoveryFlow.IsFor("tomato", "early_blight"), Is.True);
            Assert.That(RecoveryFlow.FarmUnlocked, Is.False, "the Farm button waits for Week 6");

            for (var week = 1; week < 6; week++)
                Assert.That(RecoveryFlow.WeekReached("tomato", "early_blight", week, 6), Is.False, "week " + week);
            Assert.That(RecoveryFlow.FarmShown(), Is.False, "the Farm does not heal before the six weeks were seen");
            Assert.That(RecoveryFlow.WeekReached("potato", "late_blight", 6, 6), Is.False, "another case's Week 6 does not count");
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.LabPending));

            Assert.That(RecoveryFlow.WeekReached("tomato", "early_blight", 6, 6), Is.True, "Week 6 unlocks the Farm once");
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.WeeksViewed));
            Assert.That(RecoveryFlow.FarmUnlocked, Is.True);
            Assert.That(RecoveryFlow.WeekReached("tomato", "early_blight", 6, 6), Is.False, "only the first time");
            Assert.That(RecoveryFlow.HighestWeek, Is.EqualTo(6));

            Assert.That(RecoveryFlow.FarmShown(), Is.True, "the Farm heals the plant (and highlights it) once");
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.FarmHealed));
            Assert.That(RecoveryFlow.FarmShown(), Is.False);
            Assert.That(RecoveryFlow.FarmUnlocked, Is.True, "it stays healed for the session");
            Assert.That(RecoveryFlow.FinalDiseaseAmount, Is.EqualTo(0f));
        }

        [Test]
        public void RepeatedReport_KeepsProgress_NewTreatmentStartsOver()
        {
            RecoveryFlow.TreatmentApplied("tomato", "early_blight", Specimen, 1f, 1f, "Managed");
            var generation = RecoveryFlow.Generation;
            RecoveryFlow.WeekReached("tomato", "early_blight", 3, 6);
            Assert.That(RecoveryFlow.TreatmentApplied("tomato", "early_blight", Specimen, 1f, 1f, "Managed"), Is.False, "same treatment again");
            Assert.That(RecoveryFlow.Generation, Is.EqualTo(generation));
            Assert.That(RecoveryFlow.HighestWeek, Is.EqualTo(3));

            RecoveryFlow.WeekReached("tomato", "early_blight", 6, 6);
            Assert.That(RecoveryFlow.TreatmentApplied("potato", "late_blight", null, 1f, 0.5f, "PartlyManaged"), Is.True, "a new treatment");
            Assert.That(RecoveryFlow.Generation, Is.EqualTo(generation + 1));
            Assert.That(RecoveryFlow.Stage, Is.EqualTo(RecoveryFlowStage.LabPending));
            Assert.That(RecoveryFlow.IsFor("tomato", "early_blight"), Is.False);
            Assert.That(RecoveryFlow.HighestWeek, Is.EqualTo(RecoveryFlow.FirstWeek));
            Assert.That(RecoveryFlow.TreatmentApplied("", "x", null, 1f, 1f, "Managed"), Is.False, "no case, no story");
        }

        [Test]
        public void Outcomes_DriveTheHealing_FailuresStartNoStory()
        {
            Assert.That(RecoveryFlow.HealedFor("Managed"), Is.EqualTo(1f));
            Assert.That(RecoveryFlow.HealedFor("PestSuppressed"), Is.EqualTo(1f));
            Assert.That(RecoveryFlow.HealedFor("Contained"), Is.EqualTo(1f));
            Assert.That(RecoveryFlow.HealedFor("PartlyManaged"), Is.EqualTo(0.5f));
            foreach (var failed in new[] { "NotControlled", "NotContained", "PoorlyContained", "NotTreated", null, "" })
                Assert.That(RecoveryFlow.HealedFor(failed), Is.Null, failed ?? "null");

            RecoveryFlow.TreatmentApplied("tomato", "early_blight", Specimen, 0.8f, 0.5f, "PartlyManaged");
            Assert.That(RecoveryFlow.FullyHealed, Is.False);
            Assert.That(RecoveryFlow.FinalDiseaseAmount, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(RecoveryFlow.LeafHealing("tomato", "early_blight"), Is.EqualTo(0.5f));
            Assert.That(RecoveryFlow.LeafHealing("potato", "late_blight"), Is.EqualTo(1f), "other cases keep the full demo");
            Assert.That(TreatmentFlowPrompt.AppliedText(), Does.Contain(TreatmentFlowPrompt.PartlyNote));
        }

        [Test]
        public void PartlyEffectiveTreatment_HealsTheLeafPartly()
        {
            var root = new GameObject("Partial leaf");
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var visual = root.AddComponent<LeafRecoveryVisual>();
                visual.Show(data.GetWeek(6), data.GetWeek(1), data.GetWeek(6), root.transform, healing: 0.5f);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0.5f).Within(1e-5f));
                Assert.That(visual.DisplayedBaselineRecovery, Is.EqualTo(0.5f).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }

        [Test]
        public void LabCaptions_CountTheWeeksAfterTheTreatment()
        {
            RecoveryFlow.TreatmentApplied("tomato", "early_blight", Specimen, 1f, 1f, "Managed");
            Assert.That(RecoveryDemoPanel.StoryCaption(1, 6), Does.StartWith("As treated"));
            Assert.That(RecoveryDemoPanel.StoryCaption(2, 6), Is.EqualTo("1 week after your treatment"));
            Assert.That(RecoveryDemoPanel.StoryCaption(5, 6), Is.EqualTo("4 weeks after your treatment"));
            Assert.That(RecoveryDemoPanel.StoryCaption(6, 6), Is.EqualTo("Recovered after your treatment"));
        }

        [Test]
        public void StorySpecimen_IsTheCarriedPlant_ElseTheRowsFirstPlant()
        {
            Assert.That(TreatmentFlowPrompt.StorySpecimen(Specimen, "tomato", "early_blight"), Is.SameAs(Specimen));
            var fallback = TreatmentFlowPrompt.StorySpecimen(null, "tomato", "early_blight");
            Assert.That(fallback, Is.Not.Null);
            Assert.That(fallback.IsCase("tomato", "early_blight") && fallback.PlantIndex == 0, Is.True);
        }

        [Test]
        public void FarmHealing_TakesEveryRendererOfTheDisease_NeverAnotherDiseases()
        {
            var shader = Shader.Find("BTP/Farm Recovery");
            Assert.That(shader, Is.Not.Null);
            var diseaseLeaf = new Material(shader) { name = "Tomato_septoria Leaf" };
            var diseaseBillboard = new Material(shader) { name = "Tomato_septoria Billboard" };
            var sharedStem = new Material(shader) { name = "Tomato Stem" };
            var otherLeaf = new Material(shader) { name = "Tomato_blight Leaf" };
            var copy = new Material(shader) { name = "Tomato_septoria Leaf (specimen)" };
            var plain = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Hidden/InternalErrorShader")) { name = "Label" };
            var objects = new System.Collections.Generic.List<Object> { diseaseLeaf, diseaseBillboard, sharedStem, otherLeaf, copy, plain };
            try
            {
                Renderer Make(params Material[] materials)
                {
                    var go = new GameObject("Renderer");
                    objects.Add(go);
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = materials;
                    return renderer;
                }

                // The treated row: near meshes and billboard LODs; another disease of the same crop shares only the stem.
                var treated = new[] { Make(diseaseLeaf, sharedStem), Make(diseaseBillboard), Make(copy, plain) };
                var others = new[] { Make(otherLeaf, sharedStem) };
                var own = BTP.Farm.RecoveredPlantHighlight.DiseaseMaterials(treated, others);
                Assert.That(own, Is.EquivalentTo(new[] { diseaseLeaf, diseaseBillboard }),
                    "the disease's own leaf and billboard materials; never a material another disease uses, a single plant's copy or a non-recovery material");
                Assert.That(BTP.Farm.RecoveredPlantHighlight.LabelLine(4, true), Is.EqualTo("4 plants healthy after your treatment"));
                Assert.That(BTP.Farm.RecoveredPlantHighlight.LabelLine(1, false), Is.EqualTo("1 plant partly recovered after your treatment"));
            }
            finally
            {
                foreach (var o in objects)
                    Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void Popup_ShowsTheMessage_PressRunsTheActionAndCloses()
        {
            var pressed = 0;
            var first = FlowPopup.Show(default, TreatmentFlowPrompt.AppliedTitle, TreatmentFlowPrompt.AppliedBody, UIPalette.HealthGood,
                new FlowPopup.Choice(TreatmentFlowPrompt.GoToLabLabel, () => pressed++), new FlowPopup.Choice(TreatmentFlowPrompt.CloseLabel, null, false));
            Assert.That(FlowPopup.Current, Is.SameAs(first));
            Assert.That(first.Title, Is.EqualTo("Treatment applied"));
            Assert.That(first.Body, Is.EqualTo("Return to the Lab to see the weekly changes."));
            Assert.That(first.ChoiceCount, Is.EqualTo(2));
            Assert.That(first.ChoiceLabel(0), Is.EqualTo("Go to Lab"));
            Assert.That(first.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(), Is.Not.Null, "XR rays can press it");

            var second = FlowPopup.Show(default, "Week 6: recovered", "See the same plant in the Farm.", UIPalette.HealthGood,
                new FlowPopup.Choice(RecoveryDemoPanel.SeeFarmLabel, () => pressed += 10));
            Assert.That(first == null, Is.True, "a new pop-up replaces the last");
            Assert.That(second.Press(0), Is.True);
            Assert.That(pressed, Is.EqualTo(10));
            Assert.That(FlowPopup.Current, Is.Null, "pressing closes it");
        }
    }
}
