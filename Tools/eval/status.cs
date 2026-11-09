var names = new System.Collections.Generic.List<string>();
foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
{
    if (assembly.GetName().Name.StartsWith("BTP"))
        names.Add(assembly.GetName().Name);
}
return $"assemblies=[{string.Join(",", names)}] compiling={UnityEditor.EditorApplication.isCompiling} updating={UnityEditor.EditorApplication.isUpdating} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path} dirty={UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty}";
