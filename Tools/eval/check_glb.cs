var sb = new System.Text.StringBuilder();
foreach (var path in new[] {
    "Assets/Plant_Asstes/maize_corn_plant.glb",
    "Assets/Plant_Asstes/sci-fi_lab.glb",
    "Assets/Plant_Asstes/circuit-de-spa-francorchamps-2022-layout/source/spa.glb" })
{
    var importer = UnityEditor.AssetImporter.GetAtPath(path);
    var main = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
    var subs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
    sb.AppendLine($"{path}: importer={importer?.GetType().FullName} main={(main == null ? "null" : main.GetType().Name)} subAssets={subs.Length}");
}
sb.AppendLine($"Memory: allocated={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB, gfx={UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024)} MB");
return sb.ToString();
