return string.Join(",", UnityEditor.EditorBuildSettings.scenes.Select(s => s.path + (s.enabled ? "" : "(off)"))) + " runInBackground=" + UnityEngine.Application.runInBackground;
