using System.IO;
using System.Linq;
using System.Text;
using BTP.Core;
using BTP.Farm;
using BTP.Lab;
using BTP.Treatment;
using BTP.UI;
using BTP.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Batch Play-mode check of the after-treatment story (<see cref="RecoveryFlow"/>) through the real scenes: plays
    /// XRBootstrap, picks a Farm disease-row plant, enters the Treatment lab, reports a successful treatment (the pop-up must
    /// say "Treatment applied. Return to the Lab to see the weekly changes."), presses Go to Lab, steps the Lab timeline to
    /// Week 6 (no Farm button before), presses "See changes in the Farm" and checks the Farm plant is healed, highlighted and
    /// next to the user. Batch: -executeMethod BTP.Editor.RecoveryFlowProbe.Run (no -quit, no -nographics); report in
    /// Tools/logs/recovery_flow_probe.txt, exit code 1 when a check fails.
    /// </summary>
    [InitializeOnLoad]
    public static class RecoveryFlowProbe
    {
        const string Key = "BTP.RecoveryFlowProbe";
        const string OutPath = "Tools/logs/recovery_flow_probe.txt";
        static int frame, stage, failures, week;
        static float stageTime, startTime;
        static SpecimenRef picked;
        static readonly StringBuilder Log = new StringBuilder();

        static RecoveryFlowProbe()
        {
            if (SessionState.GetInt(Key, 0) != 0)
                EditorApplication.update += Tick;
        }

        public static void Run()
        {
            File.WriteAllText(OutPath, "recovery flow probe started\n");
            SessionState.SetInt(Key, 1);
            EditorSceneManager.OpenScene("Assets/Scenes/XRBootstrap.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void Line(string s)
        {
            Log.AppendLine(s);
            File.WriteAllText(OutPath, Log.ToString());
        }

        static void Check(bool ok, string what)
        {
            if (!ok)
                failures++;
            Line((ok ? "  PASS " : "  FAIL ") + what);
        }

        static void Next()
        {
            stage++;
            stageTime = Time.realtimeSinceStartup;
        }

        /// <summary>_DiseaseAmount of every Farm recovery material under <paramref name="plant"/> (near mesh and billboard LODs).</summary>
        static System.Collections.Generic.IEnumerable<float> Amounts(Transform plant)
        {
            foreach (var renderer in plant.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.HasProperty("_DiseaseAmount"))
                        yield return material.GetFloat("_DiseaseAmount");
        }

        static bool HasSavedResult(SelectablePlant row) =>
            Enumerable.Range(0, row.Plants.Length).Any(i => SpecimenTreatmentResults.Load(row.SpecimenAt(i).SpecimenId) != null);

        /// <summary>
        /// Clears the results this probe's runs saved to the Editor's progress database (healed tomato row plants, "Managed" /
        /// "Recovered", saved in the last two days), so the Editor Farm does not keep showing them healed.
        /// </summary>
        static void ClearProbeResults(SelectablePlant[] plantings)
        {
            var store = SpecimenTreatmentResults.Store;
            if (store == null)
                return;
            var cleared = 0;
            foreach (var row in plantings.Where(p => p.IsRow && p.PlantId == "tomato"))
                for (var i = 0; i < row.Plants.Length; i++)
                {
                    var record = SpecimenTreatmentResults.Load(row.SpecimenAt(i).SpecimenId);
                    if (record == null || record.outcome != "Managed" || record.diseaseAmount > 0.01f ||
                        !System.DateTime.TryParse(record.savedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ||
                        (System.DateTime.UtcNow - when.ToUniversalTime()).TotalDays > 2)
                        continue;
                    store.SetSetting(SpecimenTreatmentResults.Key(record.specimenId), "");
                    cleared++;
                }

            Line($"cleared {cleared} probe result(s) from the Editor progress database");
        }

        static bool Ready(EnvironmentId id)
        {
            var environment = EnvironmentController.Instance;
            return environment != null && !environment.IsSwitching && environment.Current == id;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (frame > 0)
                {
                    SessionState.SetInt(Key, 0);
                    EditorApplication.update -= Tick;
                    Line(failures == 0 ? "RESULT: all checks passed" : $"RESULT: {failures} check(s) FAILED");
                    EditorApplication.Exit(failures == 0 ? 0 : 1);
                }
                return;
            }

            if (frame++ == 0)
                startTime = stageTime = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - startTime > 900f)
            {
                Line($"TIMEOUT at stage {stage}");
                failures++;
                EditorApplication.ExitPlaymode();
                return;
            }

            var step = Time.realtimeSinceStartup - stageTime;
            var environment = EnvironmentController.Instance;
            switch (stage)
            {
                case 0:
                    if (!Ready(EnvironmentId.Farm) || step < 1f)
                        return;
                    var row = Object.FindObjectsByType<SelectablePlant>(FindObjectsInactive.Exclude).Where(p => p.IsRow && p.Plants.Length > 1)
                        .OrderBy(p => p.PlantId == "tomato" ? 0 : 1).ThenBy(p => p.DiseaseId).FirstOrDefault();
                    if (row == null)
                    {
                        Check(false, "the Farm has a disease row");
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    picked = row.SpecimenAt(1);
                    Check(SelectionState.Instance.SelectSpecimen(picked), $"picked Farm plant {picked.SpecimenId}");
                    environment.EnterTreatment();
                    Next();
                    return;
                case 1:
                    if (!Ready(EnvironmentId.Treatment) || step < 1f)
                        return;
                    var station = Object.FindAnyObjectByType<ChamberStation>();
                    TreatmentFlowPrompt.Applied(station, picked.PlantId, picked.DiseaseId, picked, 1f, 1f, "Managed", true);
                    var popup = FlowPopup.Current;
                    Check(RecoveryFlow.Stage == RecoveryFlowStage.LabPending && RecoveryFlow.Specimen.Equals(picked), "a successful treatment starts the story (Lab pending)");
                    Check(popup != null && popup.Title == "Treatment applied" && popup.Body == "Return to the Lab to see the weekly changes." &&
                          popup.ChoiceLabel(0) == "Go to Lab" && popup.ChoiceLabel(1) == "Close", "the Treatment pop-up says Return to the Lab, with Go to Lab and Close");
                    Check(popup != null && popup.gameObject.scene == station.gameObject.scene, "the pop-up belongs to the Treatment lab scene");
                    Next();
                    return;
                case 2:
                    if (step < 0.5f)
                        return;
                    var head = Camera.main.transform;
                    var shown = FlowPopup.Current;
                    var distance = shown != null ? Vector3.Distance(head.position, shown.transform.position) : -1f;
                    Check(shown != null && distance > 0.6f && distance < 1.2f && Vector3.Angle(head.forward, shown.transform.position - head.position) < 30f,
                        $"the pop-up faces the user at a comfortable distance ({distance:0.00} m)");
                    shown?.Press(0);
                    Next();
                    return;
                case 3:
                    if (!Ready(EnvironmentId.Lab) || step < 1f)
                        return;
                    var panel = Object.FindAnyObjectByType<RecoveryDemoPanel>();
                    var display = Object.FindAnyObjectByType<LabPlantDisplay>();
                    Check(display.CurrentPlant.PlantId == picked.PlantId && display.CurrentDiseaseId == picked.DiseaseId, "Go to Lab shows the treated case's leaf");
                    Check(panel.CurrentWeek == RecoveryFlow.FirstWeek, "the Lab starts at Week 1 (as treated)");
                    Check(FlowPopup.Current != null && FlowPopup.Current.Title == RecoveryDemoPanel.StoryIntroTitle, "the Lab explains the weekly steps");
                    FlowPopup.Current?.Press(0);
                    week = 1;
                    Next();
                    return;
                case 4:
                {
                    // Step the weeks with the timeline (3.6 s per week so each 3 s leaf transition finishes).
                    if (step < 3.6f)
                        return;
                    var timelinePanel = Object.FindAnyObjectByType<RecoveryDemoPanel>();
                    if (week < 6)
                    {
                        Check(!RecoveryFlow.FarmUnlocked && FlowPopup.Current == null, $"no Farm button at Week {week}");
                        week++;
                        timelinePanel.SelectWeek(week);
                        stageTime = Time.realtimeSinceStartup;
                        return;
                    }

                    Check(RecoveryFlow.Stage == RecoveryFlowStage.WeeksViewed, "Week 6 completes the Lab part");
                    var farmPrompt = FlowPopup.Current;
                    Check(farmPrompt != null && farmPrompt.ChoiceLabel(0) == RecoveryDemoPanel.SeeFarmLabel, "after Week 6: the 'See changes in the Farm' pop-up button");
                    farmPrompt?.Press(0);
                    Next();
                    return;
                }
                case 5:
                {
                    // The label comes up once the view has faded in (game time); give slow batch frames up to 15 s.
                    if (!Ready(EnvironmentId.Farm) || step < 1.5f ||
                        (step < 15f && (RecoveredPlantHighlight.Current == null || !RecoveredPlantHighlight.Current.LabelShowing)))
                        return;
                    var planting = Object.FindObjectsByType<SelectablePlant>(FindObjectsInactive.Exclude).FirstOrDefault(p => RecoveredPlantHighlight.IndexOf(p, picked) >= 0);
                    var plant = planting != null ? planting.PlantAt(RecoveredPlantHighlight.IndexOf(planting, picked)) : null;
                    Check(plant != null, "the treated plant is found in the Farm");
                    if (plant == null)
                    {
                        EditorApplication.ExitPlaymode();
                        return;
                    }

                    Check(RecoveryFlow.Stage == RecoveryFlowStage.FarmHealed, "the Farm marks the story healed");
                    var pass = RecoveredPlantHighlight.Current;
                    Check(pass != null && pass.Focus == plant, "the healing pass runs, with the treated plant as its focus");
                    Check(pass != null && pass.LabelShowing, "the treated plant is highlighted (Recovered label)");
                    Line($"healing pass: {pass?.PlantCount} plants, {pass?.Healed.Count} healed objects");
                    Check(SelectionState.Instance.SelectedSpecimen != null && SelectionState.Instance.SelectedSpecimen.SpecimenId == picked.SpecimenId,
                        "the plant is selected (ring and name card)");
                    var eye = Camera.main.transform.position;
                    var flat = new Vector2(eye.x - plant.position.x, eye.z - plant.position.z).magnitude;
                    Check(flat < 3f, $"the user stands beside the plant ({flat:0.00} m)");
                    Next();
                    return;
                }
                case 6:
                {
                    if (step < 4f)
                        return;
                    // Every plant with the treated crop and disease (the whole row, near meshes and billboard LODs) is healthy.
                    var plantings = Object.FindObjectsByType<SelectablePlant>(FindObjectsInactive.Include);
                    var rows = plantings.Where(p => p.IsRow && p.PlantId == picked.PlantId && p.DiseaseId == picked.DiseaseId).ToList();
                    var plants = rows.SelectMany(r => r.Plants).Where(t => t != null).ToList();
                    var healed = 0;
                    var renderers = 0;
                    foreach (var plant in plants)
                    {
                        var visual = plant.GetComponent<FarmSpecimenVisual>();
                        var amounts = Amounts(plant).ToList();
                        renderers += amounts.Count;
                        if (visual != null && visual.CurrentAmount < 0.01f && amounts.Count > 0 && amounts.All(a => a < 0.01f))
                            healed++;
                    }

                    Line($"{rows.Count} row(s) of {picked.PlantId}/{picked.DiseaseId}: {healed}/{plants.Count} plants healthy ({renderers} recovery materials incl. billboards)");
                    Check(plants.Count > 1 && healed == plants.Count, "EVERY plant with the treated disease is healthy (near meshes and billboards)");

                    // Other diseases, of the same crop first, are untouched.
                    // Rows with no saved result of their own (a saved result shows on its plant by design).
                    var others = plantings.Where(p => p.IsRow && p.DiseaseId != picked.DiseaseId && !HasSavedResult(p))
                        .OrderBy(p => p.PlantId == picked.PlantId ? 0 : 1).Take(3).ToList();
                    var untouched = others.SelectMany(r => r.Plants).Where(t => t != null).All(t => t.GetComponent<FarmSpecimenVisual>() == null && Amounts(t).All(a => a > 0.5f));
                    Line($"checked {others.Count} other rows: {string.Join(", ", others.Select(o => o.PlantId + "/" + o.DiseaseId))}");
                    Check(others.Count > 0 && untouched, "plants with other diseases (same crop too) stay diseased");

                    var saved = rows.SelectMany(r => Enumerable.Range(0, r.Plants.Length).Select(r.SpecimenAt)).All(sp => SpecimenTreatmentResults.Load(sp.SpecimenId)?.diseaseAmount < 0.01f);
                    Check(saved, "each healed plant's result is kept (specimen results)");
                    ClearProbeResults(plantings);
                    stage = 99;
                    EditorApplication.ExitPlaymode();
                    return;
                }
            }
        }
    }
}
