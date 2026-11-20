using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>
    /// A falling water stream (Play mode; owned by the WATER station's outlet or the watering wand): a ballistic tube from
    /// the nozzle to the soil whose radius thins as the water speeds up (constant flow: r ~ 1/sqrt(v)) with a slight wobble,
    /// a front that falls from the nozzle when the water starts and a tail that detaches and falls when it stops, a flow
    /// that ramps with "Water to add", a splash of mesh particles at the impact that collide with the soil, tray and glass
    /// (<see cref="ChamberGlassPhysics"/>) and a few expanding ripples on the soil. Every buffer is preallocated: no GC per
    /// frame. The semi-transparent water is a tinted runtime copy of the chamber glass (no Shader.Find).
    /// </summary>
    public sealed class WaterStreamFx : MonoBehaviour
    {
        public const float Gravity = 9.81f;
        const int Samples = 18, Sides = 8, Ripples = 4;
        const float RampSeconds = 0.3f;

        MeshFilter filter;
        MeshRenderer meshRenderer;
        Mesh mesh;
        readonly Vector3[] vertices = new Vector3[Samples * Sides];
        readonly Vector3[] normals = new Vector3[Samples * Sides];
        readonly Vector3[] centres = new Vector3[Samples];
        ParticleSystem splash;
        Transform[] ripples;
        float[] rippleAge;
        MeshRenderer[] rippleRenderers;
        MaterialPropertyBlock block;
        Material water;
        static Mesh rippleMesh;
        int maxSplash = 120;

        Vector3 origin;
        Vector3 velocity = Vector3.down;
        float groundY;
        float targetFlow;
        float flow;
        float head, tail;
        bool running;
        float time, splashCarry, rippleTimer;
        int nextRipple;
        uint random = 2463534242u;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Where the stream meets the ground now.</summary>
        public Vector3 Impact { get; private set; }
        /// <summary>True while any water is visible (including a falling tail).</summary>
        public bool Visible => meshRenderer != null && meshRenderer.enabled;
        public bool Running => running;
        public float Flow => flow;
        public ParticleSystem Splash => splash;

        /// <summary>Creates the stream under <paramref name="parent"/> (world space) with its own tinted water material.</summary>
        public static WaterStreamFx Create(string name, Transform parent, Material glass, int maxSplashParticles)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            // Vertices are written in world space: this transform is the world identity.
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var lossy = go.transform.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(lossy.x, 1e-4f), 1f / Mathf.Max(lossy.y, 1e-4f), 1f / Mathf.Max(lossy.z, 1e-4f));
            var fx = go.AddComponent<WaterStreamFx>();
            fx.Initialise(glass, maxSplashParticles);
            return fx;
        }

        void Initialise(Material glass, int maxSplashParticles)
        {
            maxSplash = Mathf.Max(8, maxSplashParticles);
            water = ChamberGlassPhysics.Tinted(glass, new Color(0.78f, 0.9f, 0.98f, 0.42f), "water", 0.97f);
            filter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = water;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.enabled = false;
            mesh = new Mesh { name = "WaterStreamFx" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.normals = normals;
            var triangles = new int[(Samples - 1) * Sides * 6];
            var k = 0;
            for (var i = 0; i < Samples - 1; i++)
            for (var s = 0; s < Sides; s++)
            {
                var a = i * Sides + s;
                var b = i * Sides + (s + 1) % Sides;
                triangles[k++] = a;
                triangles[k++] = a + Sides;
                triangles[k++] = b + Sides;
                triangles[k++] = a;
                triangles[k++] = b + Sides;
                triangles[k++] = b;
            }

            mesh.triangles = triangles;
            filter.sharedMesh = mesh;

            splash = ChamberGlassPhysics.CreateParticles(name + "Splash", transform, water, maxSplash, true);
            block = new MaterialPropertyBlock();
            ripples = new Transform[Ripples];
            rippleAge = new float[Ripples];
            rippleRenderers = new MeshRenderer[Ripples];
            rippleMesh ??= BTP.UI.RingMesh.Build(0.4f, 0.5f, 32);
            for (var i = 0; i < Ripples; i++)
            {
                var quad = new GameObject("Ripple" + i, typeof(MeshFilter), typeof(MeshRenderer));
                quad.GetComponent<MeshFilter>().sharedMesh = rippleMesh;
                quad.transform.SetParent(transform, false);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = water;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                quad.SetActive(false);
                ripples[i] = quad.transform;
                rippleRenderers[i] = renderer;
                rippleAge[i] = -1f;
            }
        }

        void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
            if (water != null)
                Destroy(water);
        }

        /// <summary>The nozzle (world) and the water's exit velocity (world, m/s).</summary>
        public void SetSource(Vector3 nozzle, Vector3 exitVelocity)
        {
            origin = nozzle;
            velocity = exitVelocity;
        }

        /// <summary>The height the water lands at (the soil surface).</summary>
        public void SetGround(float y) => groundY = y;

        /// <summary>0 stops the water (the tail falls away); 0..1 is the flow, ramped over 0.3 s.</summary>
        public void SetFlow(float value)
        {
            var on = value > 1e-3f;
            if (on && !running)
            {
                // A fresh stream: the front leaves the nozzle now.
                if (!Visible || tail > 0f)
                {
                    head = 0f;
                    tail = 0f;
                }
            }

            running = on;
            if (on)
                targetFlow = Mathf.Clamp01(value);
        }

        // ------------------------------------------------------------------ math (pure; unit tested)

        /// <summary>Position on the ballistic path after <paramref name="t"/> seconds.</summary>
        public static Vector3 Ballistic(Vector3 start, Vector3 exitVelocity, float t, float gravity = Gravity) =>
            start + exitVelocity * t + 0.5f * gravity * t * t * Vector3.down;

        /// <summary>
        /// Seconds until the water starting at height <paramref name="y0"/> with vertical speed <paramref name="vy"/> reaches
        /// <paramref name="groundY"/> (the later root of y0 + vy t - g t^2 / 2 = ground); 0 when it starts below it.
        /// </summary>
        public static float TimeToGround(float y0, float vy, float groundY, float gravity = Gravity)
        {
            var drop = y0 - groundY;
            if (drop <= 0f)
                return 0f;
            return (vy + Mathf.Sqrt(vy * vy + 2f * gravity * drop)) / gravity;
        }

        /// <summary>Stream radius at speed <paramref name="speed"/>: a constant flow thins as it accelerates (r0 * sqrt(v0 / v)).</summary>
        public static float RadiusAt(float r0, float exitSpeed, float speed) =>
            r0 * Mathf.Sqrt(Mathf.Max(exitSpeed, 1e-3f) / Mathf.Max(speed, Mathf.Max(exitSpeed, 1e-3f)));

        // ------------------------------------------------------------------ frame

        void Update() => Tick(Time.deltaTime);

        public void Tick(float deltaTime)
        {
            if (mesh == null)
                return;
            time += deltaTime;
            flow = Mathf.MoveTowards(flow, running ? targetFlow : flow, deltaTime / RampSeconds);
            var fall = TimeToGround(origin.y, velocity.y, groundY);
            if (running)
            {
                head = Mathf.Min(fall, head + deltaTime);
                tail = 0f;
            }
            else if (Visible)
            {
                tail += deltaTime;
                head = Mathf.Min(fall, head + deltaTime);
            }

            var visible = (running || tail < fall) && head > tail + 1e-4f && fall > 1e-4f;
            meshRenderer.enabled = visible;
            if (visible)
                BuildTube(tail, head);
            Impact = Ballistic(origin, velocity, fall);
            Impact = new Vector3(Impact.x, groundY, Impact.z);

            var landing = running && head >= fall - 1e-4f;
            if (landing)
                EmitSplash(deltaTime, fall);
            TickRipples(deltaTime, landing);
            if (!running && !visible)
                flow = 0f;
        }

        void BuildTube(float t0, float t1)
        {
            var exitSpeed = velocity.magnitude;
            var r0 = Mathf.Lerp(0.0022f, 0.0042f, flow);
            var bounds = new Bounds(Ballistic(origin, velocity, t0), Vector3.zero);
            for (var i = 0; i < Samples; i++)
            {
                var u = i / (float)(Samples - 1);
                var t = Mathf.Lerp(t0, t1, u);
                var p = Ballistic(origin, velocity, t);
                // A slight wobble that grows with the fall.
                var w = u * 0.0016f;
                p.x += Mathf.Sin(time * 17f + t * 60f) * w;
                p.z += Mathf.Cos(time * 13f + t * 47f) * w * 0.7f;
                centres[i] = p;
                bounds.Encapsulate(p);
            }

            for (var i = 0; i < Samples; i++)
            {
                var t = Mathf.Lerp(t0, t1, i / (float)(Samples - 1));
                var v = velocity + Vector3.down * (Gravity * t);
                var axis = i < Samples - 1 ? centres[i + 1] - centres[i] : centres[i] - centres[i - 1];
                axis = axis.sqrMagnitude > 1e-12f ? axis.normalized : Vector3.down;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.z) < 0.9f ? Vector3.forward : Vector3.right).normalized;
                var up = Vector3.Cross(side, axis);
                var radius = RadiusAt(r0, exitSpeed, v.magnitude);
                // The front and the falling tail are rounded off.
                if (i == 0 && !running)
                    radius *= 0.4f;
                if (i == Samples - 1 && t1 < TimeToGround(origin.y, velocity.y, groundY) - 1e-4f)
                    radius *= 0.5f;
                for (var s = 0; s < Sides; s++)
                {
                    var a = s * Mathf.PI * 2f / Sides;
                    var n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    vertices[i * Sides + s] = centres[i] + n * radius;
                    normals[i * Sides + s] = n;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            bounds.Expand(0.02f);
            mesh.bounds = bounds;
        }

        float Next()
        {
            // xorshift: deterministic, allocation-free.
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return (random & 0xFFFFFF) / (float)0x1000000;
        }

        void EmitSplash(float deltaTime, float fall)
        {
            if (splash == null)
                return;
            splashCarry += deltaTime * Mathf.Lerp(25f, 70f, flow);
            var count = Mathf.FloorToInt(splashCarry);
            if (count <= 0)
                return;
            splashCarry -= count;
            var arrival = velocity + Vector3.down * (Gravity * fall);
            var speed = arrival.magnitude;
            for (var i = 0; i < count; i++)
            {
                var angle = Next() * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var up = Mathf.Lerp(0.35f, 0.9f, Next()) * Mathf.Clamp(speed, 0.6f, 2.2f);
                var across = Mathf.Lerp(0.15f, 0.55f, Next()) * Mathf.Clamp(speed, 0.6f, 2.2f);
                var parameters = new ParticleSystem.EmitParams
                {
                    position = Impact + Vector3.up * 0.004f + outward * 0.004f,
                    velocity = outward * across + Vector3.up * up + new Vector3(arrival.x, 0f, arrival.z) * 0.3f,
                    startSize = Mathf.Lerp(0.0025f, 0.0045f, Next()),
                    startLifetime = Mathf.Lerp(0.45f, 0.8f, Next()),
                    applyShapeToPosition = false,
                };
                splash.Emit(parameters, 1);
            }
        }

        void TickRipples(float deltaTime, bool landing)
        {
            if (ripples == null)
                return;
            if (landing)
            {
                rippleTimer -= deltaTime;
                if (rippleTimer <= 0f)
                {
                    rippleTimer = Mathf.Lerp(0.3f, 0.16f, flow);
                    var i = nextRipple;
                    nextRipple = (nextRipple + 1) % Ripples;
                    rippleAge[i] = 0f;
                    ripples[i].gameObject.SetActive(true);
                    ripples[i].SetPositionAndRotation(Impact + Vector3.up * (0.0022f + i * 0.0002f), Quaternion.identity);
                }
            }

            for (var i = 0; i < Ripples; i++)
            {
                if (rippleAge[i] < 0f)
                    continue;
                rippleAge[i] += deltaTime;
                const float life = 0.7f;
                if (rippleAge[i] >= life)
                {
                    rippleAge[i] = -1f;
                    ripples[i].gameObject.SetActive(false);
                    continue;
                }

                var k = rippleAge[i] / life;
                var size = Mathf.Lerp(0.008f, 0.05f + flow * 0.02f, Mathf.Sqrt(k));
                ripples[i].localScale = new Vector3(size, 1f, size);
                rippleRenderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, new Color(0.85f, 0.93f, 1f, 0.32f * (1f - k)));
                rippleRenderers[i].SetPropertyBlock(block);
            }
        }
    }
}
