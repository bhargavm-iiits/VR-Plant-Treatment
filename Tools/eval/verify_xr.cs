var sb = new System.Text.StringBuilder();
foreach (var group in new[] { UnityEditor.BuildTargetGroup.Android, UnityEditor.BuildTargetGroup.Standalone })
{
    var general = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
    var loaders = general != null && general.AssignedSettings != null
        ? string.Join(",", general.AssignedSettings.activeLoaders.Select(l => l.GetType().Name))
        : "none";
    var openxr = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(group);
    var enabled = openxr.GetFeatures().Where(f => f.enabled).Select(f => f.GetType().Name);
    sb.AppendLine($"{group}: loaders=[{loaders}] initOnStart={general?.InitManagerOnStart} renderMode={openxr.renderMode}");
    sb.AppendLine($"   features: {string.Join(", ", enabled)}");
}
sb.AppendLine($"Android: apis={string.Join(",", UnityEditor.PlayerSettings.GetGraphicsAPIs(UnityEditor.BuildTarget.Android))} " +
              $"backend={UnityEditor.PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android)} " +
              $"arch={UnityEditor.PlayerSettings.Android.targetArchitectures} min={UnityEditor.PlayerSettings.Android.minSdkVersion} " +
              $"id={UnityEditor.PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)}");
sb.AppendLine($"Windows: apis={string.Join(",", UnityEditor.PlayerSettings.GetGraphicsAPIs(UnityEditor.BuildTarget.StandaloneWindows64))}");
sb.AppendLine($"Teleport layer mask: {UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask.GetMask("Teleport")}");
return sb.ToString();
