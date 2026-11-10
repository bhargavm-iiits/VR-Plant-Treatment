using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BTP.Editor
{
    /// <summary>
    /// Bakes a distant-LOD billboard for a plant: front and side orthographic views packed
    /// side by side into one texture, shown on two crossed quads (4 triangles). Views are
    /// rendered with unlit copies of the plant's materials, and once against black and once
    /// against white so the alpha channel is exact whatever the render target keeps.
    /// </summary>
    static class BillboardBaker
    {
        public struct Result
        {
            public Mesh Mesh;
            public Texture2D Texture;
        }

        public static Result Bake(GameObject plant, string name, int viewSize)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var temporaries = new List<Object>();
            var target = new RenderTexture(viewSize, viewSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1
            };
            try
            {
                var instance = Object.Instantiate(plant);
                SceneManager.MoveGameObjectToScene(instance, scene);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var lodGroup in instance.GetComponentsInChildren<LODGroup>())
                    lodGroup.enabled = false;
                UseUnlitMaterials(instance, temporaries);

                var bounds = RendererBounds(instance);
                var size = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z), bounds.size.y) * 1.04f;
                var centre = bounds.center;

                var cameraObject = new GameObject("BillboardCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.scene = scene;
                camera.cameraType = CameraType.Preview;
                camera.orthographic = true;
                camera.orthographicSize = size * 0.5f;
                camera.aspect = 1f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = size * 4f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.targetTexture = target;

                var distance = size * 2f;
                // Front: looking along +Z (image right = +X). Side: looking along +X (image right = -Z).
                var front = CaptureView(camera, target, centre - Vector3.forward * distance, Quaternion.LookRotation(Vector3.forward));
                var side = CaptureView(camera, target, centre - Vector3.right * distance, Quaternion.LookRotation(Vector3.right));

                var atlas = new Color32[viewSize * 2 * viewSize];
                for (var y = 0; y < viewSize; y++)
                {
                    for (var x = 0; x < viewSize; x++)
                    {
                        atlas[y * viewSize * 2 + x] = front[y * viewSize + x];
                        atlas[y * viewSize * 2 + viewSize + x] = side[y * viewSize + x];
                    }
                }

                Dilate(atlas, viewSize * 2, viewSize, 8);
                var texture = SaveTexture(atlas, viewSize * 2, viewSize, $"{ProjectPaths.BillboardTextures}/{name}_Billboard.png");
                var mesh = SaveMesh(BuildCrossQuads(centre, size, $"{name}_Billboard"), $"{ProjectPaths.BillboardMeshes}/{name}_Billboard.asset");
                return new Result { Mesh = mesh, Texture = texture };
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                target.Release();
                Object.DestroyImmediate(target);
                foreach (var temporary in temporaries)
                    Object.DestroyImmediate(temporary);
            }
        }

        static void UseUnlitMaterials(GameObject instance, List<Object> temporaries)
        {
            var unlitShader = Shader.Find(MaterialFactory.UnlitShader);
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    var unlit = new Material(unlitShader);
                    if (source != null && source.HasProperty("_BaseMap"))
                        unlit.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                    if (source != null && source.HasProperty("_BaseColor"))
                        unlit.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                    var cutout = source != null && source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f;
                    unlit.SetFloat("_AlphaClip", cutout ? 1f : 0f);
                    unlit.SetFloat("_Cutoff", 0.5f);
                    unlit.SetFloat("_Cull", 0f);
                    BaseShaderGUI.SetMaterialKeywords(unlit);
                    materials[i] = unlit;
                    temporaries.Add(unlit);
                }

                renderer.sharedMaterials = materials;
            }
        }

        static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static Color32[] CaptureView(Camera camera, RenderTexture target, Vector3 position, Quaternion rotation)
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            var onBlack = Render(camera, target, Color.black);
            var onWhite = Render(camera, target, Color.white);

            // Alpha from how much the background shows through; colour un-premultiplied.
            var result = new Color32[onBlack.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var b = onBlack[i];
                var w = onWhite[i];
                var seeThrough = ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / (3f * 255f);
                var alpha = Mathf.Clamp01(1f - seeThrough);
                result[i] = alpha < 0.02f
                    ? new Color32(0, 0, 0, 0)
                    : new Color32(
                        (byte)Mathf.Clamp(b.r / alpha, 0f, 255f),
                        (byte)Mathf.Clamp(b.g / alpha, 0f, 255f),
                        (byte)Mathf.Clamp(b.b / alpha, 0f, 255f),
                        (byte)(alpha * 255f));
            }

            return result;
        }

        static Color32[] Render(Camera camera, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            var pixels = readback.GetPixels32();
            Object.DestroyImmediate(readback);
            return pixels;
        }

        /// <summary>Spreads edge colours into transparent texels so mipmaps do not darken the outline.</summary>
        static void Dilate(Color32[] pixels, int width, int height, int iterations)
        {
            var source = (Color32[])pixels.Clone();
            for (var pass = 0; pass < iterations; pass++)
            {
                var changed = false;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;
                        if (source[i].a > 0 || pixels[i].r + pixels[i].g + pixels[i].b > 0)
                            continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                                    continue;
                                var p = pixels[ny * width + nx];
                                if (p.r + p.g + p.b == 0)
                                    continue;
                                r += p.r;
                                g += p.g;
                                b += p.b;
                                n++;
                            }
                        }

                        if (n == 0)
                            continue;
                        pixels[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }
        }

        static Mesh BuildCrossQuads(Vector3 centre, float size, string name)
        {
            var h = size * 0.5f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            // Front quad in the plane z = centre.z; image left is -X.
            AddQuad(vertices, uvs,
                new Vector3(centre.x - h, centre.y - h, centre.z), new Vector3(centre.x + h, centre.y - h, centre.z),
                new Vector3(centre.x + h, centre.y + h, centre.z), new Vector3(centre.x - h, centre.y + h, centre.z), 0f);
            // Side quad in the plane x = centre.x; image left is +Z.
            AddQuad(vertices, uvs,
                new Vector3(centre.x, centre.y - h, centre.z + h), new Vector3(centre.x, centre.y - h, centre.z - h),
                new Vector3(centre.x, centre.y + h, centre.z - h), new Vector3(centre.x, centre.y + h, centre.z + h), 0.5f);

            var normals = new Vector3[vertices.Count];
            for (var i = 0; i < normals.Length; i++)
                normals[i] = Vector3.up; // lit like the ground, so both quads match from any angle

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.normals = normals;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddQuad(List<Vector3> vertices, List<Vector2> uvs, Vector3 bottomLeft, Vector3 bottomRight,
            Vector3 topRight, Vector3 topLeft, float uOffset)
        {
            vertices.Add(bottomLeft);
            vertices.Add(bottomRight);
            vertices.Add(topRight);
            vertices.Add(topLeft);
            uvs.Add(new Vector2(uOffset, 0f));
            uvs.Add(new Vector2(uOffset + 0.5f, 0f));
            uvs.Add(new Vector2(uOffset + 0.5f, 1f));
            uvs.Add(new Vector2(uOffset, 1f));
        }

        static Texture2D SaveTexture(Color32[] pixels, int width, int height, string path)
        {
            ProjectPaths.EnsureFolder(ProjectPaths.BillboardTextures);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureSettings.ConfigureCutout(path, 1024);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            ProjectPaths.EnsureFolder(ProjectPaths.BillboardMeshes);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
