using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Import settings sized for Quest 2 first (ASTC on Android), BC7 on PC.</summary>
    static class TextureSettings
    {
        // maxSize applies to PC (BC7); androidMax to Quest (ASTC). Quest 2 and Quest 3 share one Android
        // build, so the Android size is the Quest 2 budget; PC VR gets the full-size textures.
        public static void ConfigureAlbedo(string path, int maxSize = 4096, int androidMax = 2048) =>
            Configure(path, maxSize, androidMax, TextureImporterType.Default, true, false, TextureImporterFormat.ASTC_6x6, 4);

        public static void ConfigureCutout(string path, int maxSize = 4096, int androidMax = 2048) =>
            Configure(path, maxSize, androidMax, TextureImporterType.Default, true, true, TextureImporterFormat.ASTC_6x6, 4);

        public static void ConfigureNormal(string path, int maxSize = 2048, int androidMax = 1024) =>
            Configure(path, maxSize, androidMax, TextureImporterType.NormalMap, false, false, TextureImporterFormat.ASTC_6x6, 2);

        /// <summary>Data texture (disease fields): linear, clamped, higher-quality ASTC.</summary>
        public static void ConfigureData(string path, int maxSize = 1024)
        {
            Configure(path, maxSize, maxSize, TextureImporterType.Default, false, false, TextureImporterFormat.ASTC_4x4, 1);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        static void Configure(string path, int maxSize, int androidMax, TextureImporterType type, bool sRGB, bool cutout,
            TextureImporterFormat androidFormat, int anisoLevel)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException($"Texture not found: {path}");

            importer.textureType = type;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.alphaSource = cutout ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = cutout;
            importer.mipMapsPreserveCoverage = cutout;
            importer.alphaTestReferenceValue = 0.5f;
            importer.isReadable = false;
            importer.anisoLevel = anisoLevel;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;

            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = androidMax,
                format = androidFormat,
                compressionQuality = 50
            });
            importer.SaveAndReimport();
        }
    }
}
