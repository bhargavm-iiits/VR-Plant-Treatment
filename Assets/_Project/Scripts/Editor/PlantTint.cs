using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Yellowed material variants for some decorative farm plants, matching the reference's
    /// visible signs of unhealthy foliage. Purely visual; no disease data is attached.
    /// </summary>
    static class PlantTint
    {
        static readonly Color Stressed = new Color(1.0f, 0.86f, 0.45f);

        public static void ApplyStressedFoliage(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    materials[i] = StressedVariant(materials[i]);
                renderer.sharedMaterials = materials;
            }
        }

        static Material StressedVariant(Material source)
        {
            if (source == null)
                return null;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || sourcePath.EndsWith("_Stressed.mat"))
                return source;

            var path = sourcePath.Replace(".mat", "_Stressed.mat");
            var variant = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (variant == null)
            {
                variant = new Material(source);
                AssetDatabase.CreateAsset(variant, path);
            }
            else
            {
                variant.CopyPropertiesFromMaterial(source);
            }

            variant.SetColor("_BaseColor", Stressed);
            EditorUtility.SetDirty(variant);
            return variant;
        }
    }
}
