using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BTP.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace BTP.Treatment
{
    /// <summary>
    /// Runs the Treatment lab. For the shared selected case it loads the SQLite record, validates
    /// it, instantiates the database-selected environment, specimen, targets, fixtures and tool
    /// rack through the asset catalog, restores saved progress, and routes every interaction
    /// through <see cref="TreatmentSession"/>. Progress is saved after every accepted action and
    /// before switching cases or environments.
    /// Every task can be done with the task panel's action buttons alone (<see cref="PerformButton"/>): a button
    /// sends the same action a tool-on-target selection sends, and <see cref="TreatmentActionAnimator"/> flies
    /// the tool from the rack to the target and back. Selecting rack tools and targets directly still works.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class TreatmentController : MonoBehaviour, IEnvironmentReadiness, IEnvironmentExit
    {
        static readonly HashSet<string> PlantSlots = new HashSet<string>
            { "plant_lower", "plant_mid", "plant_upper", "plant_top", "plant_inner", "plant_center", "root_zone", "ground", "specimen" };

        [SerializeField] Transform environmentRoot;
        [SerializeField] SpawnAnchor spawnAnchor;
        [Tooltip("UI group moved to the environment's 'interface' slot.")]
        [SerializeField] Transform interfaceRoot;
        [Tooltip("Tool rack moved to the environment's 'tool_rack' slot; tools are created under it.")]
        [SerializeField] Transform toolRack;
        [SerializeField] TreatmentPanel panel;
        [SerializeField] Button resetButton;
        [SerializeField] GameObject tooltipRoot;
        [SerializeField] TMP_Text tooltipText;
        [Tooltip("Environment shown when a case cannot be loaded, so navigation still works.")]
        [SerializeField] string fallbackEnvironmentKey = "env.greenhouse";
        [SerializeField, Min(0.05f)] float rackSpacing = 0.18f;
        [Tooltip("Plays the tool animation for button actions.")]
        [SerializeField] TreatmentActionAnimator animator;
        [Tooltip("Turns the default view (spawn and Recenter) right of the specimen, so the specimen and the task panel are both in front.")]
        [SerializeField, Range(-45f, 45f)] float spawnYawOffset = 16f;

        TreatmentServices services;
        TreatmentSession session;
        CaseKey caseKey;
        string environmentKey;
        GameObject environmentInstance;
        readonly Dictionary<string, Transform> slots = new Dictionary<string, Transform>();
        readonly List<TreatmentTarget> targets = new List<TreatmentTarget>();
        readonly Dictionary<TreatmentTarget, (Transform parent, Vector3 position, Quaternion rotation)> homes =
            new Dictionary<TreatmentTarget, (Transform, Vector3, Quaternion)>();
        readonly List<TreatmentToolPickup> rack = new List<TreatmentToolPickup>();
        readonly Dictionary<TreatmentTarget, GameObject> cutMarks = new Dictionary<TreatmentTarget, GameObject>();
        GameObject specimen;
        Vector3 specimenHome;
        Vector3 specimenScale;
        Bounds specimenBounds;
        GameObject bagOverlay;
        string heldTool;
        GameObject heldVisual;
        Transform heldAnchor;
        bool heldAnchorIsCamera;
        TreatmentTarget hovered;
        bool loading;
        Coroutine actionRoutine;
        bool busy;
        string toolInUse;
        bool toolInUseActive;
        bool manualMode;

        public bool IsEnvironmentReady { get; private set; }
        public TreatmentSession Session => session;
        public string HeldTool => heldTool;
        public CaseKey CaseKey => caseKey;
        public IReadOnlyList<TreatmentTarget> Targets => targets;
        public TreatmentPanel Panel => panel;
        public TreatmentActionAnimator Animator => animator;

        /// <summary>True while a button action's tool animation plays; input is ignored until it ends.</summary>
        public bool IsBusy => busy;

        /// <summary>Raised after the lesson or the scene state changes.</summary>
        public event Action Changed;

        /// <summary>Raised after a case (environment and specimen) has been loaded or failed to load.</summary>
        public event Action CaseLoaded;

        /// <summary>The leaf specimen of the loaded case (null when the case could not be loaded).</summary>
        public GameObject Specimen => specimen;

        /// <summary>True while the Manual treatment mode owns the specimen and the step-lesson objects are hidden.</summary>
        public bool ManualMode => manualMode;

        internal void Bind(Transform environment, SpawnAnchor anchor, Transform ui, Transform tools, TreatmentPanel taskPanel,
            Button reset, GameObject tooltip, TMP_Text tooltipLabel, TreatmentActionAnimator actionAnimator, float viewYawOffset)
        {
            environmentRoot = environment; spawnAnchor = anchor; interfaceRoot = ui; toolRack = tools; panel = taskPanel;
            resetButton = reset; tooltipRoot = tooltip; tooltipText = tooltipLabel; animator = actionAnimator; spawnYawOffset = viewYawOffset;
        }

        void Start()
        {
            services = TreatmentServices.Instance;
            if (panel != null)
                panel.Init(this);
            if (resetButton != null)
                resetButton.onClick.AddListener(ResetCase);
            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged += OnCaseChanged;

            if (services == null)
            {
                panel?.ShowUnavailable("Treatment lab", new[] { "Treatment services are not running. Start the application from XRBootstrap." });
                IsEnvironmentReady = true;
                return;
            }

            Load(services.SelectedCase());
            IsEnvironmentReady = true;
        }

        void OnDestroy()
        {
            if (SelectionState.Instance != null)
                SelectionState.Instance.CaseChanged -= OnCaseChanged;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                Save();
        }

        void OnApplicationQuit() => Save();

        public void PrepareForExit()
        {
            CancelAction();
            Save();
            DropTool();
        }

        void OnCaseChanged(string plantId, string diseaseId)
        {
            if (!IsEnvironmentReady || loading || services == null)
                return;
            var key = services.SelectedCase();
            if (key == caseKey)
                return;
            var previousEnvironment = environmentKey;
            Load(key);
            if (previousEnvironment != environmentKey && EnvironmentController.Instance != null)
                EnvironmentController.Instance.Recenter();
        }

        // ------------------------------------------------------------------ loading

        void Load(CaseKey key)
        {
            loading = true;
            try
            {
                CancelAction();
                Save();
                DropTool();
                ClearCase();
                caseKey = key;

                var content = services.GetCase(key, out var error);
                var problems = new List<string>();
                if (content == null)
                    problems.Add(error ?? $"No treatment record exists for {key}.");
                else
                    problems.AddRange(TreatmentCaseValidator.Validate(content, k => services.Content.GetTool(k) != null, services.Catalog.Has));
                foreach (var tool in content?.RackTools ?? new List<string>())
                {
                    var definition = services.Content.GetTool(tool);
                    if (definition == null || !services.Catalog.Has(definition.AssetKey))
                        problems.Add($"Tool '{tool}' has no Unity asset binding.");
                }

                var prefabKey = content?.Environment?.PrefabKey;
                if (problems.Count > 0 || !services.Catalog.Has(prefabKey))
                    prefabKey = fallbackEnvironmentKey;
                BuildEnvironment(prefabKey);

                if (problems.Count > 0)
                {
                    foreach (var problem in problems)
                        Debug.LogWarning($"[BTP] Treatment case {key} unavailable: {problem}");
                    session = null;
                    panel?.ShowUnavailable(content != null ? content.Title : TitleFor(key), problems);
                    return;
                }

                var progress = services.Progress.Load(key, content.ContentVersion);
                if (progress.ContentVersion != content.ContentVersion)
                {
                    // Content was updated: keep progress for steps that still exist.
                    progress.CompletedSteps.RemoveWhere(id => content.FindStep(id) == null);
                    progress.ContentVersion = content.ContentVersion;
                }

                session = new TreatmentSession(content, progress);
                BuildSpecimen(content);
                foreach (var definition in content.SceneTargets())
                    BuildTarget(definition);
                BuildRack(content);
                RefreshScene();
                panel?.ShowFeedback(progress.HasStarted ? "Saved progress restored." : content.Scenario, true);
                RefreshPanel();
            }
            finally
            {
                loading = false;
                // Also reached by the early return of an unavailable case, so listeners always hear about the load.
                if (manualMode)
                    ApplyManualVisibility();
                CaseLoaded?.Invoke();
            }
        }

        static string TitleFor(CaseKey key)
        {
            foreach (var info in BTP.Plants.DiseaseClasses.For(key.PlantId))
                if (info.DiseaseId == key.DiseaseId)
                    return $"{key.PlantId} \u2022 {info.DisplayName}";
            return key.ToString();
        }

        public void Retry()
        {
            if (services == null)
                return;
            services.ReloadContent();
            Load(services.SelectedCase());
        }

        public void ResetCase()
        {
            if (services == null || !caseKey.IsValid)
                return;
            CancelAction();
            DropTool();
            session = null; // nothing to save: this case is being reset
            services.Progress.Reset(caseKey);
            services.NotifyProgressChanged(caseKey);
            Load(caseKey);
            panel?.ShowFeedback("This case was reset. Other cases keep their progress.", true);
        }

        void ClearCase()
        {
            foreach (var pickup in rack)
                if (pickup != null)
                    Destroy(pickup.gameObject);
            rack.Clear();
            targets.Clear();
            homes.Clear();
            cutMarks.Clear();
            hovered = null;
            specimen = null;
            bagOverlay = null;
            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
            // Targets and the specimen live under the environment instance and go with it.
            if (environmentInstance != null)
            {
                environmentInstance.SetActive(false);
                Destroy(environmentInstance);
            }

            environmentInstance = null;
            environmentKey = null;
            slots.Clear();
        }

        void BuildEnvironment(string prefabKey)
        {
            var prefab = services.Catalog.Resolve<GameObject>(prefabKey);
            if (prefab == null)
            {
                Debug.LogError($"[BTP] Treatment environment '{prefabKey}' is not bound in the asset catalog.", this);
                return;
            }

            environmentInstance = Instantiate(prefab, environmentRoot, false);
            environmentInstance.name = prefabKey;
            environmentKey = prefabKey;
            foreach (var slot in environmentInstance.GetComponentsInChildren<TreatmentSlot>(true))
                slots[slot.SlotName] = slot.transform;

            CopyPose("interface", interfaceRoot);
            CopyPose("tool_rack", toolRack);
            if (spawnAnchor != null)
            {
                CopyPose("spawn", spawnAnchor.transform);
                spawnAnchor.transform.rotation = Quaternion.Euler(0f, spawnYawOffset, 0f) * spawnAnchor.transform.rotation;
            }
        }

        void CopyPose(string slotName, Transform target)
        {
            if (target != null && slots.TryGetValue(slotName, out var slot))
                target.SetPositionAndRotation(slot.position, slot.rotation);
        }

        void BuildSpecimen(TreatmentCase content)
        {
            var prefab = services.Catalog.Resolve<GameObject>(content.SpecimenAsset);
            var parent = slots.TryGetValue("specimen", out var slot) ? slot : environmentInstance.transform;
            specimen = Instantiate(prefab, parent, false);
            specimen.name = "Specimen";
            specimen.transform.localScale *= content.SpecimenScale;
            foreach (var collider in specimen.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            specimenHome = specimen.transform.position;
            specimenScale = specimen.transform.localScale;
            specimenBounds = MeasureBounds(specimen);
        }

        static Bounds MeasureBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.5f);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                if (!(r is ParticleSystemRenderer))
                    bounds.Encapsulate(r.bounds);
            return bounds;
        }

        void BuildTarget(TreatmentTargetDef definition)
        {
            var prefab = services.Catalog.Resolve<GameObject>(definition.AssetKey);
            var instance = Instantiate(prefab, environmentInstance.transform, false);
            instance.name = "Target_" + definition.TargetId;
            if (!string.IsNullOrEmpty(definition.MaterialKey))
            {
                var material = services.Catalog.Resolve<Material>(definition.MaterialKey);
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.StartsWith("Blade"))
                        renderer.sharedMaterial = material;
            }

            if (instance.GetComponent<XRSimpleInteractable>() == null)
                instance.AddComponent<XRSimpleInteractable>();
            var target = instance.GetComponent<TreatmentTarget>() ?? instance.AddComponent<TreatmentTarget>();
            target.Init(this, definition, services.Catalog.Resolve<GameObject>("ui.marker"));
            targets.Add(target);
            Place(target, definition);
            homes[target] = (target.transform.parent, target.transform.localPosition, target.transform.localRotation);
        }

        // ------------------------------------------------------------------ placement

        Vector3 ViewerPosition => slots.TryGetValue("spawn", out var spawn) ? spawn.position : environmentInstance.transform.position - Vector3.forward * 1.5f;

        void Place(TreatmentTarget target, TreatmentTargetDef definition)
        {
            var sameSlot = targets.Where(t => t.Definition.Slot == definition.Slot).ToList();
            var index = sameSlot.IndexOf(target);
            var count = session.Case.SceneTargets().Count(d => d.Slot == definition.Slot);
            var transform = target.transform;

            if (definition.Slot.StartsWith("leaf_"))
            {
                PlaceOnLeaf(target, definition, index);
                return;
            }

            if (!PlantSlots.Contains(definition.Slot))
            {
                if (!slots.TryGetValue(definition.Slot, out var slot))
                {
                    Debug.LogWarning($"[BTP] Environment has no slot '{definition.Slot}' for target {definition.TargetId}.");
                    slot = environmentInstance.transform;
                }

                var spacing = slot.GetComponent<TreatmentSlot>()?.Spacing ?? 0.25f;
                var offset = (index - (count - 1) * 0.5f) * spacing;
                transform.SetPositionAndRotation(slot.position + slot.right * offset, slot.rotation);
                // The comparison leaf is shown large enough to compare with the specimen.
                if (definition.Slot == "comparison" && definition.AssetKey.StartsWith("target.leaf"))
                    transform.localScale = Vector3.one * 2.4f;
                return;
            }

            var bounds = specimenBounds;
            var center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            var toViewer = ViewerPosition - center;
            toViewer.y = 0f;
            toViewer = toViewer.sqrMagnitude < 1e-4f ? Vector3.back : toViewer.normalized;
            var height = Mathf.Max(bounds.size.y, 0.1f);
            var width = Mathf.Max(bounds.size.x, bounds.size.z);
            // Trees: targets sit at the canopy edge, inside the reachable 0.95-1.95 m band.
            var radius = height > 2.2f ? Mathf.Clamp(width * 0.45f, 0.6f, 1.6f) : Mathf.Clamp(width * 0.42f, 0.1f, 0.85f);
            var floor = ViewerPosition.y;

            float fraction;
            float baseAngle;
            switch (definition.Slot)
            {
                case "plant_lower": fraction = 0.28f; baseAngle = -28f; break;
                case "plant_mid": fraction = 0.5f; baseAngle = 26f; break;
                case "plant_upper": fraction = 0.72f; baseAngle = -12f; break;
                case "plant_top": fraction = 0.9f; baseAngle = 14f; radius *= 0.55f; break;
                case "plant_inner": fraction = 0.5f; baseAngle = -4f; radius *= 0.35f; break;
                case "plant_center":
                {
                    // The main stem or crown: on the plant's axis, low enough to reach.
                    var p = center + Vector3.up * Mathf.Min(height * 0.4f, 1.2f);
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(-toViewer, Vector3.up));
                    transform.localScale = new Vector3(1f, Mathf.Clamp(height * 0.6f, 0.1f, 1.2f) / 0.4f, 1f);
                    return;
                }
                case "root_zone":
                {
                    var spread = (index - (count - 1) * 0.5f) * 0.12f;
                    var right = Vector3.Cross(Vector3.up, toViewer);
                    var p = center + toViewer * Mathf.Min(radius * 0.45f, 0.2f) + right * spread + Vector3.up * 0.01f;
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(-toViewer, Vector3.up));
                    return;
                }
                case "ground":
                {
                    var angle = Mathf.Lerp(-70f, 70f, count > 1 ? index / (float)(count - 1) : 0.5f);
                    var direction = Quaternion.Euler(0f, angle, 0f) * toViewer;
                    var p = center + direction * (radius + 0.3f);
                    p.y = floor + 0.01f;
                    transform.SetPositionAndRotation(p, Quaternion.LookRotation(direction, Vector3.up));
                    return;
                }
                default: // "specimen": the whole-plant proxy at the base
                {
                    transform.SetPositionAndRotation(center, Quaternion.LookRotation(-toViewer, Vector3.up));
                    var size = Mathf.Clamp(width * 0.6f, 0.3f, 1.2f);
                    transform.localScale = new Vector3(size, 1f, size);
                    return;
                }
            }

            var spreadAngle = Mathf.Min(110f, 38f * count);
            var a = baseAngle + (count > 1 ? Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, index / (float)(count - 1)) : 0f);
            var dir = Quaternion.Euler(0f, a, 0f) * toViewer;
            var y = height > 2.2f ? floor + Mathf.Lerp(0.95f, 1.95f, Mathf.InverseLerp(0.28f, 0.9f, fraction)) : bounds.min.y + fraction * height;
            var position = center + dir * radius;
            position.y = y;
            // Leaves face the viewer and tilt back a little, like leaves held out for inspection.
            var look = position - new Vector3(ViewerPosition.x, position.y, ViewerPosition.z);
            var rotation = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look.normalized : -toViewer, Vector3.up) * Quaternion.Euler(-35f, 0f, 0f);
            transform.SetPositionAndRotation(position, rotation);
            if (target.Definition.AssetKey.StartsWith("target.leaf"))
                transform.localScale = Vector3.one * Mathf.Clamp(height * 0.14f, 0.1f, 0.17f) / 0.15f;
        }

        /// <summary>
        /// Leaf targets sit on the leaf surface (LeafAnchors computed from the leaf's texture) and are
        /// parented to the specimen, so they move with it into containment.
        /// </summary>
        void PlaceOnLeaf(TreatmentTarget target, TreatmentTargetDef definition, int index)
        {
            var anchors = specimen != null ? specimen.GetComponentInChildren<LeafAnchors>() : null;
            var t = target.transform;
            if (anchors == null)
            {
                Debug.LogWarning($"[BTP] Specimen has no LeafAnchors for target {definition.TargetId}.");
                return;
            }

            if (definition.Slot == "leaf_whole")
            {
                // The sample tag on the holder clip, just below the petiole: it never covers the leaf.
                anchors.TryGetPose("leaf_petiole", 0, out var petiole, out _);
                var toViewer = ViewerPosition - petiole;
                toViewer.y = 0f;
                t.SetPositionAndRotation(petiole - Vector3.up * 0.07f + toViewer.normalized * 0.03f,
                    Quaternion.LookRotation(-toViewer.normalized, Vector3.up));
            }
            else if (anchors.TryGetPose(definition.Slot, index, out var position, out var rotation))
            {
                t.SetPositionAndRotation(position, rotation);
            }

            t.SetParent(specimen.transform, true);
        }

        GameObject CutMark(TreatmentTarget target)
        {
            if (cutMarks.TryGetValue(target, out var mark))
                return mark;
            var prefab = services.Catalog.Resolve<GameObject>("ui.cut_mark");
            if (prefab == null || !homes.TryGetValue(target, out var home))
                return null;
            mark = Instantiate(prefab, home.parent, false);
            mark.name = "CutMark_" + target.TargetId;
            mark.transform.SetLocalPositionAndRotation(home.position, home.rotation);
            cutMarks[target] = mark;
            return mark;
        }

        void BuildRack(TreatmentCase content)
        {
            if (toolRack == null)
                return;
            var keys = new List<string> { null };
            keys.AddRange(content.RackTools);
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                var tool = key != null ? services.Content.GetTool(key) : null;
                var assetKey = tool != null ? tool.AssetKey : "tool.hand";
                var prefab = services.Catalog.Resolve<GameObject>(assetKey);
                if (prefab == null)
                    continue;
                var slot = new GameObject(key == null ? "Rack_Hand" : "Rack_" + key);
                slot.transform.SetParent(toolRack, false);
                slot.transform.localPosition = new Vector3((i - (keys.Count - 1) * 0.5f) * rackSpacing, 0f, 0f);
                var model = Instantiate(prefab, slot.transform, false);
                model.name = "Model";
                var pickupLabel = services.Catalog.Resolve<GameObject>("ui.rack_label");
                if (pickupLabel != null)
                {
                    var label = Instantiate(pickupLabel, slot.transform, false);
                    label.name = "Label";
                    var text = label.GetComponentInChildren<TMP_Text>();
                    if (text != null)
                        text.text = tool != null ? tool.Name : "Hand (no tool)";
                }

                var box = slot.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.08f, 0f);
                box.size = new Vector3(rackSpacing * 0.9f, 0.22f, 0.16f);
                slot.AddComponent<XRSimpleInteractable>();
                var pickup = slot.AddComponent<TreatmentToolPickup>();
                pickup.Init(this, key, tool != null ? tool.Name : "Hand");
                rack.Add(pickup);
            }
        }

        // ------------------------------------------------------------------ interaction

        public void OnToolSelected(TreatmentToolPickup pickup, Transform interactor)
        {
            if (session == null || IsBusy)
                return;
            if (pickup.ToolKey == null || pickup.ToolKey == heldTool)
            {
                DropTool();
                panel?.ShowFeedback(pickup.ToolKey == null ? "Empty gloved hand." : "Tool returned to the rack.", true);
            }
            else
            {
                Equip(pickup, interactor);
                var tool = services.Content.GetTool(pickup.ToolKey);
                panel?.ShowFeedback($"Holding the {tool?.Name?.ToLowerInvariant()}. {tool?.Description}", true);
            }

            RefreshPanel();
        }

        void Equip(TreatmentToolPickup pickup, Transform interactor)
        {
            DropTool();
            heldTool = pickup.ToolKey;
            var model = pickup.transform.Find("Model");
            if (model != null)
            {
                heldVisual = Instantiate(model.gameObject);
                heldVisual.name = "HeldTool_" + heldTool;
                foreach (var collider in heldVisual.GetComponentsInChildren<Collider>(true))
                    Destroy(collider);
                model.gameObject.SetActive(false);
            }

            heldAnchorIsCamera = interactor == null || interactor.name.Contains("Desktop") || interactor.GetComponentInParent<Camera>() != null;
            heldAnchor = heldAnchorIsCamera && Camera.main != null ? Camera.main.transform : interactor;
        }

        void DropTool()
        {
            if (heldVisual != null)
                Destroy(heldVisual);
            heldVisual = null;
            foreach (var pickup in rack)
            {
                var model = pickup != null ? pickup.transform.Find("Model") : null;
                if (model != null)
                    model.gameObject.SetActive(true);
            }

            heldTool = null;
            heldAnchor = null;
        }

        Vector3 CarryPosition(int index, out Quaternion rotation)
        {
            var anchor = heldAnchor != null ? heldAnchor : Camera.main != null ? Camera.main.transform : transform;
            var anchorIsCamera = heldAnchor == null || heldAnchorIsCamera;
            var local = anchorIsCamera ? new Vector3(-0.2f + index * 0.06f, -0.22f, 0.5f) : new Vector3(0f, 0.05f + index * 0.04f, 0.12f);
            rotation = anchor.rotation;
            return anchor.TransformPoint(local);
        }

        void LateUpdate()
        {
            if (heldVisual != null && heldAnchor != null)
            {
                var local = heldAnchorIsCamera ? new Vector3(0.2f, -0.2f, 0.45f) : new Vector3(0f, -0.02f, 0.06f);
                heldVisual.transform.SetPositionAndRotation(heldAnchor.TransformPoint(local), heldAnchor.rotation * Quaternion.Euler(10f, -20f, 0f));
            }

            if (session == null)
                return;
            var carried = session.CarriedTargets;
            for (var i = 0; i < carried.Count; i++)
            {
                var target = FindTarget(carried[i]);
                if (target == null)
                    continue;
                var position = CarryPosition(i, out var rotation);
                target.transform.SetPositionAndRotation(position, rotation);
            }

            if (tooltipRoot != null && tooltipRoot.activeSelf && Camera.main != null)
            {
                var away = tooltipRoot.transform.position - Camera.main.transform.position;
                if (away.sqrMagnitude > 1e-4f)
                    tooltipRoot.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }

        TreatmentTarget FindTarget(string targetId)
        {
            foreach (var target in targets)
                if (target.TargetId == targetId)
                    return target;
            return null;
        }

        public void OnTargetSelected(TreatmentTarget target, Transform interactor)
        {
            if (session == null || IsBusy)
                return;
            Handle(session.TryPerform(ActionContext.OnTarget(heldTool, target.TargetId, target.Tag)));
        }

        public void Choose(string optionId)
        {
            if (session != null && !IsBusy)
                Handle(session.TryPerform(ActionContext.Choose(optionId)));
        }

        public void Advance()
        {
            if (session != null && !IsBusy)
                Handle(session.TryPerform(ActionContext.Advance()));
        }

        /// <summary>
        /// Performs an action-panel button. Choices and the simulated advance apply at once. A tool or gloved-hand
        /// action is checked by the lesson engine first (the same <see cref="TreatmentSession.TryPerform"/> call a
        /// tool-on-target selection makes) and saved; the tool then flies to the target and the scene changes when
        /// it arrives. A refused action shakes the tool on the rack and shows the lesson's feedback.
        /// </summary>
        public void PerformButton(TreatmentButtonAction action)
        {
            if (session == null || action == null || IsBusy || loading)
                return;
            switch (action.Kind)
            {
                case TreatmentButtonKind.PractiseAgain:
                    ResetCase();
                    return;
                case TreatmentButtonKind.Choose:
                case TreatmentButtonKind.Advance:
                    Handle(session.TryPerform(action.Context));
                    return;
                default:
                    busy = true;
                    actionRoutine = StartCoroutine(UseRoutine(action));
                    return;
            }
        }

        IEnumerator UseRoutine(TreatmentButtonAction action)
        {
            // A tool taken from the rack directly is put back first: the button says which tool it uses.
            DropTool();
            var target = FindTarget(action.TargetId);
            var slot = RackSlot(action.ToolKey);
            var result = session.TryPerform(action.Context);
            panel?.SetBusy(true);
            if (!result.Accepted)
            {
                panel?.ShowFeedback(result.Message, false);
                if (animator != null)
                    yield return animator.StartCoroutine(animator.Refuse(slot));
                FinishAction();
                Changed?.Invoke();
                yield break;
            }

            Save();
            toolInUse = action.ToolKey;
            toolInUseActive = true;
            panel?.ShowHeld(HeldLine());
            var tool = action.ToolKey != null ? services.Content.GetTool(action.ToolKey) : null;
            panel?.ShowFeedback(tool != null ? $"Using the {tool.Name.ToLowerInvariant()}..." : "Working with the gloved hand...", true);

            // Whole-leaf actions (bag, remove) aim at the leaf itself, not its small sample tag on the holder clip.
            var wholeLeaf = target != null && target.Definition.Slot == "leaf_whole" && specimen != null && specimen.activeInHierarchy;
            var point = wholeLeaf ? MeasureBounds(specimen).center : target != null ? MeasureBounds(target.gameObject).center : specimenHome;
            var viewer = Camera.main != null ? Camera.main.transform.position : ViewerPosition + Vector3.up * 1.6f;
            var presented = false;
            void Present()
            {
                if (presented)
                    return;
                presented = true;
                RefreshScene();
                panel?.ShowFeedback(result.LessonCompleted ? session.Case.ResultText : result.Message, true);
                Changed?.Invoke();
            }

            if (animator != null)
                yield return animator.StartCoroutine(animator.Use(slot, action.ToolKey, point, viewer, Present));
            Present();
            FinishAction();
        }

        void FinishAction()
        {
            busy = false;
            actionRoutine = null;
            toolInUse = null;
            toolInUseActive = false;
            panel?.SetBusy(false);
            RefreshPanel();
        }

        /// <summary>Stops a running button animation (case change, reset, leaving the lab). The action is already saved.</summary>
        void CancelAction()
        {
            if (!busy)
                return;
            busy = false;
            if (actionRoutine != null)
                StopCoroutine(actionRoutine);
            if (animator != null)
                animator.Cancel();
            actionRoutine = null;
            toolInUse = null;
            toolInUseActive = false;
            if (panel != null)
                panel?.SetBusy(false);
        }

        Transform RackSlot(string toolKey)
        {
            foreach (var pickup in rack)
                if (pickup != null && pickup.ToolKey == toolKey)
                    return pickup.transform;
            return null;
        }

        void Handle(ActionResult result)
        {
            panel?.ShowFeedback(result.Message, result.Accepted);
            if (result.Accepted)
            {
                Save();
                RefreshScene();
                if (result.LessonCompleted)
                    panel?.ShowFeedback(session.Case.ResultText, true);
            }

            RefreshPanel();
            Changed?.Invoke();
        }

        void Save()
        {
            if (session == null || services == null)
                return;
            if (services.Progress.Save(session.Progress))
                services.NotifyProgressChanged(session.Case.Key);
            else
                Debug.LogError("[BTP] " + services.Progress.Error, this);
        }

        void RefreshPanel()
        {
            // The step-lesson task panel was removed from the scene (Manual treatment is the lab's UI); the engine still runs.
            if (session == null || panel == null)
                return;
            panel.Show(session, HeldLine(), TreatmentButtonPlanner.Plan(session, key => services.Content.GetTool(key)?.Name));
        }

        /// <summary>"Holding: ..." plus tools that need cleaning and removed material waiting for disposal.</summary>
        string HeldLine()
        {
            var key = toolInUseActive ? toolInUse : heldTool;
            var tool = key != null ? services.Content.GetTool(key) : null;
            var line = tool == null ? (toolInUseActive ? "Holding: gloved hand" : "Holding: gloved hand (no tool)") : "Holding: " + tool.Name;
            if (toolInUseActive)
                line += "  <color=#22D4ED>(in use)</color>";
            var dirty = session.Case.RackTools.Where(session.IsToolContaminated)
                .Select(k => services.Content.GetTool(k)?.Name ?? k).ToList();
            if (dirty.Count > 0)
                line += $"\n<color=#FBBF24>Needs cleaning: {string.Join(", ", dirty)}</color>";
            if (session.CarriedTargets.Count > 0)
                line += $"\nCarrying {session.CarriedTargets.Count} removed item(s) for disposal";
            return line;
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>Rebuilds every visible state from the saved progress record.</summary>
        void RefreshScene()
        {
            if (session == null)
                return;
            var carried = session.CarriedTargets;
            var marked = MarkedTargets();
            // Parts of an infected original leave the scene with it (they are not left floating).
            var originalRemoved = targets.Any(t => t.Tag == "infected_plant" && session.Progress.GetTargetState(t.TargetId) == "removed_plant");
            foreach (var target in targets)
            {
                var isCarried = carried.Contains(target.TargetId);
                var onOriginal = target.Definition.Slot.StartsWith("plant_") || target.Definition.Slot.StartsWith("leaf_") ||
                                 target.Definition.Slot == "root_zone";
                var state = session.Progress.GetTargetState(target.TargetId);
                if (target.Definition.Slot.StartsWith("leaf_") && (state == "removed" || state == "disposed"))
                    CutMark(target)?.SetActive(!(originalRemoved && onOriginal));
                var visible = isCarried || (session.IsTargetVisible(target.Definition) && !(originalRemoved && onOriginal));
                target.gameObject.SetActive(visible);
                if (!visible)
                    continue;
                target.ApplyState(session.Progress.GetTargetState(target.TargetId));
                target.SetInteractable(!isCarried);
                target.SetMarked(!isCarried && marked.Contains(target));
                if (!isCarried && homes.TryGetValue(target, out var home))
                {
                    target.transform.SetParent(home.parent, false);
                    target.transform.SetLocalPositionAndRotation(home.position, home.rotation);
                }
            }

            RefreshSpecimen();
            if (manualMode)
                ApplyManualVisibility();
        }

        /// <summary>
        /// Switches between the step lesson and the Manual treatment mode. In manual mode the tool rack, lesson targets
        /// and cut marks are hidden and the specimen stands on its holder; the step-lesson progress is saved first and
        /// left untouched, and switching back rebuilds the scene from it.
        /// </summary>
        public void SetManualMode(bool manual)
        {
            if (manualMode == manual)
                return;
            CancelAction();
            Save();
            DropTool();
            manualMode = manual;
            if (toolRack != null)
                toolRack.gameObject.SetActive(!manual);
            if (manual)
                ApplyManualVisibility();
            else
            {
                foreach (var mark in cutMarks.Values)
                    if (mark != null)
                        mark.SetActive(true);
                RefreshScene();
                RefreshPanel();
            }
        }

        void ApplyManualVisibility()
        {
            if (toolRack != null)
                toolRack.gameObject.SetActive(false);
            foreach (var target in targets)
                if (target != null)
                    target.gameObject.SetActive(false);
            foreach (var mark in cutMarks.Values)
                if (mark != null)
                    mark.SetActive(false);
            if (bagOverlay != null)
                bagOverlay.SetActive(false);
            if (tooltipRoot != null)
                tooltipRoot.SetActive(false);
            hovered = null;
            if (specimen != null)
            {
                specimen.SetActive(true);
                specimen.transform.position = specimenHome;
                specimen.transform.localScale = specimenScale;
            }
        }

        /// <summary>Targets that are valid for an available step and not yet counted: shown with a cyan ring.</summary>
        HashSet<TreatmentTarget> MarkedTargets()
        {
            var result = new HashSet<TreatmentTarget>();
            foreach (var step in session.ApplicableSteps)
            {
                if (!session.IsAvailable(step) || TreatmentActions.IsPanelAction(step.Action))
                    continue;
                session.Progress.StepTargets.TryGetValue(step.StepId, out var counted);
                foreach (var target in targets)
                    if (target.Tag == step.TargetTag && (counted == null || !counted.Contains(target.TargetId)))
                        result.Add(target);
            }

            return result;
        }

        /// <summary>The infected original is bagged, moved to containment or removed; it never turns healthy.</summary>
        void RefreshSpecimen()
        {
            if (specimen == null)
                return;
            string state = null;
            foreach (var target in targets)
                if (target.Tag == "infected_plant")
                    state = session.Progress.GetTargetState(target.TargetId);

            var contained = state == "contained" ||
                            (state == "removed_plant" && session.Case.Steps.Any(s => s.Action == TreatmentActions.Contain && session.IsComplete(s)));
            if (contained && bagOverlay == null)
            {
                var prefab = services.Catalog.Resolve<GameObject>("fx.bag_overlay");
                if (prefab != null)
                {
                    bagOverlay = Instantiate(prefab, specimen.transform, false);
                    bagOverlay.name = "ContainmentBag";
                    var local = specimen.transform.InverseTransformVector(specimenBounds.size);
                    bagOverlay.transform.localPosition = specimen.transform.InverseTransformPoint(specimenBounds.center) - Vector3.zero;
                    bagOverlay.transform.localScale = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) * 1.1f;
                }
            }

            if (bagOverlay != null)
                bagOverlay.SetActive(contained);

            if (state == "removed_plant")
            {
                if (contained && slots.TryGetValue("containment", out var containment))
                {
                    specimen.SetActive(true);
                    var fit = Mathf.Min(1f, 0.8f / Mathf.Max(specimenBounds.size.y, 0.01f));
                    specimen.transform.position = containment.position;
                    specimen.transform.localScale = specimenScale * fit;
                }
                else
                {
                    specimen.SetActive(false);
                }
            }
            else
            {
                specimen.SetActive(true);
                specimen.transform.position = specimenHome;
                specimen.transform.localScale = specimenScale;
            }
        }

        public void ShowTooltip(TreatmentTarget target)
        {
            if (tooltipRoot == null || target == null)
                return;
            hovered = target;
            tooltipText.text = target.Label;
            var bounds = MeasureBounds(target.gameObject);
            tooltipRoot.transform.position = bounds.center + Vector3.up * (bounds.extents.y + 0.06f);
            tooltipRoot.SetActive(true);
        }

        public void HideTooltip(TreatmentTarget target)
        {
            if (tooltipRoot == null || hovered != target)
                return;
            hovered = null;
            tooltipRoot.SetActive(false);
        }
    }
}