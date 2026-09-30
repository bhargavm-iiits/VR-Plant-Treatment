var log = new System.Text.StringBuilder();
void ImportSample(string package, string name)
{
    foreach (var sample in UnityEditor.PackageManager.UI.Sample.FindByPackage(package, string.Empty))
    {
        if (sample.displayName != name)
            continue;
        if (sample.isImported)
        {
            log.AppendLine($"{name}: already imported at {sample.importPath}");
            return;
        }
        var ok = sample.Import(UnityEditor.PackageManager.UI.Sample.ImportOptions.OverridePreviousImports |
                               UnityEditor.PackageManager.UI.Sample.ImportOptions.HideImportWindow);
        log.AppendLine($"{name}: import {(ok ? "ok" : "FAILED")} -> {sample.importPath}");
        return;
    }
    log.AppendLine($"{name}: NOT FOUND in {package}");
}

ImportSample("com.unity.xr.interaction.toolkit", "Starter Assets");
ImportSample("com.unity.xr.hands", "HandVisualizer");
ImportSample("com.unity.xr.interaction.toolkit", "Hands Interaction Demo");
ImportSample("com.unity.xr.interaction.toolkit", "XR Interaction Simulator");
return log.ToString();
