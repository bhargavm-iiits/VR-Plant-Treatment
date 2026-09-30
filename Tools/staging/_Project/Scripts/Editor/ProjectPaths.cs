using System.IO;
using UnityEditor;

namespace BTP.Editor
{
    /// <summary>Asset locations used by the setup tools.</summary>
    static class ProjectPaths
    {
        public const string Root = "Assets/_Project";
        public const string SourcePlants = "Assets/Plant_Asstes";

        public const string Models = Root + "/Models/Plants";
        public const string BillboardMeshes = Root + "/Models/Billboards";
        public const string PlantTextures = Root + "/Textures/Plants";
        public const string BillboardTextures = Root + "/Textures/Billboards";
        public const string PlantMaterials = Root + "/Materials/Plants";
        public const string EnvironmentMaterials = Root + "/Materials/Environment";
        public const string UIMaterials = Root + "/Materials/UI";
        public const string PlantPrefabs = Root + "/Prefabs/Plants";
        public const string EnvironmentPrefabs = Root + "/Prefabs/Environment";
        public const string Data = Root + "/Data";
        public const string PlantData = Data + "/Plants";
        public const string Shaders = Root + "/Shaders";
        public const string Resources = Root + "/Resources/BTP";
        public const string Meshes = Root + "/Models/Generated";

        public const string Catalog = Data + "/PlantCatalog.asset";
        public const string RecoveryData = Data + "/TomatoRecoveryData.asset";

        public const string Scenes = "Assets/Scenes";
        public const string BootstrapScene = Scenes + "/XRBootstrap.unity";
        // The Farm lives in the project's existing environment scene (name kept as created).
        public const string FarmScene = Scenes + "/Envirornment.unity";
        public const string LabScene = Scenes + "/Lab.unity";

        /// <summary>Creates an asset folder (and its parents) if missing.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
