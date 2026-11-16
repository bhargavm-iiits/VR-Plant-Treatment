foreach (var path in new[] { "Assets/_Project/Scripts/Runtime/Lab/LeafViewerFacing.cs", "Assets/_Project/Scripts/Runtime/Lab/LeafRecoveryVisual.cs", "Assets/_Project/Scripts/Runtime/Lab/LabPlantDisplay.cs", "Assets/_Project/Scripts/Runtime/Lab/RecoveryDemoPanel.cs", "Assets/_Project/Scripts/Runtime/Lab/TomatoRecoveryData.cs", "Assets/_Project/Tests/EditMode/LeafDisplayTests.cs", "Assets/_Project/Tests/EditMode/RecoveryDemoTests.cs" })
    UnityEditor.AssetDatabase.ImportAsset(path,UnityEditor.ImportAssetOptions.ForceUpdate);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache);
return "Requested fresh compilation of runtime and test scripts";
