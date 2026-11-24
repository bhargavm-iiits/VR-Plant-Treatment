using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace BTP.Editor
{
    public static class QuestDemoBuild
    {
        const string OutputPath = "Builds/Quest/PlantDiseaseVR.apk";
        const string ApplicationId = "com.btp.plantdiseasevr";

        [MenuItem("BTP/Build/Quest Demo APK")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var requiredScenes = new[] { ProjectPaths.BootstrapScene, ProjectPaths.FarmScene, ProjectPaths.LabScene, ProjectPaths.TreatmentScene };
            if (scenes.FirstOrDefault() != ProjectPaths.BootstrapScene || requiredScenes.Any(scene => !scenes.Contains(scene)))
                throw new BuildFailedException("Enable XRBootstrap first, followed by the Farm, Lab and Treatment scenes in Build Settings.");

            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != ApplicationId)
                throw new BuildFailedException($"The Quest web launcher expects the Android application ID {ApplicationId}.");

            // Native hand tracking (Addendum 5): the Android OpenXR hand features must be on in the build.
            XRProjectSetup.EnsureQuestHandTracking();
            // Where the chamber specimen's leaves are (the crop meshes are not CPU-readable on the Quest): the sprayer's wet beads.
            SpecimenSurfaceBaker.Bake();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var previousBundleSetting = EditorUserBuildSettings.buildAppBundle;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                var report = BuildPipeline.BuildPlayer(scenes, OutputPath, BuildTarget.Android, BuildOptions.None);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Quest demo build {report.summary.result} with {report.summary.totalErrors} error(s).");
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundleSetting;
            }
        }
    }
}
