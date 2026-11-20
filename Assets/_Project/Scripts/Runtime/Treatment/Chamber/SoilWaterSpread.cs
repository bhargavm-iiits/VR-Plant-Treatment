using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// Water spreading over the TOP of the soil in the chamber tray (Play mode; the WATER console and the watering wand
    /// drive it). Three light layers, all clipped to the tray's soil rectangle:
    /// <list type="bullet">
    /// <item>the wet soil: one transparent quad over the soil top with a small CPU mask texture (64 x 64, redrawn only when
    /// the patch changes) of a soft, irregular darkened patch that grows outward from the impact point with the water
    /// delivered (<see cref="TargetRadius"/>) at a finite speed, and keeps where a moving wand has already wetted it;</item>
    /// <item>a faint satin sheen over the wet area (a lit glass-like disc with the same wobbly edge);</item>
    /// <item>a shallow glossy film near the impact that builds while water flows and soaks in over a few seconds after
    /// OFF (<see cref="StepFilm"/>).</item>
    /// </list>
    /// Quest-friendly: no decals or projectors, three draw calls, preallocated buffers and meshes, no per-frame allocations.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SoilWaterSpread : MonoBehaviour
    {
        public const int Resolution = 64;
        /// <summary>Radius of the first wet spot (m).</summary>
        public const float MinRadius = 0.022f;
        /// <summary>How fast the wet edge creeps outward (m/s) and back while draining.</summary>
        public const float SpreadSpeed = 0.045f, DrySpeed = 0.015f;
        /// <summary>The film builds at this rate while water flows and soaks in at the slower one after (per second).</summary>
        public const float FilmRise = 1.4f, FilmSoak = 0.24f;
        /// <summary>Soft edge width (m) and the edge's irregularity (fraction of the radius).</summary>
        public const float Feather = 0.014f, EdgeNoise = 0.2f;
        /// <summary>The patch never covers more than this share of the soil rectangle's diagonal.</summary>
        public const float MaxRadiusShare = 0.7f;
        const int Segments = 48;
        const float WetLift = 0.0012f, SheenLift = 0.0018f, FilmLift = 0.0024f;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly Color WetColor = new Color(0.085f, 0.048f, 0.022f, 1f);
        static readonly Color FilmColor = new Color(0.3f, 0.34f, 0.36f, 1f);

        Vector2 soilMin, soilMax;
        float soilY, maxRadius;
        bool fitted;
        Vector3 centre;
        float radius, targetRadius, drawnRadius = -1f;
        Vector3 drawnCentre;
        float film, drawnFilm = -1f;
        bool flowing;
        float phase1, phase2, phase3;

        MeshRenderer wetRenderer, sheenRenderer, filmRenderer;
        Mesh wetMesh, sheenMesh, filmMesh;
        Texture2D mask;
        Color32[] pixels;
        float[] field;
        float[] grain;
        Material wetMaterial, sheenMaterial, filmMaterial;
        readonly Vector3[] discVertices = new Vector3[Segments + 1];
        readonly Vector3[] discNormals = new Vector3[Segments + 1];
        int[] discTriangles;

        public float Radius => radius;
        public float TargetRadiusNow => targetRadius;
        public float MaxRadius => maxRadius;
        public float Film => film;
        public bool Flowing => flowing;
        public Vector3 Centre => centre;
        public bool Visible => wetRenderer != null && wetRenderer.enabled;
        /// <summary>The mask's coverage (0..1) at a world point on the soil (verification).</summary>
        public float CoverageAt(Vector3 world)
        {
            if (field == null || !fitted)
                return 0f;
            var u = Mathf.InverseLerp(soilMin.x, soilMax.x, world.x);
            var v = Mathf.InverseLerp(soilMin.y, soilMax.y, world.z);
            var i = Mathf.Clamp(Mathf.FloorToInt(u * Resolution), 0, Resolution - 1);
            var j = Mathf.Clamp(Mathf.FloorToInt(v * Resolution), 0, Resolution - 1);
            return field[j * Resolution + i];
        }

        // ------------------------------------------------------------------ pure (unit tested)

        /// <summary>The wet patch's radius for 0..1 soil wetness: none when dry, then <see cref="MinRadius"/> growing with the
        /// square root of the water delivered (an area that grows with the volume) up to <paramref name="max"/>.</summary>
        public static float TargetRadius(float wetness, float max)
        {
            if (wetness <= 0.005f)
                return 0f;
            return Mathf.Lerp(MinRadius, Mathf.Max(MinRadius, max), Mathf.Sqrt(Mathf.Clamp01(wetness)));
        }

        /// <summary>The shown radius one step towards <paramref name="target"/>: it creeps out while wetting, back while drying.</summary>
        public static float StepRadius(float current, float target, float deltaTime) =>
            Mathf.MoveTowards(current, target, (target > current ? SpreadSpeed : DrySpeed) * Mathf.Max(0f, deltaTime));

        /// <summary>The film's strength one step on: builds while <paramref name="water"/> flows, soaks in after.</summary>
        public static float StepFilm(float current, bool water, float deltaTime) =>
            Mathf.MoveTowards(current, water ? 1f : 0f, (water ? FilmRise : FilmSoak) * Mathf.Max(0f, deltaTime));

        /// <summary>The shallow film's radius near the impact for a wet patch of <paramref name="wetRadius"/>.</summary>
        public static float PuddleRadius(float wetRadius) => Mathf.Min(wetRadius * 0.5f, 0.06f);

        /// <summary>The edge's wobble (-1..1) at angle <paramref name="theta"/>: three low harmonics with per-tray phases.</summary>
        public static float Wobble(float theta, float p1, float p2, float p3) =>
            0.5f * Mathf.Sin(3f * theta + p1) + 0.3f * Mathf.Sin(5f * theta + p2) + 0.2f * Mathf.Sin(9f * theta + p3);

        /// <summary>
        /// Wet coverage (0..1) of a point <paramref name="distance"/> from the impact for a patch of <paramref name="r"/>
        /// whose edge is pushed out or in by <paramref name="wobble"/>: 1 inside, a smooth fall over <see cref="Feather"/>.
        /// </summary>
        public static float Coverage(float distance, float r, float wobble)
        {
            if (r <= 0f)
                return 0f;
            var edge = r * (1f + EdgeNoise * Mathf.Clamp(wobble, -1f, 1f));
            var t = Mathf.Clamp01((edge - distance) / Feather + 0.5f);
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ setup

        /// <summary>The chamber's spread (created on first use, Play mode only: null in edit mode).</summary>
        public static SoilWaterSpread For(TreatmentChamber chamber)
        {
            if (chamber == null || !Application.isPlaying)
                return null;
            var existing = chamber.GetComponentInChildren<SoilWaterSpread>(true);
            if (existing != null)
                return existing;
            var go = new GameObject("SoilWaterSpread") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(chamber.transform, false);
            return go.AddComponent<SoilWaterSpread>();
        }

        /// <summary>Fits the layers to the chamber's current tray (soil rectangle and height) and dries the soil.</summary>
        public void Fit(TreatmentChamber chamber)
        {
            if (chamber == null)
                return;
            chamber.SoilArea(out soilMin, out soilMax);
            soilY = chamber.SoilWorldY;
            maxRadius = Mathf.Max(MinRadius, (soilMax - soilMin).magnitude * MaxRadiusShare);
            fitted = true;
            Build(chamber);

            // A fixed grain and edge phases per tray size, so a tray always shows the same natural-looking patch.
            var seed = Mathf.Abs(Mathf.RoundToInt((soilMax.x - soilMin.x) * 9973f + (soilMax.y - soilMin.y) * 7919f));
            phase1 = seed % 628 / 100f;
            phase2 = seed * 7 % 628 / 100f;
            phase3 = seed * 13 % 628 / 100f;
            var offsetX = seed % 97 * 0.37f;
            var offsetY = seed % 89 * 0.53f;
            for (var j = 0; j < Resolution; j++)
            for (var i = 0; i < Resolution; i++)
            {
                var a = Mathf.PerlinNoise(offsetX + i * 0.16f, offsetY + j * 0.16f);
                var b = Mathf.PerlinNoise(offsetY + i * 0.41f, offsetX + j * 0.41f);
                grain[j * Resolution + i] = (a - 0.5f) * 1.4f + (b - 0.5f) * 0.6f;
            }

            Clear();
        }

        void Build(TreatmentChamber chamber)
        {
            if (wetRenderer == null)
            {
                pixels = new Color32[Resolution * Resolution];
                field = new float[Resolution * Resolution];
                grain = new float[Resolution * Resolution];
                mask = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { name = "SoilWetMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                discTriangles = new int[Segments * 3];
                for (var s = 0; s < Segments; s++)
                {
                    discTriangles[s * 3] = 0;
                    discTriangles[s * 3 + 1] = 1 + (s + 1) % Segments;
                    discTriangles[s * 3 + 2] = 1 + s;
                }

                for (var k = 0; k < discNormals.Length; k++)
                    discNormals[k] = Vector3.up;

                var wetSource = chamber.WetPatch != null && chamber.WetPatch.TryGetComponent<MeshRenderer>(out var patch) ? patch.sharedMaterial : null;
                var glass = chamber.GlassMaterial;
                if (wetSource != null)
                {
                    wetMaterial = new Material(wetSource) { name = wetSource.name + " (spread)" };
                    wetMaterial.SetTexture(BaseMapId, mask);
                    wetMaterial.SetColor(BaseColorId, WetColor);
                }

                // The sheen and the film are glass-like (lit, very smooth): the specular highlight makes them read as water.
                sheenMaterial = ChamberGlassPhysics.Tinted(glass, new Color(0.2f, 0.17f, 0.14f, 0.02f), "wet sheen", 0.5f);
                filmMaterial = ChamberGlassPhysics.Tinted(glass, new Color(FilmColor.r, FilmColor.g, FilmColor.b, 0f), "water film", 0.97f);
                wetRenderer = Layer("WetSoil", wetMaterial, out wetMesh);
                sheenRenderer = Layer("WetSheen", sheenMaterial, out sheenMesh);
                filmRenderer = Layer("WaterFilm", filmMaterial, out filmMesh);
                foreach (var (r, queue) in new[] { (wetRenderer, 0), (sheenRenderer, 1), (filmRenderer, 2) })
                    if (r != null && r.sharedMaterial != null)
                        r.sharedMaterial.renderQueue = (int)RenderQueue.Transparent + queue;
            }

            // The wet quad spans the soil rectangle exactly (its UVs are the mask's texels).
            var t = wetRenderer.transform;
            wetMesh.Clear();
            wetMesh.vertices = new[]
            {
                t.InverseTransformPoint(new Vector3(soilMin.x, soilY + WetLift, soilMin.y)), t.InverseTransformPoint(new Vector3(soilMax.x, soilY + WetLift, soilMin.y)),
                t.InverseTransformPoint(new Vector3(soilMin.x, soilY + WetLift, soilMax.y)), t.InverseTransformPoint(new Vector3(soilMax.x, soilY + WetLift, soilMax.y)),
            };
            wetMesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            wetMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            wetMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            wetMesh.RecalculateBounds();
        }

        MeshRenderer Layer(string layerName, Material material, out Mesh mesh)
        {
            var go = new GameObject(layerName, typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = layerName };
            mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Simple;
            r.enabled = false;
            return r;
        }

        void OnDestroy()
        {
            foreach (var o in new Object[] { wetMesh, sheenMesh, filmMesh, mask, wetMaterial, sheenMaterial, filmMaterial })
                if (o != null)
                    Destroy(o);
        }

        // ------------------------------------------------------------------ driving

        /// <summary>Dry soil: no patch, no film.</summary>
        public void Clear()
        {
            radius = targetRadius = 0f;
            film = 0f;
            flowing = false;
            drawnRadius = drawnFilm = -1f;
            if (field != null)
                System.Array.Clear(field, 0, field.Length);
            foreach (var r in new[] { wetRenderer, sheenRenderer, filmRenderer })
                if (r != null)
                    r.enabled = false;
        }

        /// <summary>
        /// Water lands at <paramref name="impact"/> with the soil at 0..1 <paramref name="wetness"/>; <paramref name="water"/>
        /// says whether it is still flowing (the film builds) and <paramref name="instant"/> shows the result at once (a
        /// restored specimen) instead of letting it spread.
        /// </summary>
        public void SetWater(Vector3 impact, float wetness, bool water, bool instant = false)
        {
            if (!fitted)
                return;
            centre = new Vector3(Mathf.Clamp(impact.x, soilMin.x, soilMax.x), soilY, Mathf.Clamp(impact.z, soilMin.y, soilMax.y));
            targetRadius = TargetRadius(wetness, maxRadius);
            flowing = water;
            if (instant)
            {
                radius = targetRadius;
                film = 0f;
                Redraw(true);
            }
        }

        /// <summary>The water stopped (OFF, the wand released) or started: the film soaks in or builds.</summary>
        public void SetFlowing(bool water) => flowing = water;

        void Update() => Tick(Time.deltaTime);

        /// <summary>Spreads the patch and builds or soaks the film (Update; verification may call it directly).</summary>
        public void Tick(float deltaTime)
        {
            if (!fitted || wetRenderer == null)
                return;
            radius = StepRadius(radius, targetRadius, deltaTime);
            film = StepFilm(film, flowing && radius > 0f, deltaTime);
            Redraw(false);
        }

        void Redraw(bool force)
        {
            var texel = (soilMax.x - soilMin.x) / Resolution;
            var moved = (centre - drawnCentre).sqrMagnitude > texel * texel * 0.25f;
            if (force || moved || Mathf.Abs(radius - drawnRadius) > texel * 0.35f)
                DrawPatch(radius < drawnRadius - 1e-5f || force);
            if (force || Mathf.Abs(film - drawnFilm) > 0.004f || moved)
                DrawFilm();
        }

        /// <summary>The wet mask and the satin sheen for the current radius (a growing patch keeps what it already wetted).</summary>
        void DrawPatch(bool shrinking)
        {
            drawnRadius = radius;
            drawnCentre = centre;
            var visible = radius > 0.001f;
            wetRenderer.enabled = visible && wetMaterial != null;
            sheenRenderer.enabled = visible && sheenMaterial != null;
            if (!visible)
            {
                System.Array.Clear(field, 0, field.Length);
                return;
            }

            var sizeX = soilMax.x - soilMin.x;
            var sizeZ = soilMax.y - soilMin.y;
            for (var j = 0; j < Resolution; j++)
            {
                var z = soilMin.y + (j + 0.5f) / Resolution * sizeZ - centre.z;
                for (var i = 0; i < Resolution; i++)
                {
                    var x = soilMin.x + (i + 0.5f) / Resolution * sizeX - centre.x;
                    var index = j * Resolution + i;
                    var d = Mathf.Sqrt(x * x + z * z);
                    var wobble = Wobble(Mathf.Atan2(z, x), phase1, phase2, phase3) + grain[index] * 0.45f;
                    var c = Coverage(d, radius, wobble);
                    var value = shrinking ? c : Mathf.Max(field[index], c);
                    field[index] = value;
                    // Darkest where it is soaked longest (the impact), a little lighter towards the creeping edge.
                    var depth = Mathf.Lerp(0.66f, 0.4f, Mathf.Clamp01(d / Mathf.Max(radius, 1e-4f)));
                    var alpha = value * (depth + grain[index] * 0.06f);
                    pixels[index] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            mask.SetPixels32(pixels);
            mask.Apply(false);
            Disc(sheenMesh, sheenRenderer.transform, radius * 0.96f, SheenLift);
        }

        /// <summary>The glossy film near the impact: its size and opacity follow its strength.</summary>
        void DrawFilm()
        {
            drawnFilm = film;
            var visible = film > 0.01f && radius > 0.001f && filmMaterial != null;
            filmRenderer.enabled = visible;
            if (!visible)
                return;
            var puddle = PuddleRadius(radius) * Mathf.Lerp(0.55f, 1f, film);
            Disc(filmMesh, filmRenderer.transform, puddle, FilmLift);
            filmMaterial.SetColor(BaseColorId, new Color(FilmColor.r, FilmColor.g, FilmColor.b, 0.14f * film));
            if (filmMaterial.HasProperty(SmoothnessId))
                filmMaterial.SetFloat(SmoothnessId, Mathf.Lerp(0.85f, 0.97f, film));
        }

        /// <summary>A flat disc with the patch's wobbly edge round the impact, clipped to the soil rectangle.</summary>
        void Disc(Mesh mesh, Transform space, float r, float lift)
        {
            discVertices[0] = space.InverseTransformPoint(new Vector3(centre.x, soilY + lift, centre.z));
            for (var s = 0; s < Segments; s++)
            {
                var theta = s * Mathf.PI * 2f / Segments;
                var edge = r * (1f + EdgeNoise * Wobble(theta, phase1, phase2, phase3));
                var x = Mathf.Clamp(centre.x + Mathf.Cos(theta) * edge, soilMin.x, soilMax.x);
                var z = Mathf.Clamp(centre.z + Mathf.Sin(theta) * edge, soilMin.y, soilMax.y);
                discVertices[s + 1] = space.InverseTransformPoint(new Vector3(x, soilY + lift, z));
            }

            mesh.vertices = discVertices;
            if (mesh.GetIndexCount(0) == 0)
            {
                mesh.normals = discNormals;
                mesh.triangles = discTriangles;
            }
            else
            {
                mesh.normals = discNormals;
            }

            mesh.RecalculateBounds();
        }
    }
}
