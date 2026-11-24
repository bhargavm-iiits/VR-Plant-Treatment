using BTP.Lab;
using BTP.Plants;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BTP.Tests
{
    public class LeafDisplayTests
    {
        [Test]
        public void MouseDrag_RotatesAroundOffsetCentre_AndStopsOnRelease()
        {
            var root = new GameObject("Offset leaf rotation");
            var cameraRoot = new GameObject("Drag test camera");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(2, 1, 3), Quaternion.Euler(0, 35, 0));
                root.transform.localScale = Vector3.one * 0.8f;
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(root.transform, false);
                mesh.transform.localPosition = new Vector3(2, 3, -1);
                mesh.transform.localScale = new Vector3(0.5f, 1f, 0.1f);
                var centre = mesh.GetComponent<Renderer>().bounds.center;
                var rotation = root.transform.rotation;
                var camera = cameraRoot.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, 960, 640);
                camera.transform.position = centre + Vector3.back * 3f;
                var pointer = (Vector2)camera.WorldToScreenPoint(centre);
                var turn = root.AddComponent<LeafMouseRotation>();
                turn.Initialize(); // EditMode does not invoke the runtime lifecycle.
                turn.ProcessPointer(camera, pointer, false, false, false);
                Assert.That(Quaternion.Angle(mesh.transform.rotation, rotation), Is.LessThan(0.001f), "No idle rotation.");
                turn.ProcessPointer(camera, pointer, true, true, false);
                Assert.That(turn.IsDragging, Is.True);
                pointer += Vector2.right * (180f / 0.35f);
                turn.ProcessPointer(camera, pointer, false, true, false);
                Assert.That(Vector3.Distance(mesh.GetComponent<Renderer>().bounds.center, centre), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(root.transform.rotation, rotation), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(mesh.transform.rotation, rotation), Is.EqualTo(180f).Within(0.001f));
                var released = mesh.transform.rotation;
                turn.ProcessPointer(camera, pointer, false, false, false);
                turn.ProcessPointer(camera, pointer + Vector2.right * 100f, false, false, false);
                Assert.That(turn.IsDragging, Is.False);
                Assert.That(Quaternion.Angle(mesh.transform.rotation, released), Is.LessThan(0.001f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraRoot); }
        }

        [Test]
        public void MouseDrag_IgnoresButtonsAndEmptySpace_AndLimitsTilt()
        {
            var root = new GameObject("Leaf input test");
            var cameraRoot = new GameObject("Input test camera");
            try
            {
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(root.transform, false);
                var camera = cameraRoot.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, 960, 640);
                camera.transform.position = Vector3.back * 3f;
                var pointer = (Vector2)camera.WorldToScreenPoint(Vector3.zero);
                var turn = root.AddComponent<LeafMouseRotation>();
                turn.Initialize();
                turn.ProcessPointer(camera, pointer, true, true, true);
                Assert.That(turn.IsDragging, Is.False, "UI clicks must not turn the leaf.");
                turn.ProcessPointer(camera, new Vector2(-10000, -10000), true, true, false);
                Assert.That(turn.IsDragging, Is.False, "Drag must begin on the leaf.");
                turn.ProcessPointer(camera, pointer, true, true, false);
                turn.ProcessPointer(camera, pointer + Vector2.up * 10000f, false, true, false);
                Assert.That(Quaternion.Angle(Quaternion.identity,mesh.transform.rotation), Is.EqualTo(65f).Within(0.001f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraRoot); }
        }

        [Test]
        public void LeafCatalog_CoversEveryPlantCondition_WithDistinctPrefabs()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LeafCatalog>("Assets/_Project/Data/LeafCatalog.asset");
            Assert.That(catalog, Is.Not.Null, "Prepare Leaf Display Assets must have completed.");
            Assert.That(catalog.Entries.Count, Is.EqualTo(DiseaseClasses.AllCases.Count));
            var prefabs = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var condition in DiseaseClasses.AllCases)
            {
                Assert.That(catalog.TryGet(condition.PlantId, condition.DiseaseId, out var prefab), Is.True,
                    condition.PlantId + "/" + condition.DiseaseId);
                Assert.That(prefabs.Add(prefab), Is.True, "Conditions should use their own supplied leaf.");
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
                    foreach (var material in renderer.sharedMaterials)
                    {
                        Assert.That(material.shader.name, Is.EqualTo("BTP/Leaf Recovery"));
                        Assert.That(material.GetTexture("_BaseMap"), Is.Not.Null);
                        Assert.That(material.GetTexture("_HealthyMap"), Is.Not.Null);
                        Assert.That(material.GetTexture("_RecoveryMap"), Is.Not.Null);
                        Assert.That(material.GetTexture("_BumpMap"), Is.Not.Null);
                        // A supplied leaf without a roughness map (soybean/septoria_brown_spot) keeps the shader default.
                        if (System.IO.File.Exists($"Assets/_Project/Textures/Leaves/{condition.PlantId}_{condition.DiseaseId}_roughness.png"))
                            Assert.That(material.GetTexture("_RoughnessMap"), Is.Not.Null);
                        else
                            Assert.That(material.GetTexture("_RoughnessMap"), Is.Null);
                    }
                long triangles = 0;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
                    for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                        triangles += filter.sharedMesh.GetIndexCount(i) / 3;
                // Grape Leaf Blight needs more geometry to preserve its folded surface and UVs.
                long budget = condition.PlantId == "grape" && condition.DiseaseId == "leaf_blight" ? 80000 : 24000;
                Assert.That(triangles, Is.GreaterThan(0).And.LessThanOrEqualTo(budget));
            }
            Assert.That(catalog.TryGet("tomato", "unavailable", out _), Is.False);
        }

        [Test]
        public void WeekChange_Interpolates_AndCanReverseWithoutJumping()
        {
            var root = new GameObject("Animation test");
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var visual = root.AddComponent<LeafRecoveryVisual>();
                visual.Show(data.GetWeek(1), data.GetWeek(1), data.GetWeek(6), root.transform);
                visual.Show(data.GetWeek(6), data.GetWeek(1), data.GetWeek(6), root.transform);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0f), "Week selection must not jump to the endpoint.");
                Assert.That(visual.TransitionSeconds, Is.EqualTo(3f));
                visual.Advance(1.5f);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(visual.TransitionGlow, Is.GreaterThan(0f), "Healing steps show the glow front.");
                visual.Show(data.GetWeek(1), data.GetWeek(1), data.GetWeek(6), root.transform);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0.5f).Within(0.001f), "Interrupted animation must continue from its visible state.");
                Assert.That(visual.TransitionGlow, Is.EqualTo(0f), "Going back a week must not glow.");
                visual.Advance(3f);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0f));
                Assert.That(visual.IsTransitioning, Is.False);
                Assert.That(visual.TransitionGlow, Is.EqualTo(0f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }

        [Test]
        public void Reset_UsesTheShorterDuration()
        {
            var root = new GameObject("Reset timing");
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var visual = root.AddComponent<LeafRecoveryVisual>();
                visual.Show(data.GetWeek(4), data.GetWeek(1), data.GetWeek(6), root.transform);
                visual.Show(data.GetWeek(1), data.GetWeek(1), data.GetWeek(6), root.transform, quick: true);
                Assert.That(visual.ResetSeconds, Is.LessThan(visual.TransitionSeconds));
                visual.Advance(visual.ResetSeconds);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(0f));
                Assert.That(visual.IsTransitioning, Is.False);
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }

        [Test]
        public void Animation_EndsAtTheSameValueAcrossFrameRates()
        {
            var slowRoot = new GameObject("Slow frames");
            var fastRoot = new GameObject("Fast frames");
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var slow = slowRoot.AddComponent<LeafRecoveryVisual>();
                var fast = fastRoot.AddComponent<LeafRecoveryVisual>();
                foreach (var visual in new[] { slow, fast })
                {
                    visual.Show(data.GetWeek(1), data.GetWeek(1), data.GetWeek(6), visual.transform);
                    visual.Show(data.GetWeek(6), data.GetWeek(1), data.GetWeek(6), visual.transform);
                }
                slow.Advance(1.5f); slow.Advance(1.5f);
                for (int i = 0; i < 200; i++) fast.Advance(0.016f);
                Assert.That(slow.DisplayedRecovery, Is.EqualTo(1f));
                Assert.That(fast.DisplayedRecovery, Is.EqualTo(slow.DisplayedRecovery).Within(0.001f));
            }
            finally { Object.DestroyImmediate(slowRoot); Object.DestroyImmediate(fastRoot); Object.DestroyImmediate(data); }
        }

        [Test]
        public void WeekSix_CompletelyRecoversBothHalves()
        {
            var root = new GameObject("Whole-leaf recovery");
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var visual = root.AddComponent<LeafRecoveryVisual>();
                visual.Show(data.GetWeek(5), data.GetWeek(1), data.GetWeek(6), root.transform);
                Assert.That(visual.DisplayedBaselineRecovery, Is.EqualTo(0f));
                visual.Show(data.GetWeek(6), data.GetWeek(1), data.GetWeek(6), root.transform);
                visual.Advance(3f);
                Assert.That(visual.DisplayedRecovery, Is.EqualTo(1f));
                Assert.That(visual.DisplayedBaselineRecovery, Is.EqualTo(1f));
                Assert.That(data.GetWeek(6).leafHealth, Is.EqualTo(100));
                Assert.That(data.GetWeek(6).diseaseSeverity, Is.EqualTo(0));
                Assert.That(visual.TransitionGlow, Is.EqualTo(0f), "Settled weeks must not glow.");
                visual.Show(data.GetWeek(5), data.GetWeek(1), data.GetWeek(6), root.transform);
                visual.Advance(3f);
                Assert.That(visual.DisplayedBaselineRecovery, Is.EqualTo(0f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }

        [Test]
        public void InspectionFrontAndRight_AreConsistentFromEverySide()
        {
            foreach (var eye in new[] { Vector3.back * 2, Vector3.forward * 2, Vector3.left * 2, Vector3.right * 2 })
            {
                var rotation = LeafViewerFacing.FacingRotation(Vector3.zero, eye, Quaternion.identity);
                Assert.That(Vector3.Dot(-(rotation * Vector3.forward), eye.normalized), Is.GreaterThan(0.999f), "Front must face the viewer.");
                var screenRight = Vector3.Cross(Vector3.up, -eye.normalized);
                Assert.That(Vector3.Dot(rotation * Vector3.right, screenRight), Is.GreaterThan(0.999f), "Comparison halves must not reverse.");
            }
        }

        [Test]
        public void Facing_WhenViewerIsAboveCentre_KeepsPreviousRotation()
        {
            var previous = Quaternion.Euler(0, 35, 0);
            var result = LeafViewerFacing.FacingRotation(Vector3.zero, Vector3.up * 1.6f, previous);
            Assert.That(Quaternion.Angle(previous, result), Is.LessThan(0.001f));
        }

        [Test]
        public void Recovery_PreservesSuppliedWeekOne_AndProgressesToGreen()
        {
            var data = ScriptableObject.CreateInstance<TomatoRecoveryData>();
            try
            {
                var baseline = data.GetWeek(1);
                var final = data.GetWeek(6);
                Assert.That(LeafRecoveryVisual.RecoveryAmount(baseline, baseline, final), Is.EqualTo(0f));
                Assert.That(LeafRecoveryVisual.RecoveryAmount(final, baseline, final), Is.EqualTo(1f));
                float previous = -1f;
                for (int i = 1; i <= 6; i++)
                {
                    float amount = LeafRecoveryVisual.RecoveryAmount(data.GetWeek(i), baseline, final);
                    Assert.That(amount, Is.GreaterThan(previous));
                    previous = amount;
                }
            }
            finally { Object.DestroyImmediate(data); }
        }

        [Test]
        public void LeafGrab_IsAGripToggleItem_LikeTheScoopAndSprayBottle()
        {
            var root = new GameObject("Grip-toggle leaf");
            BTP.Treatment.TreatmentGrabbable item = null;
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(30f, 1f, 2f), Quaternion.Euler(0f, 20f, 0f));
                root.transform.localScale = Vector3.one * 0.7f;
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(mesh.GetComponent<Collider>());
                mesh.transform.SetParent(root.transform, false);
                mesh.transform.localPosition = new Vector3(0.3f, 0.6f, 0f);
                var leaf = root.AddComponent<LeafMouseRotation>();
                leaf.Initialize(); // EditMode does not invoke the runtime lifecycle.
                var centre = mesh.GetComponent<Renderer>().bounds.center;

                // A flat leaf still gets a collider at least MinThickness thick (world), fitted round it, on the pivot.
                var box = leaf.GrabCollider;
                Assert.That(box, Is.Not.Null);
                Assert.That(box.transform, Is.SameAs(leaf.Pivot), "the collider moves with the leaf");
                Assert.That(box.size.z * 0.7f, Is.EqualTo(LeafMouseRotation.MinThickness).Within(1e-4f));
                Assert.That(box.size.x * 0.7f, Is.EqualTo(0.7f + 2f * LeafMouseRotation.ColliderPadding).Within(1e-4f));
                Assert.That(Vector3.Distance(box.transform.TransformPoint(box.center), centre), Is.LessThan(1e-4f));
                Assert.That(leaf.Pivot.gameObject.layer, Is.EqualTo(0), "on the layer the rays and the grip toggle test");

                // The same hand-held item as the tools: grip toggle only for controllers, brought to the hand by its stem.
                leaf.AttachGrab();
                item = leaf.Grabbable;
                Assert.That(item, Is.Not.Null);
                Assert.That(BTP.Treatment.ToolGripToggle.Registered, Has.Member(item), "the grip toggle manages the leaf");
                Assert.That(item.Grab.attachTransform, Is.SameAs(leaf.Grip));
                Assert.That(item.Grab.farAttachMode, Is.EqualTo(UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Near),
                    "a ray pick-up brings the leaf into the hand");
                Assert.That(item.Grab.unparentTransformOnGrab, Is.False, "the held leaf stays under its model (a new leaf takes it away)");
                Assert.That(item.Grab.retainTransformParent, Is.True);
                Assert.That(Vector3.Distance(leaf.Grip.position, centre - leaf.Pivot.up * 0.35f), Is.LessThan(1e-3f), "held by the stem end");
                Assert.That(Vector3.Angle(leaf.Grip.forward, leaf.Pivot.up), Is.LessThan(0.01f), "the leaf points forward from the hand");
                Assert.That(item.AtHome && !leaf.IsHeld, Is.True);
                Assert.That(Vector3.Distance(item.HomePosition, leaf.Pivot.position), Is.LessThan(1e-5f), "home is the stand");

                // Picked as the tools are: reached at the hand, else pointed at; nothing otherwise.
                Physics.SyncTransforms();
                var items = new System.Collections.Generic.List<BTP.Treatment.TreatmentGrabbable> { item };
                Assert.That(BTP.Treatment.ToolGripToggle.Choose(items, null, centre - leaf.Pivot.forward * 0.1f, null), Is.SameAs(item), "a controller at the leaf");
                var away = centre - leaf.Pivot.forward * 1.5f + Vector3.up * 0.4f;
                Assert.That(BTP.Treatment.ToolGripToggle.Choose(items, null, away, null), Is.Null, "nothing within reach, nothing pointed at");
                Assert.That(BTP.Treatment.ToolGripToggle.Choose(items, null, away, new Ray(away, (centre - away).normalized)), Is.SameAs(item), "pointing at the leaf");
            }
            finally
            {
                if (item != null)
                    BTP.Treatment.ToolGripToggle.Unregister(item);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MouseDrag_StillTurnsTheLeafOnItsStand_WithTheGrabAttached()
        {
            var root = new GameObject("Drag with grab");
            var cameraRoot = new GameObject("Drag camera");
            BTP.Treatment.TreatmentGrabbable item = null;
            try
            {
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(root.transform, false);
                root.transform.position = new Vector3(-30f, 0f, 0f);
                var camera = cameraRoot.AddComponent<Camera>();
                camera.pixelRect = new Rect(0, 0, 960, 640);
                camera.transform.position = root.transform.position + Vector3.back * 3f;
                var leaf = root.AddComponent<LeafMouseRotation>();
                leaf.Initialize();
                leaf.AttachGrab();
                item = leaf.Grabbable;
                var before = leaf.Pivot.rotation;
                var pointer = (Vector2)camera.WorldToScreenPoint(root.transform.position);
                leaf.ProcessPointer(camera, pointer, true, true, false);
                Assert.That(leaf.IsDragging, Is.True, "the mouse still drags the leaf on its stand");
                leaf.ProcessPointer(camera, pointer + Vector2.right * 100f, false, true, false);
                Assert.That(Quaternion.Angle(before, leaf.Pivot.rotation), Is.EqualTo(35f).Within(0.01f));
                Assert.That(item.AtHome, Is.True, "dragging does not take it off the stand");
            }
            finally
            {
                if (item != null)
                    BTP.Treatment.ToolGripToggle.Unregister(item);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraRoot);
            }
        }
    }
}
