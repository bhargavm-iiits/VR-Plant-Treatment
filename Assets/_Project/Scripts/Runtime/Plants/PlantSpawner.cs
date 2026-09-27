using BTP.UI;
using TMPro;
using UnityEngine;

namespace BTP.Plants
{
    public enum PlantVariant
    {
        Farm,
        Lab
    }

    /// <summary>
    /// Instantiates plant models. A missing model produces a visible setup error (Editor and
    /// development builds) instead of silently showing another species.
    /// </summary>
    public static class PlantSpawner
    {
        const string ErrorMaterialResource = "BTP/SetupErrorMaterial";

        public static GameObject Spawn(PlantDefinition plant, PlantVariant variant, Transform parent)
        {
            var prefab = plant == null ? null : variant == PlantVariant.Farm ? plant.FarmPrefab : plant.LabPrefab;
            if (prefab != null)
            {
                var instance = Object.Instantiate(prefab, parent, false);
                instance.name = prefab.name;
                return instance;
            }

            var message = plant == null
                ? "Plant definition missing"
                : $"{plant.DisplayName} {variant.ToString().ToLowerInvariant()} model missing";
            Debug.LogError($"[BTP] Setup error: {message}.");
            return Debug.isDebugBuild ? CreateSetupErrorMarker(message, parent) : null;
        }

        /// <summary>A red block with a label that names the missing asset.</summary>
        public static GameObject CreateSetupErrorMarker(string message, Transform parent)
        {
            var root = new GameObject("SETUP ERROR");
            root.transform.SetParent(parent, false);

            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Marker";
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            block.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
            var errorMaterial = Resources.Load<Material>(ErrorMaterialResource);
            if (errorMaterial != null)
                block.GetComponent<Renderer>().sharedMaterial = errorMaterial;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshPro)).GetComponent<TextMeshPro>();
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            label.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);
            label.font = TMP_Settings.defaultFontAsset;
            label.text = $"SETUP ERROR\n{message}";
            label.fontSize = 0.6f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.4f, 0.4f);
            label.gameObject.AddComponent<FaceCamera>();
            return root;
        }
    }
}
