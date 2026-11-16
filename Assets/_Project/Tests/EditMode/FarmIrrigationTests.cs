using System.Collections.Generic;
using System.Linq;
using BTP.Farm;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Tests
{
    public class FarmIrrigationTests
    {
        const string FarmScenePath = "Assets/Scenes/Envirornment.unity";

        static T WithFarmScene<T>(System.Func<Scene, T> body)
        {
            var scene = SceneManager.GetSceneByPath(FarmScenePath);
            var opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(FarmScenePath, OpenSceneMode.Additive);
            try
            {
                return body(scene);
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static List<FarmIrrigationController> Controllers(Scene scene) =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FarmIrrigationController>(true)).ToList();

        [Test]
        public void FarmScene_HasOneIrrigationController_WithOneToggleButtonWiredToIt()
        {
            WithFarmScene(scene =>
            {
                var controllers = Controllers(scene);
                Assert.That(controllers.Count, Is.EqualTo(1), "one irrigation controller");
                var controller = controllers[0];
                var button = controller.ToggleButton;
                Assert.That(button, Is.Not.Null, "toggle button");
                Assert.That(button.gameObject.scene, Is.EqualTo(scene));
                var canvas = button.GetComponentInParent<Canvas>(true);
                Assert.That(canvas, Is.Not.Null);
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.WorldSpace));
                Assert.That(canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(), Is.Not.Null, "XR ray / poke");
                Assert.That(button.GetComponent<FarmConsolePressGuard>(), Is.Not.Null, "press does not close the disease ring");
                // Exactly one button on the irrigation console.
                Assert.That(canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length, Is.EqualTo(1));
                // The controller subscribes in Awake (runtime listener); no persistent listeners point elsewhere.
                Assert.That(button.onClick.GetPersistentEventCount(), Is.EqualTo(0));
                // Reachable at standing height near the entrance (spawn at 0, 0, -6.5).
                var position = button.transform.position;
                Assert.That(position.y, Is.InRange(0.9f, 1.6f));
                Assert.That(Vector2.Distance(new Vector2(position.x, position.z), new Vector2(0f, -6.5f)), Is.LessThan(5f));
                // The flow shows on hardware: valve lever, pressure gauge needle, a 3D push button.
                Assert.That(controller.ValveHandle, Is.Not.Null, "valve lever");
                Assert.That(controller.GaugeNeedle, Is.Not.Null, "gauge needle");
                Assert.That(controller.PushButton, Is.Not.Null, "push button");
                return 0;
            });
        }

        [Test]
        public void FarmScene_EveryRowHasABranchPipeAndAPool_RegisteredWithTheController()
        {
            WithFarmScene(scene =>
            {
                var controller = Controllers(scene).Single();
                var ids = DiseaseClasses.AllCases.Select(d => d.PlantId + "_" + d.DiseaseId).ToList();
                Assert.That(ids.Count, Is.EqualTo(58));
                Assert.That(controller.Rows.Count, Is.EqualTo(58), "58 rows registered");
                Assert.That(controller.Rows.Select(r => r.id), Is.EquivalentTo(ids));

                Assert.That(controller.PipeRenderers.Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(controller.PoolRenderers.Count, Is.GreaterThanOrEqualTo(1));
                foreach (var renderer in controller.PipeRenderers.Concat(controller.PoolRenderers))
                {
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.GetComponent<Collider>(), Is.Null, $"{renderer.name} must not block rays");
                    Assert.That(renderer.sharedMaterial, Is.Not.Null);
                    Assert.That(renderer.sharedMaterial.shader.isSupported, Is.True, renderer.sharedMaterial.shader.name);
                }

                // Every pool vertex carries its row's arrival distance (UV1.x); every row has pool vertices.
                var arrivals = new List<Vector2>();
                foreach (var pool in controller.PoolRenderers)
                {
                    var mesh = pool.GetComponent<MeshFilter>().sharedMesh;
                    Assert.That(mesh, Is.Not.Null);
                    var uv1 = new List<Vector2>();
                    mesh.GetUVs(1, uv1);
                    arrivals.AddRange(uv1);
                }

                var rowsRoot = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SelectablePlant>(true)).Where(s => s.IsRow).ToList();
                foreach (var row in controller.Rows)
                {
                    Assert.That(row.branchLength, Is.GreaterThan(1f), $"{row.id} branch pipe");
                    Assert.That(row.arrival, Is.GreaterThan(0f).And.LessThan(controller.NetworkLength), $"{row.id} arrival");
                    Assert.That(row.poolSize.x * row.poolSize.y, Is.GreaterThan(0.5f), $"{row.id} pool");
                    Assert.That(arrivals.Any(a => Mathf.Abs(a.x - row.arrival) < 1e-3f && a.y > 0f), Is.True, $"{row.id} pool geometry");
                    // The pool lies along its row's plants.
                    var plants = rowsRoot.Single(s => s.PlantId + "_" + s.DiseaseId == row.id).Plants;
                    foreach (var plant in plants)
                    {
                        Assert.That(Mathf.Abs(plant.position.x - row.poolCentre.x), Is.LessThan(row.poolSize.x * 0.5f), $"{row.id} {plant.name} x");
                        Assert.That(Mathf.Abs(plant.position.z - row.poolCentre.z), Is.LessThan(row.poolSize.y * 0.5f), $"{row.id} {plant.name} z");
                    }
                }

                Assert.That(controller.DripPoints.Count, Is.GreaterThanOrEqualTo(58));
                Assert.That(controller.Drips, Is.Not.Null);
                Assert.That(controller.Drips.main.maxParticles, Is.LessThanOrEqualTo(2000), "particle cap for Quest");
                return 0;
            });
        }

        [Test]
        public void Controller_StateMachine_OffFlowingStandingDrainingOff()
        {
            var go = new GameObject("IrrigationTest");
            try
            {
                var controller = go.AddComponent<FarmIrrigationController>();
                var rows = new List<FarmIrrigationController.Row>
                {
                    new FarmIrrigationController.Row { id = "near", arrival = 5f, branchLength = 6f, poolSize = Vector2.one },
                    new FarmIrrigationController.Row { id = "far", arrival = 40f, branchLength = 14f, poolSize = Vector2.one },
                };
                controller.Configure(rows, 55f, 8f, 7f, 5f);
                controller.ResetToOff();
                var seen = new List<IrrigationState> { controller.State };
                controller.StateChanged += s => seen.Add(s);

                void AssertFills()
                {
                    for (var i = 0; i < rows.Count; i++)
                        Assert.That(controller.RowFill(i), Is.InRange(0f, 1f));
                    Assert.That(controller.Flow, Is.InRange(0f, 1f));
                    Assert.That(controller.Level, Is.InRange(0f, 1f));
                    Assert.That(controller.PipeFill, Is.InRange(0f, 1f));
                }

                Assert.That(controller.State, Is.EqualTo(IrrigationState.Off));
                controller.Toggle();
                Assert.That(controller.IsOn, Is.True);
                Assert.That(controller.State, Is.EqualTo(IrrigationState.Flowing));

                // Six seconds in (pools start 4 s after the water reaches a row): the near row is filling, the far row is not.
                for (var t = 0f; t < 6f; t += 0.1f)
                {
                    controller.Step(0.1f);
                    AssertFills();
                }

                Assert.That(controller.State, Is.EqualTo(IrrigationState.Flowing));
                Assert.That(controller.RowFill(0), Is.GreaterThan(0f).And.LessThan(1f));
                Assert.That(controller.RowFill(1), Is.EqualTo(0f));

                // Frame-rate independent: many small steps reach the same front as few large ones.
                var steps = 0;
                while (controller.State == IrrigationState.Flowing && steps++ < 10000)
                {
                    controller.Step(1f / 90f);
                    AssertFills();
                }

                Assert.That(controller.State, Is.EqualTo(IrrigationState.Standing));
                Assert.That(controller.RowFill(0), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(controller.RowFill(1), Is.EqualTo(1f).Within(1e-4f));
                // (40 m + 8 m/s * (4 s + 7 s)) / 8 m/s = 16 s of flow in all.
                Assert.That(controller.Front, Is.EqualTo(controller.FrontMax).Within(1e-3f));

                controller.Toggle();
                Assert.That(controller.State, Is.EqualTo(IrrigationState.Draining));
                controller.Step(2f);
                AssertFills();
                Assert.That(controller.State, Is.EqualTo(IrrigationState.Draining));
                Assert.That(controller.RowFill(0), Is.GreaterThan(0f).And.LessThan(1f));
                for (var t = 0f; t < 6f; t += 0.25f)
                {
                    controller.Step(0.25f);
                    AssertFills();
                }

                Assert.That(controller.State, Is.EqualTo(IrrigationState.Off));
                Assert.That(controller.RowFill(0), Is.EqualTo(0f));
                Assert.That(controller.Front, Is.EqualTo(0f));
                // The soil stays wet for a while after the water stops, then dries.
                Assert.That(controller.Wetness, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
                for (var t = 0f; t < 25f; t += 0.5f)
                    controller.Step(0.5f);
                Assert.That(controller.Wetness, Is.EqualTo(0f));
                Assert.That(controller.State, Is.EqualTo(IrrigationState.Off));
                Assert.That(seen, Is.EqualTo(new[] { IrrigationState.Off, IrrigationState.Flowing, IrrigationState.Standing, IrrigationState.Draining, IrrigationState.Off }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Controller_StepIsFrameRateIndependent()
        {
            float FrontAfter(float dt, float seconds)
            {
                var go = new GameObject("IrrigationRate");
                try
                {
                    var c = go.AddComponent<FarmIrrigationController>();
                    c.Configure(new List<FarmIrrigationController.Row> { new FarmIrrigationController.Row { id = "r", arrival = 30f } }, 50f, 8f, 7f, 5f);
                    c.SetOn(true);
                    for (var t = 0f; t < seconds - 1e-4f; t += dt)
                        c.Step(dt);
                    return c.Front;
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            Assert.That(FrontAfter(1f / 30f, 3f), Is.EqualTo(FrontAfter(1f / 120f, 3f)).Within(0.2f));
        }
    }
}
