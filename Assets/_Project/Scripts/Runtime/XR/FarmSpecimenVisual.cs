using System.Collections.Generic;
using BTP.Farm;
using UnityEngine;

namespace BTP.XR
{
    /// <summary>
    /// The confirmed treatment result of one connected specimen, shown on that plant only. It gives the plant's renderers
    /// (near mesh and billboard LOD) their own copies of the Farm recovery materials and blends their _DiseaseAmount, so
    /// the crop's other plants and the demo timeline (<see cref="FarmRecoveryState"/>, per crop) are unaffected. Added at
    /// runtime to the specimen's plant; the copies are destroyed with the Farm scene.
    /// </summary>
    public sealed class FarmSpecimenVisual : MonoBehaviour
    {
        static readonly int DiseaseAmountId = Shader.PropertyToID("_DiseaseAmount");

        readonly List<Material> instances = new List<Material>();
        float from;
        float to = 1f;
        float current = 1f;
        float elapsed;
        bool animating;
        bool captured;

        public float CurrentAmount => current;
        public float TargetAmount => to;
        public int MaterialCount => instances.Count;

        /// <summary>Shows <paramref name="amount"/> (1 diseased, 0 healthy); blends over the Farm's transition time when <paramref name="animate"/>.</summary>
        public void Show(float amount, bool animate)
        {
            Capture();
            amount = Mathf.Clamp01(amount);
            from = current;
            to = amount;
            elapsed = 0f;
            animating = animate && !Mathf.Approximately(from, to);
            if (!animating)
            {
                current = to;
                Apply();
            }
        }

        /// <summary>Copies the plant's recovery materials once, keeping their current disease amount.</summary>
        void Capture()
        {
            if (captured)
                return;
            captured = true;
            var copies = new Dictionary<Material, Material>();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var shared = materials[i];
                    if (shared == null || !shared.HasProperty(DiseaseAmountId))
                        continue;
                    if (!copies.TryGetValue(shared, out var copy))
                    {
                        copy = new Material(shared) { name = shared.name + " (specimen)" };
                        copies[shared] = copy;
                        instances.Add(copy);
                    }

                    materials[i] = copy;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = materials;
            }

            current = instances.Count > 0 ? instances[0].GetFloat(DiseaseAmountId) : 1f;
            if (instances.Count == 0)
                Debug.LogWarning($"[BTP][XR] Specimen '{name}' has no Farm recovery material; its treatment result cannot be shown.", this);
        }

        void Update()
        {
            if (!animating)
                return;
            elapsed += Time.deltaTime;
            var t = elapsed / FarmRecoveryMath.TransitionSeconds;
            current = Mathf.Lerp(from, to, FarmRecoveryMath.Ease(t));
            if (t >= 1f)
            {
                current = to;
                animating = false;
            }

            Apply();
        }

        void Apply()
        {
            foreach (var material in instances)
                material.SetFloat(DiseaseAmountId, current);
        }

        void OnDestroy()
        {
            foreach (var material in instances)
                if (material != null)
                    Destroy(material);
            instances.Clear();
        }
    }
}
