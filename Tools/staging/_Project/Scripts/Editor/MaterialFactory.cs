using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Creates or updates URP material assets with correctly synchronised keywords.</summary>
    static class MaterialFactory
    {
        public const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";
        public const string LeafDiseaseShader = "BTP/Leaf Disease";
        public const string GlowShader = "BTP/Unlit Glow";
        public const string FadeShader = "BTP/Fade Overlay";

        /// <summary>Loads the material at <paramref name="path"/>, creating it with <paramref name="shaderName"/> if needed.</summary>
        public static Material GetOrCreate(string path, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException($"Shader '{shaderName}' not found.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                ProjectPaths.EnsureFolder(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'));
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        /// <summary>
        /// Simple Lit foliage/surface material. Cutout materials alpha-clip and render both faces
        /// (leaf cards). Specular highlights are off: cheaper on Quest and right for matte leaves.
        /// </summary>
        public static Material SimpleLit(string path, Texture albedo, Texture normal, bool cutout, Color? tint = null)
        {
            var material = GetOrCreate(path, SimpleLitShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", tint ?? Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_Surface", 0f);
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Cull", cutout ? 0f : 2f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_Smoothness", 0.15f);
            material.enableInstancing = true;
            BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Plain coloured Simple Lit material for environment geometry.</summary>
        public static Material Solid(string path, Color color, float smoothness = 0.1f, Texture albedo = null)
        {
            var material = SimpleLit(path, albedo, null, false, color);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        public static Material Unlit(string path, Color color, bool transparent = false)
        {
            var material = GetOrCreate(path, UnlitShader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            BaseShaderGUI.SetMaterialKeywords(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material Glow(string path, Color color, float intensity)
        {
            var material = GetOrCreate(path, GlowShader);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Intensity", intensity);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Material for the illustrative tomato leaf disease (BTP/Leaf Disease).</summary>
        public static Material LeafDisease(string path, Texture albedo, Texture normal, Texture diseaseMask)
        {
            var material = GetOrCreate(path, LeafDiseaseShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_DiseaseMask", diseaseMask);
            material.SetFloat("_UseVertexStemMask", 1f);
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
