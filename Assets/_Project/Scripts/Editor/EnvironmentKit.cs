using BTP.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BTP.Editor
{
    /// <summary>Primitive-based building blocks for the Farm and Lab environments.</summary>
    static class EnvironmentKit
    {
        const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                              StaticEditorFlags.OccludeeStatic;

        public static GameObject Group(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group;
        }

        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material,
            bool collider = true, float yaw = 0f, bool castShadows = true)
        {
            return Primitive(PrimitiveType.Cube, name, parent, center, size, material, collider, Quaternion.Euler(0f, yaw, 0f), castShadows);
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 center, float radius, float height,
            Material material, bool collider = true, bool castShadows = true)
        {
            // Unity's cylinder is 2 units tall and 1 unit wide.
            return Primitive(PrimitiveType.Cylinder, name, parent, center, new Vector3(radius * 2f, height * 0.5f, radius * 2f),
                material, collider, Quaternion.identity, castShadows);
        }

        public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 center, Vector3 scale,
            Material material, bool collider, Quaternion rotation, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(center, rotation);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
            return go;
        }

        public static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material, Vector3 position,
            Quaternion rotation, Vector3 scale, bool castShadows = false, bool isStatic = true)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (isStatic)
                GameObjectUtility.SetStaticEditorFlags(go, StaticFlags);
            return go;
        }

        /// <summary>Instantiates a prefab, keeping the prefab link, with position, yaw and uniform scale.</summary>
        public static GameObject Place(GameObject prefab, Transform parent, Vector3 position, float yaw, float scale = 1f)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one * scale;
            return instance;
        }

        /// <summary>Sun, sky, ambient light and haze for an environment scene.</summary>
        public static Light ConfigureLighting(Transform parent, Color sunColor, float sunIntensity, Vector3 sunEuler,
            Color skyTint, Color ambientSky, Color ambientEquator, Color ambientGround, Color fogColor, float fogStart, float fogEnd,
            string skyboxPath)
        {
            var sky = MaterialFactory.GetOrCreate(skyboxPath, "Skybox/Procedural");
            sky.SetFloat("_SunDisk", 1f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", skyTint);
            sky.SetColor("_GroundColor", new Color(0.37f, 0.40f, 0.36f));
            sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;

            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(sunEuler);
            sun.type = LightType.Directional;
            sun.color = sunColor;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.75f;
            sun.lightmapBakeType = LightmapBakeType.Realtime;
            RenderSettings.sun = sun;
            return sun;
        }

        /// <summary>
        /// Editor-only view from the scene's SpawnAnchor at standing eye height, so the Game view
        /// shows the environment while editing. It switches itself off at runtime (XR rig takes over).
        /// </summary>
        public static Camera PreviewCamera(Transform spawnAnchor)
        {
            var go = new GameObject("PreviewCamera (Game view while editing)", typeof(Camera), typeof(ScenePreviewCamera));
            go.transform.SetPositionAndRotation(spawnAnchor.position + Vector3.up * 1.6f,
                Quaternion.Euler(8f, spawnAnchor.eulerAngles.y, 0f));
            var camera = go.GetComponent<Camera>();
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 450f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return camera;
        }

        /// <summary>No baked or realtime GI: environments light fully in real time from the sun and ambient gradient.</summary>
        public static void DisableGlobalIllumination(string settingsPath)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = System.IO.Path.GetFileNameWithoutExtension(settingsPath) };
                ProjectPaths.EnsureFolder(System.IO.Path.GetDirectoryName(settingsPath)?.Replace('\\', '/'));
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            settings.bakedGI = false;
            settings.realtimeGI = false;
            Lightmapping.lightingSettings = settings;
        }
    }
}
