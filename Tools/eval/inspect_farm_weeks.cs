// Reports the Farm six-week timeline wiring in the open scene.
var sb = new System.Text.StringBuilder();
var driver = UnityEngine.Object.FindAnyObjectByType<BTP.Farm.FarmCropRecovery>(UnityEngine.FindObjectsInactive.Include);
sb.Append($"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path} driver={(driver != null)} ");
if (driver != null)
{
    var so = new UnityEditor.SerializedObject(driver);
    var crops = so.FindProperty("crops");
    for (var i = 0; i < crops.arraySize; i++)
    {
        var c = crops.GetArrayElementAtIndex(i);
        sb.Append($"{c.FindPropertyRelative("plantId").stringValue}:{c.FindPropertyRelative("materials").arraySize} ");
    }
}
var console = UnityEngine.Object.FindAnyObjectByType<BTP.Farm.FarmTimelineConsole>(UnityEngine.FindObjectsInactive.Include);
var gallery = UnityEngine.Object.FindAnyObjectByType<BTP.Farm.DiseaseGalleryController>(UnityEngine.FindObjectsInactive.Include);
sb.Append($"console={(console != null)} galleryConsole={(gallery != null && gallery.Console == console)}");
return sb.ToString();
