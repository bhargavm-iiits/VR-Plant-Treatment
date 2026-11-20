using System.Collections.Generic;
using BTP.Core;
using BTP.Farm;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BTP.Treatment
{
    /// <summary>
    /// Carries the selected Farm plant into the Treatment lab. The Farm is unloaded before the Treatment scene loads, so
    /// when the player leaves the Farm with a specimen selected this copies that exact plant's model (stripped to
    /// visuals, with its own material copies), its world scale and yaw and its disease amount into an inactive holder in
    /// XRBootstrap (on the TreatmentServices object). The chamber station instantiates its own copy from the template.
    /// </summary>
    public sealed class FarmSpecimenSnapshot : MonoBehaviour
    {
        public const string SourceFarm = "farm";

        /// <summary>One captured Farm plant.</summary>
        public sealed class Snapshot
        {
            public SpecimenRef Specimen;
            /// <summary>Inactive, stripped clone of the plant's model (never shown itself).</summary>
            public GameObject Template;
            /// <summary>The model's world scale in the Farm.</summary>
            public Vector3 Scale;
            /// <summary>The model's world yaw in the Farm (degrees).</summary>
            public float Yaw;
            public float DiseaseAmount;
            public string Source;
        }

        Transform holder;
        Snapshot current;
        readonly List<Material> materials = new List<Material>();
        EnvironmentController subscribed;

        public static FarmSpecimenSnapshot Instance { get; private set; }

        /// <summary>The latest capture, or null.</summary>
        public Snapshot Current => current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        void Start() => Subscribe();

        void OnEnable()
        {
            if (Instance == null)
                Instance = this;
            Subscribe();
        }

        void OnDisable()
        {
            if (subscribed != null)
                subscribed.Exiting -= OnExiting;
            subscribed = null;
        }

        void OnDestroy()
        {
            Clear();
            if (Instance == this)
                Instance = null;
        }

        void Subscribe()
        {
            // EnvironmentController may wake after this component; Start retries once it exists.
            var controller = EnvironmentController.Instance;
            if (controller == null || controller == subscribed)
                return;
            if (subscribed != null)
                subscribed.Exiting -= OnExiting;
            subscribed = controller;
            subscribed.Exiting += OnExiting;
        }

        void OnExiting(EnvironmentId leaving, Scene scene)
        {
            if (leaving != EnvironmentId.Farm)
                return;
            var specimen = SelectionState.Instance != null ? SelectionState.Instance.SelectedSpecimen : null;
            if (specimen == null)
                return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var planting in root.GetComponentsInChildren<SelectablePlant>(true))
            {
                if (!planting.Owns(specimen))
                    continue;
                if (Capture(planting, specimen.PlantIndex, specimen) != null)
                    Debug.Log($"[BTP] Captured Farm specimen {specimen.SpecimenId} for the treatment chamber.");
                return;
            }

            Debug.LogWarning($"[BTP] The selected specimen {specimen.SpecimenId} was not found in the Farm; the chamber will show the crop's Farm model.");
        }

        /// <summary>
        /// Captures plant <paramref name="index"/> of <paramref name="planting"/> (replacing any earlier capture). Used on
        /// Farm exit, and by tests and the verification tool.
        /// </summary>
        public Snapshot Capture(SelectablePlant planting, int index, SpecimenRef specimen = null)
        {
            if (planting == null)
                return null;
            var plant = planting.PlantAt(index);
            var model = SpecimenGeometry.ModelOf(plant);
            if (model == null)
            {
                Debug.LogWarning($"[BTP] Farm plant {planting.name} #{index + 1} has no model to capture.");
                return null;
            }

            Clear();
            EnsureHolder();
            var clone = Instantiate(model, holder, false);
            clone.name = model.name + " (Farm specimen)";
            clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;
            SpecimenGeometry.Strip(clone);
            materials.AddRange(SpecimenGeometry.CloneMaterials(clone));
            current = new Snapshot
            {
                Specimen = specimen ?? planting.SpecimenAt(index),
                Template = clone,
                Scale = model.transform.lossyScale,
                Yaw = model.transform.eulerAngles.y,
                DiseaseAmount = SpecimenGeometry.DiseaseAmount(model),
                Source = SourceFarm,
            };
            return current;
        }

        /// <summary>The capture of <paramref name="specimen"/> (same specimen ID and case), if there is one.</summary>
        public bool TryGet(SpecimenRef specimen, out Snapshot snapshot)
        {
            snapshot = null;
            if (specimen == null || current == null || current.Template == null)
                return false;
            if (current.Specimen.SpecimenId != specimen.SpecimenId || !current.Specimen.IsCase(specimen.PlantId, specimen.DiseaseId))
                return false;
            snapshot = current;
            return true;
        }

        void EnsureHolder()
        {
            if (holder != null)
                return;
            var go = new GameObject("FarmSpecimenSnapshot (inactive)");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            holder = go.transform;
        }

        /// <summary>Forgets the capture and destroys its clone and material copies.</summary>
        public void Clear()
        {
            if (current != null && current.Template != null)
            {
                if (Application.isPlaying)
                    Destroy(current.Template);
                else
                    DestroyImmediate(current.Template);
            }

            foreach (var material in materials)
            {
                if (material == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }

            materials.Clear();
            current = null;
        }
    }
}
