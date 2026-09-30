using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>Import settings sized for Quest 2 first (ASTC on Android), BC7 on PC.</summary>
    static class TextureSettings
    {
        public static void ConfigureAlbedo(string path, int maxSize = 2048) =>
            Configure(path, maxSize, TextureImporterType.Default, true, false, TextureImporterFormat.ASTC_6x6);

        public static void ConfigureCutout(string path, int maxSize = 2048) =>
            Configure(path, maxSize, TextureImporterType.Default, true, true, TextureImporterFormat.ASTC_6x6);

        public static void ConfigureNormal(string path, int maxSize = 1024) =>
            Configure(path, maxSize, TextureImporterType.NormalMap, false, false, TextureImporterFormat.ASTC_6x6);

        /// <summary>Data texture (disease fields): linear, clamped, higher-quality ASTC.</summary>
        public static void ConfigureData(string path, int maxSize = 1024)
        {
            Configure(path, maxSize, TextureImporterType.Default, false, false, TextureImporterFormat.ASTC_4x4);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        static void Configure(string path, int maxSize, TextureImporterType type, bool sRGB, bool cutout,
            TextureImporterFormat androidFormat)
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

            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = maxSize,
                format = androidFormat,
                compressionQuality = 50
            });
            importer.SaveAndReimport();
        }
    }
}
