using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Creates or updates URP material assets with correctly synchronised keywords.</summary>
    static class MaterialFactory
    {
        public const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";
        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";
        public const string LeafDiseaseShader = "BTP/Leaf Disease";
        public const string FarmRecoveryShader = "BTP/Farm Recovery";
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
            // Alpha to coverage (needs MSAA, on in both URP assets) smooths alpha-clipped leaf edges when the shader supports it.
            if (material.HasProperty("_AlphaToMask"))
                material.SetFloat("_AlphaToMask", cutout ? 1f : 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_Smoothness", 0.15f);
            material.enableInstancing = true;
            BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Farm plant or billboard material for the six-week timeline (BTP/Farm Recovery): healthy albedo in
        /// _BaseMap, diseased albedo in _DiseasedMap, per-texel recovery thresholds in _RecoveryMask. The asset
        /// keeps _DiseaseAmount = 1 (diseased); the Farm scene drives runtime copies per crop.
        /// </summary>
        public static Material FarmRecovery(string path, Texture healthy, Texture diseased, Texture recoveryMask, Texture normal,
            bool cutout, float maskScale = 1f)
        {
            var material = GetOrCreate(path, FarmRecoveryShader);
            material.SetTexture("_BaseMap", healthy);
            material.SetTexture("_DiseasedMap", diseased);
            material.SetTexture("_RecoveryMask", recoveryMask);
            material.SetFloat("_MaskScale", maskScale);
            material.SetFloat("_DiseaseAmount", 1f);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            material.SetFloat("_AlphaToMask", cutout ? 1f : 0f);
            material.SetFloat("_Cull", cutout ? 0f : 2f);
            if (cutout)
                material.EnableKeyword("_ALPHATEST_ON");
            else
                material.DisableKeyword("_ALPHATEST_ON");
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Simple Lit specular highlights from a constant colour, for glossy surfaces such as cherry skin. Note that
        /// Simple Lit's _SpecularHighlights is a SpecularSource enum (0 = texture/colour, 1 = none), not an on/off
        /// toggle. The GUI keyword sync copies <paramref name="smoothness"/> into _SpecColor.a.
        /// </summary>
        public static void Glossy(Material material, float smoothness, Color specular)
        {
            material.SetFloat("_SpecularHighlights", (float)SimpleLitGUI.SpecularSource.SpecularTextureAndColor);
            material.SetFloat("_SmoothnessSource", (float)SimpleLitGUI.SmoothnessMapChannel.SpecularAlpha);
            material.SetColor("_SpecColor", specular);
            material.SetFloat("_Smoothness", smoothness);
            BaseShaderGUI.SetMaterialKeywords(material, SimpleLitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// BTP/Berry Impostor material (round berries, see BerryImpostors) with the colour and the specular gloss of
        /// a Simple Lit fruit material <paramref name="source"/>, so the berries look as they did, only round.
        /// </summary>
        public static Material BerryImpostor(string path, Material source)
        {
            var material = GetOrCreate(path, BerryImpostors.Shader);
            material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            var specular = source.HasProperty("_SpecColor") ? source.GetColor("_SpecColor") : new Color(0.5f, 0.5f, 0.5f);
            material.SetColor("_SpecColor", new Color(specular.r, specular.g, specular.b, 1f));
            material.SetFloat("_Smoothness", source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : 0.6f);
            material.SetFloat("_BerryUnlit", 0f);
            material.DisableKeyword("_BERRY_UNLIT");
            material.enableInstancing = true;
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

        /// <summary>
        /// URP Lit material (metallic workflow) for the few surfaces that need real reflections: the greenhouse
        /// frame, its wet-concrete slab and its glass. With a <paramref name="metallicGloss"/> mask, URP reads
        /// metallic from R and multiplies <paramref name="smoothness"/> by A. Transparent materials alpha-blend
        /// (alpha from <paramref name="color"/>) without depth writes; double-sided ones render both faces.
        /// <paramref name="receiveShadows"/> = false compiles the shadow lookups out; <paramref name="queueOffset"/>
        /// (-50..50) moves the material within its render queue (transparent: sorted before or after the others).
        /// </summary>
        public static Material Lit(string path, Color color, float metallic, float smoothness, Texture albedo = null,
            Texture normal = null, Texture metallicGloss = null, bool transparent = false, bool doubleSided = false,
            float normalScale = 1f, bool receiveShadows = true, int queueOffset = 0)
        {
            var material = GetOrCreate(path, LitShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", normalScale);
            material.SetTexture("_MetallicGlossMap", metallicGloss);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Cull", doubleSided ? 0f : 2f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            material.SetFloat("_ReceiveShadows", receiveShadows ? 1f : 0f);
            material.SetFloat("_QueueOffset", Mathf.Clamp(queueOffset, -50, 50));
            material.enableInstancing = true;
            // Also applies _ReceiveShadows (_RECEIVE_SHADOWS_OFF) and the render queue (surface type + _QueueOffset).
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
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

        /// <summary>
        /// Material for the illustrative Lab leaf disease (BTP/Leaf Disease). The Tomato uses its vertex stem mask and the
        /// shader's default colours; other plants pass <paramref name="useVertexStemMask"/> = false and their own lesion colours.
        /// </summary>
        public static Material LeafDisease(string path, Texture albedo, Texture normal, Texture diseaseMask,
            bool useVertexStemMask = true, Color? lesion = null, Color? lesionRing = null, Color? halo = null, bool cutout = false)
        {
            var material = GetOrCreate(path, LeafDiseaseShader);
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_DiseaseMask", diseaseMask);
            material.SetFloat("_UseVertexStemMask", useVertexStemMask ? 1f : 0f);
            if (lesion.HasValue)
                material.SetColor("_LesionColor", lesion.Value);
            if (lesionRing.HasValue)
                material.SetColor("_LesionRingColor", lesionRing.Value);
            if (halo.HasValue)
                material.SetColor("_HaloColor", halo.Value);
            material.SetFloat("_Cutoff", 0.5f);
            material.SetFloat("_Cull", cutout ? 0f : 2f);
            if (cutout)
                material.EnableKeyword("_ALPHATEST_ON");
            else
                material.DisableKeyword("_ALPHATEST_ON");
            if (normal != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
