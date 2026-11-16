// Renders Apple_Farm.prefab in a temporary preview scene to Tools/out/apple_preview.png.
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/Prefabs/Plants/Apple_Farm.prefab");
var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
var go = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(prefab);
UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
var lightGo = new UnityEngine.GameObject("Sun"); var light = lightGo.AddComponent<UnityEngine.Light>(); light.type = UnityEngine.LightType.Directional; light.intensity = 1.3f; lightGo.transform.rotation = UnityEngine.Quaternion.Euler(45, 30, 0);
UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
var camGo = new UnityEngine.GameObject("Cam"); var cam = camGo.AddComponent<UnityEngine.Camera>(); cam.scene = scene; cam.backgroundColor = new UnityEngine.Color(0.62f, 0.78f, 0.92f); cam.clearFlags = UnityEngine.CameraClearFlags.SolidColor; cam.fieldOfView = 45;
UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
var b = new UnityEngine.Bounds(go.transform.position, UnityEngine.Vector3.zero); foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>()) if (r.name == "Model") b = r.bounds;
camGo.transform.position = b.center + new UnityEngine.Vector3(0, 0.4f, -b.size.y * 1.45f); camGo.transform.LookAt(b.center);
var rt = new UnityEngine.RenderTexture(1200, 1000, 24); cam.targetTexture = rt; cam.Render();
UnityEngine.RenderTexture.active = rt; var tex = new UnityEngine.Texture2D(1200, 1000, UnityEngine.TextureFormat.RGB24, false); tex.ReadPixels(new UnityEngine.Rect(0, 0, 1200, 1000), 0, 0); tex.Apply();
System.IO.File.WriteAllBytes("Tools/out/apple_preview.png", tex.EncodeToPNG());
UnityEngine.RenderTexture.active = null; cam.targetTexture = null;
UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
return "size=" + b.size;
