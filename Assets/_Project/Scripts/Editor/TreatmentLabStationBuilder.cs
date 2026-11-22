using System.Collections.Generic;
using BTP.Treatment;
using BTP.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace BTP.Editor
{
    /// <summary>
    /// Scene-level parts of the Treatment lab built with <see cref="TreatmentSceneBuilder.BuildScene"/> (they persist
    /// across case changes, while each case's room prefab is swapped): the vineyard landscape seen through the
    /// panoramic window, the charcoal case sign ("Apply targeted spray" / "Tomato / Early blight") on its pole over the
    /// counter, and the hand-held "Training spray" bottle on the workbench. Room space: viewer at (0, 0, -1.35) facing +Z.
    /// </summary>
    public static class TreatmentLabStationBuilder
    {
        const string Materials = ProjectPaths.Root + "/Materials/Treatment";
        const string SprayerModel = "Assets/Lab_Treatment_Assets/spray-bottle/model/SprayBottle_Quest.fbx";
        const string MoteMesh = ProjectPaths.Meshes + "/VeinMote.asset";
        /// <summary>Text printed on the hand sprayer's label.</summary>
        const string BottleLabel = "Fertilizer";
        static readonly Vector3 Viewer = new Vector3(0f, 1.6f, -1.35f);

        // Sign: right of the specimen above the counter (yaw from the viewer, distance, centre height).
        const float SignYaw = 24f, SignDistance = 3.3f, SignHeight = 2.32f;
        static readonly Vector2 SignSize = new Vector2(1150f, 360f);

        /// <param name="withCounterStation">False when the specimen chamber station replaces the counter sign and sprayer.</param>
        internal static void Build(Transform treatment, Transform scenery, TreatmentController controller, bool withCounterStation = true)
        {
            BuildVineyard(scenery);
            if (!withCounterStation)
                return;
            var station = new GameObject("LabStation").transform;
            station.SetParent(treatment, false);
            BuildSign(station, controller);
            BuildSprayer(station, controller);
        }

        // ------------------------------------------------------------------ vineyard beyond the window

        /// <summary>Orderly trellised vine rows running away from the window, then low rolling hills.</summary>
        static void BuildVineyard(Transform scenery)
        {
            var hills = MaterialFactory.Solid($"{ProjectPaths.EnvironmentMaterials}/Lab_VineyardHills.mat", Color.white, 0f, ProceduralTextures.Hills);
            EnvironmentKit.MeshObject("VineyardHills", scenery, ProceduralMeshes.HillRing("TreatmentVineyardHills", 46f, 150f, 5f, 20f, 7), hills,
                Vector3.zero, Quaternion.identity, Vector3.one);

            var canopy = MaterialFactory.Solid($"{ProjectPaths.EnvironmentMaterials}/Lab_VineCanopy.mat", new Color(0.34f, 0.5f, 0.2f), 0.1f);
            var posts = MaterialFactory.Solid($"{ProjectPaths.EnvironmentMaterials}/Lab_VinePosts.mat", new Color(0.45f, 0.34f, 0.22f), 0.05f);
            var go = new GameObject("VineyardRows", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(scenery, false);
            go.GetComponent<MeshFilter>().sharedMesh = VineyardMesh();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { canopy, posts };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        /// <summary>One mesh (canopy and post submeshes) so the whole vineyard is two draw calls on Quest.</summary>
        static Mesh VineyardMesh()
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var canopy = new List<int>();
            var posts = new List<int>();
            var random = new System.Random(17);
            const float rowSpacing = 2.6f, segment = 3.4f, start = 13.5f, end = 44f;
            for (var x = -33.8f; x <= 33.9f; x += rowSpacing)
            {
                for (var z = start; z < end; z += segment)
                {
                    var height = 0.95f + (float)random.NextDouble() * 0.4f;
                    var width = 0.55f + (float)random.NextDouble() * 0.12f;
                    Box(vertices, normals, canopy, new Vector3(x, 0.55f + height / 2f, z + segment / 2f), new Vector3(width, height, segment - 0.25f));
                    Box(vertices, normals, posts, new Vector3(x, 0.85f, z), new Vector3(0.09f, 1.7f, 0.09f));
                }

                Box(vertices, normals, posts, new Vector3(x, 0.85f, end), new Vector3(0.09f, 1.7f, 0.09f));
            }

            var path = $"{ProjectPaths.Meshes}/TreatmentVineyardRows.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            var created = mesh == null;
            if (created)
                mesh = new Mesh { name = "TreatmentVineyardRows" };
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(canopy, 0);
            mesh.SetTriangles(posts, 1);
            mesh.RecalculateBounds();
            if (created)
            {
                ProjectPaths.EnsureFolder(ProjectPaths.Meshes);
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
                EditorUtility.SetDirty(mesh);

            return mesh;
        }

        static readonly Vector3[] FaceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        static void Box(List<Vector3> vertices, List<Vector3> normals, List<int> triangles, Vector3 centre, Vector3 size)
        {
            var half = size * 0.5f;
            foreach (var n in FaceNormals)
            {
                // Two axes spanning the face, ordered so the quad winds clockwise seen from outside.
                var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(u, n);
                var origin = vertices.Count;
                var c = centre + Vector3.Scale(n, half);
                var du = Vector3.Scale(u, half);
                var dv = Vector3.Scale(v, half);
                vertices.Add(c - du - dv);
                vertices.Add(c - du + dv);
                vertices.Add(c + du + dv);
                vertices.Add(c + du - dv);
                for (var i = 0; i < 4; i++)
                    normals.Add(n);
                triangles.AddRange(new[] { origin, origin + 1, origin + 2, origin, origin + 2, origin + 3 });
            }
        }

        // ------------------------------------------------------------------ case sign

        static void BuildSign(Transform station, TreatmentController controller)
        {
            var direction = Quaternion.Euler(0f, SignYaw, 0f) * Vector3.forward;
            var position = new Vector3(Viewer.x, 0f, Viewer.z) + direction * SignDistance + Vector3.up * SignHeight;
            var rotation = UIFactory.Facing(position, Viewer);
            var root = new GameObject("LabSign").transform;
            root.SetParent(station, false);
            root.SetPositionAndRotation(position, rotation);

            var canvas = UIFactory.WorldCanvas("Sign", root, SignSize, Vector3.zero, Quaternion.identity);
            Object.DestroyImmediate(canvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var rect = (RectTransform)canvas.transform;
            UIFactory.Panel(rect, new Color(0.2f, 0.215f, 0.23f, 0.97f), false);
            var outline = UIFactory.RoundedImage("Border", rect, new Color(0.78f, 0.8f, 0.82f, 1f), ProceduralTextures.RoundedOutline);
            UIFactory.Stretch(outline.rectTransform);
            outline.raycastTarget = false;
            var accent = UIFactory.RoundedImage("Accent", rect, UIPalette.Cyan, ProceduralTextures.RoundedFill);
            accent.raycastTarget = false;
            UIFactory.Place(accent.rectTransform, 38f, 62f, 14f, SignSize.y - 124f);

            var title = UIFactory.Text("Title", rect, TrainingSprayer.ActionLabel, 76f, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UIFactory.Place(title.rectTransform, 84f, 44f, SignSize.x - 120f, 120f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 40f;
            title.fontSizeMax = 76f;
            var subtitle = UIFactory.Text("Subtitle", rect, "Tomato / Early blight", 58f, new Color(0.93f, 0.94f, 0.95f), TextAlignmentOptions.MidlineLeft,
                FontStyles.Italic);
            UIFactory.Place(subtitle.rectTransform, 84f, 172f, SignSize.x - 120f, 100f);
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            subtitle.enableAutoSizing = true;
            subtitle.fontSizeMin = 30f;
            subtitle.fontSizeMax = 58f;
            root.gameObject.AddComponent<TreatmentLabSign>().Bind(controller, title, subtitle);

            // Support pole under the sign's right third, down to the floor.
            var bottom = SignHeight - SignSize.y * UIFactory.MillimetreScale / 2f;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "SignPole";
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.transform.SetParent(root, false);
            pole.transform.localPosition = new Vector3(0.28f, -SignHeight + bottom / 2f, 0.03f);
            pole.transform.localScale = new Vector3(0.06f, bottom / 2f, 0.06f);
            var poleRenderer = pole.GetComponent<MeshRenderer>();
            poleRenderer.sharedMaterial = MaterialFactory.Solid($"{Materials}/SignPole.mat", new Color(0.78f, 0.8f, 0.82f), 0.5f);
            poleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ Training spray bottle

        static void BuildSprayer(Transform station, TreatmentController controller)
        {
            var bench = Quaternion.Euler(0f, TreatmentAssetBuilder.BenchYaw, 0f);
            var home = TreatmentAssetBuilder.BenchCentre + bench * new Vector3(0.34f, 0f, 0.02f) + Vector3.up * TreatmentAssetBuilder.BenchTop;
            var away = home - Viewer;
            away.y = 0f;

            var root = new GameObject("TrainingSprayer");
            root.transform.SetParent(station, false);
            root.transform.SetPositionAndRotation(home, Quaternion.LookRotation(away.normalized, Vector3.up));
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(-0.008f, 0.135f, 0f);
            box.size = new Vector3(0.095f, 0.275f, 0.08f);
            root.AddComponent<XRSimpleInteractable>();

            var white = MaterialFactory.Lit($"{Materials}/SprayerWhite.mat", new Color(0.93f, 0.94f, 0.95f), 0f, 0.55f);
            var label = MaterialFactory.Solid($"{Materials}/SprayerLabel.mat", new Color(0.97f, 0.97f, 0.96f), 0.2f);
            var red = MaterialFactory.Lit($"{Materials}/SprayerRed.mat", new Color(0.82f, 0.09f, 0.07f), 0f, 0.5f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SprayerModel);
            if (model != null)
            {
                var instance = (GameObject)Object.Instantiate(model, root.transform, false);
                instance.name = "Model";
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var material = renderer.name switch
                    {
                        "Cylinder.001" => label,
                        "Cube.001" => red,
                        "Cylinder.002" => red,
                        _ => white,
                    };
                    var materials = new Material[renderer.sharedMaterials.Length];
                    for (var i = 0; i < materials.Length; i++)
                        materials[i] = material;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            else
            {
                Debug.LogWarning($"[BTP] {SprayerModel} is missing (run Tools/blender_spray_bottle.py); using a simple bottle.");
                Primitive(PrimitiveType.Cylinder, "Bottle", root.transform, new Vector3(0f, 0.1f, 0f), new Vector3(0.07f, 0.1f, 0.07f), white);
                Primitive(PrimitiveType.Cube, "Head", root.transform, new Vector3(0.01f, 0.235f, 0f), new Vector3(0.07f, 0.05f, 0.03f), red);
            }

            // "Fertilizer" label on the side that faces the viewer while it rests on the bench.
            var labelCanvas = UIFactory.WorldCanvas("Label", root.transform, new Vector2(54f, 40f), new Vector3(-0.008f, 0.085f, -0.0352f), Quaternion.identity);
            Object.DestroyImmediate(labelCanvas.GetComponent<TrackedDeviceGraphicRaycaster>());
            var labelRect = (RectTransform)labelCanvas.transform;
            var labelBack = UIFactory.RoundedImage("Background", labelRect, new Color(0.96f, 0.96f, 0.95f), ProceduralTextures.RoundedFill);
            UIFactory.Stretch(labelBack.rectTransform);
            labelBack.raycastTarget = false;
            var labelText = UIFactory.Text("Text", labelRect, BottleLabel, 11f, new Color(0.12f, 0.13f, 0.14f), TextAlignmentOptions.Center, FontStyles.Bold);
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 5f;
            labelText.fontSizeMax = 11f;
            UIFactory.Stretch(labelText.rectTransform, 3f);

            var mist = BuildMist(root.transform);
            root.AddComponent<TrainingSprayer>().Bind(mist, controller);
        }

        static ParticleSystem BuildMist(Transform bottle)
        {
            var go = new GameObject("Mist", typeof(ParticleSystem));
            go.transform.SetParent(bottle, false);
            // Nozzle tip at the front of the trigger head; particles leave along +X (the nozzle direction).
            go.transform.SetLocalPositionAndRotation(new Vector3(0.046f, 0.258f, 0f), Quaternion.Euler(0f, 90f, 0f));
            var system = go.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.005f, 0.011f);
            main.startColor = new Color(0.94f, 0.98f, 1f, 1f);
            main.gravityModifier = 0.08f;
            main.maxParticles = 320;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var emission = system.emission;
            emission.rateOverTime = 560f;
            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.002f;
            var drag = system.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 2.6f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mote = AssetDatabase.LoadAssetAtPath<Mesh>(MoteMesh);
            if (mote != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = mote;
            }

            // Its own fine-mist material: denser than the lesson animator's Mist.mat so the spray cone reads in the headset.
            renderer.sharedMaterial = MaterialFactory.Lit($"{Materials}/SprayMist.mat", new Color(0.92f, 0.97f, 1f, 0.6f), 0f, 0.6f, transparent: true, doubleSided: true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
