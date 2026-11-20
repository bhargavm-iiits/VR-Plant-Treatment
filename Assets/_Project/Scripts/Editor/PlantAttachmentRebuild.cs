using System;
using UnityEditor;
using UnityEngine;

namespace BTP.Editor
{
    /// <summary>
    /// Rebuilds every Farm plant after Tools/attach_plant_parts.py has joined their loose leaves, fruit and stems to
    /// the plant (it rewrites the model FBX files and moves the matching wind pivots in the *_Wind.bytes files).
    ///
    /// Reimports the models, re-bakes their wind textures (LeafWindBaker refuses data whose anchors no longer fit
    /// the mesh, so a successful bake confirms the wind files were updated with the geometry), and rebuilds the
    /// plant prefabs, their billboards and the 58 Farm disease variants in place. The Farm scene places those
    /// prefabs as instances, so it picks the changes up without being rebuilt.
    ///
    /// Command line (not -nographics: the billboards are rendered):
    /// Unity.exe -batchmode -projectPath . -executeMethod BTP.Editor.PlantAttachmentRebuild.Run -quit -logFile Tools/logs/plant_attachment_rebuild.log
    /// </summary>
    public static class PlantAttachmentRebuild
    {
        [MenuItem("BTP/Setup/28 - Rebuild Plants After Attachment Pass", priority = 28)]
        public static void RebuildAll()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Exit Play mode before rebuilding the plants.");
            AssetDatabase.Refresh();
            BlenderTreeBuilder.ApplyApple();
            BlenderTreeBuilder.ApplyPeach();
            BlenderTreeBuilder.ApplyGrape();
            BlenderTreeBuilder.ApplyCherry();
            TomatoPlantBuilder.Apply();
            BellPepperPlantBuilder.Apply();
            // Every disease variant, the ten field and tropical crops included (FarmCropBuilder.BuildVariants).
            DiseaseVariantBuilder.BuildAll();
            AssetDatabase.SaveAssets();
            Debug.Log("[BTP] Plants rebuilt after the attachment pass.");
        }

        public static void Run()
        {
            var result = Automation.Run("plant_attachment_rebuild", RebuildAll);
            if (Application.isBatchMode && result != "done")
                EditorApplication.Exit(1);
        }
    }
}