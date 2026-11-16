using System;
using System.Collections.Generic;
using BTP.Lab;
using UnityEngine;

namespace BTP.Farm
{
    /// <summary>
    /// Drives the BTP/Farm Recovery materials of every Farm crop from <see cref="FarmRecoveryState"/>. On start it
    /// gives each crop runtime copies of its farm materials (near mesh and billboard) and puts them on every
    /// renderer in the scene that used the assets, so the specimen, its rows or orchard trees and their billboard
    /// LODs change together while other crops keep their own week. One material per crop and texture keeps the
    /// SRP Batcher working; a week change blends _DiseaseAmount over 1.6 s. The material assets stay diseased.
    /// </summary>
    public sealed class FarmCropRecovery : MonoBehaviour
    {
        static readonly int DiseaseAmountId = Shader.PropertyToID("_DiseaseAmount");

        [Serializable]
        public sealed class Crop
        {
            public string plantId;
            public List<Material> materials = new List<Material>();
        }

        [SerializeField] TomatoRecoveryData data;
        [SerializeField] List<Crop> crops = new List<Crop>();

        sealed class State
        {
            public Material[] Instances;
            public float From;
            public float To;
            public float Current;
            public float Elapsed;
            public bool Animating;
        }

        readonly Dictionary<string, State> states = new Dictionary<string, State>();
        readonly List<Material> created = new List<Material>();

        public TomatoRecoveryData Data => data;

        internal void Bind(TomatoRecoveryData recoveryData, List<Crop> cropMaterials)
        {
            data = recoveryData;
            crops = cropMaterials;
        }

        /// <summary>The disease amount currently shown for a crop (1 diseased, 0 healthy); 1 for unknown crops.</summary>
        public float CurrentAmount(string plantId) => states.TryGetValue(plantId, out var state) ? state.Current : 1f;

        public bool IsAnimating(string plantId) => states.TryGetValue(plantId, out var state) && state.Animating;

        void Awake()
        {
            var replacements = new Dictionary<Material, Material>();
            foreach (var crop in crops)
            {
                if (crop == null || string.IsNullOrEmpty(crop.plantId))
                    continue;
                var instances = new List<Material>();
                foreach (var asset in crop.materials)
                {
                    if (asset == null || replacements.ContainsKey(asset))
                        continue;
                    var instance = new Material(asset) { name = asset.name + " (runtime)" };
                    replacements[asset] = instance;
                    instances.Add(instance);
                    created.Add(instance);
                }

                var amount = FarmRecoveryMath.DiseaseAmount(data, FarmRecoveryState.GetWeek(crop.plantId));
                var state = new State { Instances = instances.ToArray(), From = amount, To = amount, Current = amount };
                states[crop.plantId] = state;
                Apply(state);
            }

            // Swap once: every renderer that uses a crop's material asset gets that crop's runtime copy.
            foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && replacements.TryGetValue(materials[i], out var instance))
                    {
                        materials[i] = instance;
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        void OnEnable() => FarmRecoveryState.WeekChanged += OnWeekChanged;

        void OnDisable() => FarmRecoveryState.WeekChanged -= OnWeekChanged;

        void OnDestroy()
        {
            foreach (var material in created)
            {
                if (material != null)
                    Destroy(material);
            }

            created.Clear();
        }

        void OnWeekChanged(string plantId, int week)
        {
            if (!states.TryGetValue(plantId, out var state))
                return;
            state.From = state.Current;
            state.To = FarmRecoveryMath.DiseaseAmount(data, week);
            state.Elapsed = 0f;
            state.Animating = !Mathf.Approximately(state.From, state.To);
            if (!state.Animating)
            {
                state.Current = state.To;
                Apply(state);
            }
        }

        void Update()
        {
            foreach (var state in states.Values)
            {
                if (!state.Animating)
                    continue;
                state.Elapsed += Time.deltaTime;
                var t = state.Elapsed / FarmRecoveryMath.TransitionSeconds;
                state.Current = Mathf.Lerp(state.From, state.To, FarmRecoveryMath.Ease(t));
                if (t >= 1f)
                {
                    state.Current = state.To;
                    state.Animating = false;
                }

                Apply(state);
            }
        }

        static void Apply(State state)
        {
            foreach (var material in state.Instances)
                material.SetFloat(DiseaseAmountId, state.Current);
        }
    }
}
