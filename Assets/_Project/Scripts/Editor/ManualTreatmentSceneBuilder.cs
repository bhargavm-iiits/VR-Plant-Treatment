using System.Collections.Generic;
using System.Linq;
using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BTP.Editor
{
    /// <summary>
    /// Adds the Manual treatment mode to Treatment.unity (called by BTP/Treatment/3 - Build Treatment Scene): the mode
    /// switch and learner picker on the case selector, and five more floating panels around the central leaf holder,
    /// in the Interface's local space (viewer at the origin, leaf straight ahead):
    /// selector −64°, disease information −30°, leaf health above the leaf, treatment adjustment / final results +36°,
    /// progress +80°. The step lesson's task and case-notes panels are hidden while the manual mode is active.
    /// </summary>
    public static class ManualTreatmentSceneBuilder
    {
        public const float SelectorHeight = 940f;
        public const float InfoYaw = -30f;
        public const float AdjustmentYaw = 36f;
        public const float ProgressYaw = 80f;
        public const int ControlRows = 5;
        const float Eye = 1.6f;
        const string MoteMaterial = ProjectPaths.Root + "/Materials/Treatment/VeinMote.mat";
        const string MoteMesh = ProjectPaths.Meshes + "/VeinMote.asset";
        static readonly Color PrimaryText = new Color(0.02f, 0.11f, 0.14f);

        internal static void Build(Transform treatment, Transform ui, RectTransform selector, TreatmentController controller)
        {
            var view = treatment.gameObject.AddComponent<ManualTreatmentView>();
            AddModeAndLearner(selector, view);
            var info = BuildInfo(ui, view);
            var adjustment = BuildAdjustment(ui, view);
            var results = BuildResults(ui, view);
            var progress = BuildProgress(ui, view);
            var health = BuildHealth(ui, view);
            view.manualPanels = new List<GameObject> { info, adjustment, results, progress, health };
            // The step-lesson windows are no longer built, so nothing is hidden per mode.
            view.stepPanels = new List<GameObject>();

            var particles = BuildParticles(treatment);
            var particleController = treatment.gameObject.AddComponent<LeafParticleController>();
            particleController.Bind(particles);
            var recovery = treatment.gameObject.AddComponent<LeafRecoveryController>();
            recovery.Bind(particleController, LeafRecoveryController.DefaultTransitionSeconds);
            treatment.gameObject.AddComponent<ManualTreatmentController>().Bind(controller, view, recovery);
            // Built hidden: the controller shows the panels of the remembered mode at start.
            foreach (var panel in view.manualPanels)
                panel.SetActive(false);
        }

        static TMP_Text Text(RectTransform parent, string name, string text, float size, Color color, float x, float y, float w, float h,
            FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, float minSize = 0f)
        {
            var label = TreatmentSceneBuilder.Text(parent, name, text, size, color, x, y, w, h, style, alignment);
            if (minSize > 0f)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = minSize;
                label.fontSizeMax = size;
            }

            return label;
        }

        static Button Button(RectTransform parent, string name, string label, float size, float x, float y, float w, float h, out SelectedStateVisual state,
            bool primary = false)
        {
            var button = primary
                ? UIFactory.Button(name, parent, label, size, out state, UIPalette.Cyan, PrimaryText)
                : UIFactory.Button(name, parent, label, size, out state);
            var text = button.GetComponentInChildren<TMP_Text>();
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(14f, size);
            text.fontSizeMax = size;
            if (primary)
                text.fontStyle = FontStyles.Bold;
            UIFactory.Place((RectTransform)button.transform, x, y, w, h);
            return button;
        }

        // ------------------------------------------------------------------ 1 mode switch and learner picker

        static void AddModeAndLearner(RectTransform selector, ManualTreatmentView view)
        {
            // Manual treatment is the lab's only mode, so there is no mode switch: just the learner picker.
            const float w = 620f, m = 28f;
            Text(selector, "LearnerTitle", "Learner (saved on this headset)", 24f, UIPalette.TextSecondary, m, 812f, w - 2f * m, 32f, FontStyles.Bold);
            view.learnerPrevious = Button(selector, "Learner_Previous", "<", 26f, m, 850f, 64f, 64f, out _);
            var name = UIFactory.RoundedImage("LearnerName", selector, UIPalette.Header, ProceduralTextures.RoundedFill);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, m + 72f, 850f, 300f, 64f);
            view.learnerName = UIFactory.Text("Text", name.transform, "", 22f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            view.learnerName.enableAutoSizing = true;
            view.learnerName.fontSizeMin = 14f;
            view.learnerName.fontSizeMax = 22f;
            UIFactory.Stretch(view.learnerName.rectTransform, 6f);
            view.learnerNext = Button(selector, "Learner_Next", ">", 26f, m + 380f, 850f, 64f, 64f, out _);
            view.learnerNew = Button(selector, "Learner_New", "+ New", 22f, m + 452f, 850f, 112f, 64f, out _);
        }

        // ------------------------------------------------------------------ 2 disease information

        static GameObject BuildInfo(Transform ui, ManualTreatmentView view)
        {
            const float w = 600f, h = 980f, m = 28f, inner = w - 2f * m;
            var panel = TreatmentSceneBuilder.Panel(ui, "DiseaseInfoPanel", new Vector2(w, h), InfoYaw, 1.5f, 1.2f);
            Text(panel, "Header", "Disease information", 20f, UIPalette.Cyan, m, 16f, inner, 26f, FontStyles.Bold);
            view.infoTitle = Text(panel, "Title", "", 34f, Color.white, m, 44f, inner, 46f, FontStyles.Bold, TextAlignmentOptions.TopLeft, 20f);
            view.infoTitle.textWrappingMode = TextWrappingModes.NoWrap;
            view.infoStatus = Text(panel, "Status", "", 18f, UIPalette.Cyan, m, 92f, inner, 26f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 13f);
            view.infoBody = Text(panel, "Body", "", 19f, Color.white, m, 124f, inner, 666f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 12f);
            view.infoBody.overflowMode = TextOverflowModes.Truncate;
            view.infoPrimary = Button(panel, "Primary", "Start manual session", 26f, m, 802f, inner, 84f, out _, true);
            view.infoSecondary = Button(panel, "Secondary", "Start over", 22f, m, 898f, inner, 60f, out _);
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ 3 treatment adjustment

        static GameObject BuildAdjustment(Transform ui, ManualTreatmentView view)
        {
            const float w = 720f, h = 1060f, m = 28f, inner = w - 2f * m, rowHeight = 144f, top = 104f;
            var panel = TreatmentSceneBuilder.Panel(ui, "TreatmentAdjustmentPanel", new Vector2(w, h), AdjustmentYaw, 1.52f, 1.2f);
            Text(panel, "Title", "Treatment adjustment", 34f, Color.white, m, 16f, inner, 46f, FontStyles.Bold);
            view.adjustmentHint = Text(panel, "Hint", "", 18f, UIPalette.TextSecondary, m, 62f, inner, 38f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 13f);
            view.rows = new List<ManualTreatmentView.ControlRow>();
            const float buttonSize = 80f, textWidth = inner - 2f * buttonSize - 24f;
            for (var i = 0; i < ControlRows; i++)
            {
                var y = top + i * rowHeight;
                var row = UIFactory.Place(UIFactory.Node("Control" + (i + 1), panel), m, y, inner, rowHeight - 8f);
                var background = UIFactory.RoundedImage("Background", row, new Color(1f, 1f, 1f, 0.05f), ProceduralTextures.RoundedFill);
                background.raycastTarget = false;
                UIFactory.Stretch(background.rectTransform);
                var r = new ManualTreatmentView.ControlRow { root = row.gameObject };
                r.name = Text(row, "Name", "", 23f, Color.white, 12f, 6f, textWidth, 30f, FontStyles.Bold, TextAlignmentOptions.TopLeft, 15f);
                r.name.textWrappingMode = TextWrappingModes.NoWrap;
                r.description = Text(row, "Description", "", 15f, UIPalette.TextSecondary, 12f, 36f, textWidth, 40f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 11f);
                r.values = Text(row, "Values", "", 18f, Color.white, 12f, 78f, textWidth, 26f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 12f);
                r.status = Text(row, "Status", "", 16f, UIPalette.TextSecondary, 12f, 106f, textWidth, 24f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 11f);
                r.minus = Button(row, "Minus", "-", 40f, inner - 2f * buttonSize - 12f, 28f, buttonSize, buttonSize, out _);
                r.plus = Button(row, "Plus", "+", 40f, inner - buttonSize - 4f, 28f, buttonSize, buttonSize, out _);
                view.rows.Add(r);
            }

            var bottom = top + ControlRows * rowHeight;
            view.applyButton = Button(panel, "Apply", "Apply treatment", 28f, m, bottom + 8f, inner, 84f, out _, true);
            view.adjustmentFeedback = Text(panel, "Feedback", "", 18f, UIPalette.Cyan, m, bottom + 102f, inner, h - bottom - 120f, FontStyles.Italic,
                TextAlignmentOptions.TopLeft, 12f);
            Text(panel, "Footer", "EDUCATIONAL SIMULATION • ABSTRACT 0-10 SCALES • NO PRODUCTS OR DOSES", 13f, UIPalette.TextSecondary, m, h - 26f, inner, 18f,
                FontStyles.Normal, TextAlignmentOptions.BottomRight);
            view.adjustmentPanel = panel.gameObject;
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ 6 final results (same place as the adjustment panel)

        static GameObject BuildResults(Transform ui, ManualTreatmentView view)
        {
            const float w = 720f, h = 1060f, m = 28f, inner = w - 2f * m;
            var panel = TreatmentSceneBuilder.Panel(ui, "FinalResultsPanel", new Vector2(w, h), AdjustmentYaw, 1.52f, 1.2f);
            Text(panel, "Title", "Final results", 34f, Color.white, m, 16f, inner, 46f, FontStyles.Bold);
            view.resultsBody = Text(panel, "Body", "", 21f, Color.white, m, 72f, inner, 860f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 13f);
            view.resultsBody.overflowMode = TextOverflowModes.Truncate;
            view.newSessionButton = Button(panel, "NewSession", "Start a new session", 26f, m, h - 110f, inner, 84f, out _, true);
            view.resultsPanel = panel.gameObject;
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ 4 progress

        static GameObject BuildProgress(Transform ui, ManualTreatmentView view)
        {
            const float w = 560f, h = 860f, m = 28f, inner = w - 2f * m;
            var panel = TreatmentSceneBuilder.Panel(ui, "TreatmentProgressPanel", new Vector2(w, h), ProgressYaw, 1.5f, 1.2f);
            Text(panel, "Title", "Treatment progress", 32f, Color.white, m, 16f, inner, 44f, FontStyles.Bold);
            view.clock = Text(panel, "Clock", $"{TreatmentTimer.Clock(0f)} / {TreatmentTimer.Clock(TreatmentTimer.SessionSeconds)}", 36f, UIPalette.Cyan, m, 64f, inner, 52f, FontStyles.Bold, TextAlignmentOptions.TopLeft, 20f);
            view.stageChips = new List<SelectedStateVisual>();
            view.stageLabels = new List<TMP_Text>();
            for (var i = 0; i < TreatmentTimer.StageCount; i++)
            {
                var chip = Button(panel, "Stage" + (i + 1), "", 20f, m, 128f + i * 60f, inner, 52f, out var state);
                chip.targetGraphic.raycastTarget = false;
                var label = chip.GetComponentInChildren<TMP_Text>();
                label.alignment = TextAlignmentOptions.MidlineLeft;
                view.stageChips.Add(state);
                view.stageLabels.Add(label);
            }

            view.guidance = Text(panel, "Guidance", "", 18f, UIPalette.TextSecondary, m, 432f, inner, 52f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 13f);
            view.stageButton = Button(panel, "StageAction", "Continue to monitoring", 24f, m, 490f, inner, 76f, out _, true);
            Text(panel, "HistoryTitle", "Action history", 20f, UIPalette.TextSecondary, m, 580f, inner, 28f, FontStyles.Bold);
            view.history = Text(panel, "History", "", 15f, Color.white, m, 612f, inner, h - 630f, FontStyles.Normal, TextAlignmentOptions.TopLeft, 11f);
            view.history.overflowMode = TextOverflowModes.Truncate;
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ 5 leaf health (above the leaf, tilted towards the viewer)

        static GameObject BuildHealth(Transform ui, ManualTreatmentView view)
        {
            const float w = 600f, h = 300f, m = 24f, inner = w - 2f * m;
            var position = TreatmentSceneBuilder.PanelPosition(0f, 1.95f, 1.12f);
            var canvas = UIFactory.WorldCanvas("LeafHealthPanel", ui, new Vector2(w, h), position, UIFactory.Facing(position, Vector3.up * Eye, -16f));
            var panel = (RectTransform)canvas.transform;
            UIFactory.Panel(panel);
            Text(panel, "Title", "Leaf health monitoring (simulated)", 24f, Color.white, m, 12f, inner, 34f, FontStyles.Bold);
            view.healthBar = UIFactory.Metric("Health", panel, "Leaf health", true, m, 50f, inner);
            view.severityBar = UIFactory.Metric("Severity", panel, "Active severity", false, m, 108f, inner);
            view.checkHealthButton = Button(panel, "CheckHealth", "Check health", 22f, m, 176f, 220f, 72f, out _, true);
            view.healthNote = Text(panel, "Note", "", 15f, UIPalette.Warning, m + 236f, 176f, inner - 236f, 106f, FontStyles.Italic, TextAlignmentOptions.TopLeft, 11f);
            return panel.gameObject;
        }

        // ------------------------------------------------------------------ vein particles

        static ParticleSystem BuildParticles(Transform treatment)
        {
            var go = new GameObject("LeafVeinParticles", typeof(ParticleSystem));
            go.transform.SetParent(treatment, false);
            var system = go.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.008f);
            main.startColor = new Color(0.45f, 1f, 0.6f, 1f);
            main.maxParticles = 80;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Octahedron();
            renderer.sharedMaterial = MaterialFactory.Glow(MoteMaterial, new Color(0.4f, 1f, 0.62f), 1.8f);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        /// <summary>An 8-triangle mote mesh (far cheaper on Quest than the built-in sphere).</summary>
        static Mesh Octahedron()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MoteMesh);
            if (mesh != null)
                return mesh;
            mesh = new Mesh { name = "VeinMote" };
            mesh.vertices = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back }.Select(v => v * 0.5f).ToArray();
            mesh.triangles = new[] { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ProjectPaths.EnsureFolder(ProjectPaths.Meshes);
            AssetDatabase.CreateAsset(mesh, MoteMesh);
            return mesh;
        }
    }
}
