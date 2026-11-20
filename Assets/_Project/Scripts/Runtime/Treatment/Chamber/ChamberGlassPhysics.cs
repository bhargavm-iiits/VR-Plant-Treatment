using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace BTP.Treatment
{
    /// <summary>
    /// Real collision for the specimen chamber (Play mode only; added by <see cref="TreatmentChamber"/>): solid, few-cm
    /// thick box colliders behind every glass pane whose inner faces sit exactly on the visual inner glass surface (the
    /// front door only while it is closed), the chamber floor, the soil surface, the tray walls and its front window and
    /// the pedestal. They live on the "Ignore Raycast" layer (<see cref="GlassLayer"/>): the chamber's particles (spray
    /// mist, water splash, the recovery sparkle) collide with exactly that layer, while every XR ray, far caster, poke,
    /// reticle and UI occlusion mask in the scene has the layer stripped (<see cref="StripGlassFromRayMasks"/>), so
    /// controller rays, hand rays, teleport and the desktop mouse ray pass straight through the glass to the plant, the
    /// soil and the tools. The rig's CharacterController ignores every chamber collider (nobody is ever trapped), and the
    /// held tools are kinematic with Instantaneous movement, so PhysX never pushes or blocks them. Droplets that hit a
    /// vertical pane leave short-lived running drops sliding down the inside of the glass (a pooled set of quads, no GC).
    /// </summary>
    public sealed class ChamberGlassPhysics : MonoBehaviour
    {
        /// <summary>Unity's built-in "Ignore Raycast" layer.</summary>
        public const int GlassLayer = 2;
        public const int GlassMask = 1 << GlassLayer;
        /// <summary>Collider thickness behind each pane (the visual glass is 8 mm; the collider reaches outwards).</summary>
        public const float Thickness = 0.04f;
        /// <summary>All chamber particle systems together stay at or below this many live particles.</summary>
        public const int ParticleBudget = 300;
        const int MaxDrops = 24;

        static readonly string[] Panes = { "GlassLeft", "GlassRight", "GlassBack", "GlassTop", "Door" };

        /// <summary>The chamber in the open scene (there is one); particle relays report glass hits to it.</summary>
        public static ChamberGlassPhysics Active { get; private set; }

        Transform root;
        Transform paneRoot;
        readonly Dictionary<string, BoxCollider> colliders = new Dictionary<string, BoxCollider>();
        readonly List<CharacterController> rigs = new List<CharacterController>();
        float ignoreTimer;
        bool frontOpen;
        bool doorSwung;

        // Running drops: a fixed pool of quads.
        Transform[] drops;
        Vector3[] dropVelocity;
        float[] dropLife, dropFloor, dropSize;
        int nextDrop;
        Material dropMaterial;

        public IReadOnlyDictionary<string, BoxCollider> Colliders => colliders;
        public int LiveDrops { get; private set; }

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        void OnDestroy()
        {
            if (dropMaterial != null)
                Destroy(dropMaterial);
        }

        // ------------------------------------------------------------------ colliders

        /// <summary>
        /// Builds (once) and fits every collider to the chamber's current parts. <paramref name="parts"/> is the chamber's
        /// Parts transform; collider boxes use the same local space as the part boxes.
        /// </summary>
        public void Fit(Transform parts, ChamberLayout layout, bool doorOpen)
        {
            if (parts == null)
                return;
            if (root == null)
            {
                root = new GameObject("GlassPhysics").transform;
                root.SetParent(parts, false);
                root.gameObject.layer = GlassLayer;
                paneRoot = new GameObject("Panes").transform;
                paneRoot.SetParent(root, false);
                paneRoot.gameObject.layer = GlassLayer;
            }

            var interiorCentre = new Vector3(0f, layout.FloorHeight + layout.Interior.y * 0.5f, 0f);
            const float frame = ChamberFit.FrameThickness;
            foreach (var name in Panes)
            {
                var part = parts.Find(name);
                if (part == null)
                    continue;
                // The pane's thin axis and the inward direction (towards the interior's centre).
                var size = part.localScale;
                var axis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
                var inward = Vector3.zero;
                inward[axis] = interiorCentre[axis] - part.localPosition[axis] >= 0f ? 1f : -1f;
                var innerFace = part.localPosition + inward * (size[axis] * 0.5f);
                var box = Box(paneRoot, name);
                var extent = size + Vector3.one * (2f * frame);
                extent[axis] = Thickness;
                box.transform.localPosition = innerFace - inward * (Thickness * 0.5f);
                box.size = extent;
            }

            // The floor plate, the soil surface and the pedestal: thickened downwards from their top faces.
            Slab(parts, "FloorPlate", Vector3.down);
            Slab(parts, "SoilCap", Vector3.down);
            Solid(parts, "Pedestal");
            // The tray: its walls as they are, the clear front window thickened outwards.
            Solid(parts, "TrayLeft");
            Solid(parts, "TrayRight");
            Solid(parts, "TrayBack");
            Solid(parts, "TrayBottom");
            Slab(parts, "TrayWindow", Vector3.back);

            SetFrontOpen(doorOpen);
            StripGlassFromRayMasks();
            FindRigs();
            IgnoreRigs();
        }

        BoxCollider Box(Transform parent, string name)
        {
            if (colliders.TryGetValue(name, out var existing) && existing != null)
                return existing;
            var go = new GameObject(name + "Collider");
            go.layer = GlassLayer;
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            colliders[name] = box;
            return box;
        }

        /// <summary>A collider exactly matching a part box (thick parts: tray walls, pedestal).</summary>
        void Solid(Transform parts, string name)
        {
            var part = parts.Find(name);
            if (part == null)
                return;
            var box = Box(root, name);
            box.transform.localPosition = part.localPosition;
            box.size = part.localScale;
            box.enabled = part.gameObject.activeSelf;
        }

        /// <summary>A collider whose face opposite <paramref name="away"/> matches the part's face; it extends <see cref="Thickness"/> along <paramref name="away"/>.</summary>
        void Slab(Transform parts, string name, Vector3 away)
        {
            var part = parts.Find(name);
            if (part == null)
                return;
            var size = part.localScale;
            var axis = Mathf.Abs(away.x) > 0.5f ? 0 : Mathf.Abs(away.y) > 0.5f ? 1 : 2;
            var face = part.localPosition - away * (size[axis] * 0.5f);
            var box = Box(root, name);
            var extent = size;
            extent[axis] = Thickness;
            box.transform.localPosition = face + away * (Thickness * 0.5f);
            box.size = extent;
            box.enabled = part.gameObject.activeSelf;
        }

        /// <summary>The front door's collider follows the door: none while the front is open for the hands-on stations.</summary>
        public void SetFrontOpen(bool open)
        {
            frontOpen = open;
            ApplyDoorCollider();
        }

        /// <summary>
        /// The user's hinged door (<see cref="ChamberDoor"/>): its collider is solid only while the leaf is fully closed, never
        /// while it is open or swinging (the collider is fitted to the closed leaf and does not follow the swing).
        /// </summary>
        public void SetDoorSwung(bool swung)
        {
            if (doorSwung == swung)
                return;
            doorSwung = swung;
            ApplyDoorCollider();
        }

        void ApplyDoorCollider()
        {
            if (colliders.TryGetValue("Door", out var door) && door != null)
            {
                door.enabled = !frontOpen && !doorSwung;
                IgnoreRigs();
            }
        }

        public bool FrontOpen => frontOpen;

        /// <summary>True while the user's door is open or moving (its collider is off).</summary>
        public bool DoorSwung => doorSwung;

        /// <summary>True for a glass pane collider (not the floor, soil or tray).</summary>
        public bool IsPane(GameObject other) => other != null && paneRoot != null && other.transform.parent == paneRoot;

        void FindRigs()
        {
            rigs.Clear();
            rigs.AddRange(FindObjectsByType<CharacterController>(FindObjectsInactive.Include));
        }

        /// <summary>Every rig CharacterController ignores every chamber collider (re-applied: re-enabling a collider resets it).</summary>
        void IgnoreRigs()
        {
            foreach (var rig in rigs)
            {
                if (rig == null || !rig.enabled || !rig.gameObject.activeInHierarchy)
                    continue;
                foreach (var box in colliders.Values)
                    if (box != null && box.enabled && box.gameObject.activeInHierarchy)
                        Physics.IgnoreCollision(rig, box, true);
            }
        }

        /// <summary>
        /// Removes <see cref="GlassLayer"/> from every ray, far caster, near caster, poke, reticle and UI occlusion mask in the
        /// scene (inactive ones included): XRRayInteractor and CurveInteractionCaster default to every layer (-1), which
        /// would otherwise stop at the glass. Safe to call repeatedly.
        /// </summary>
        public static void StripGlassFromRayMasks()
        {
            foreach (var ray in FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include))
                ray.raycastMask &= ~GlassMask;
            foreach (var caster in FindObjectsByType<CurveInteractionCaster>(FindObjectsInactive.Include))
                caster.raycastMask &= ~GlassMask;
            foreach (var caster in FindObjectsByType<SphereInteractionCaster>(FindObjectsInactive.Include))
                caster.physicsLayerMask &= ~GlassMask;
            foreach (var poke in FindObjectsByType<XRPokeInteractor>(FindObjectsInactive.Include))
                poke.physicsLayerMask &= ~GlassMask;
            foreach (var reticle in FindObjectsByType<XRInteractorReticleVisual>(FindObjectsInactive.Include))
                reticle.raycastMask &= ~GlassMask;
            foreach (var raycaster in FindObjectsByType<TrackedDeviceGraphicRaycaster>(FindObjectsInactive.Include))
                raycaster.blockingMask &= ~GlassMask;
        }

        void Update()
        {
            ignoreTimer -= Time.deltaTime;
            if (ignoreTimer <= 0f)
            {
                ignoreTimer = 1f;
                IgnoreRigs();
            }

            TickDrops(Time.deltaTime);
        }

        // ------------------------------------------------------------------ materials and particles

        /// <summary>
        /// A Quest-friendly realistic glass from the chamber's glass material (a runtime copy; no grab pass, no refraction):
        /// alpha 0.1, smoothness 0.95, a faint blue-green tint, specular highlights and environment reflections on.
        /// </summary>
        public static Material MakeRealisticGlass(Material source)
        {
            if (source == null)
                return null;
            var glass = new Material(source) { name = source.name + " (runtime)" };
            if (glass.HasProperty("_BaseColor"))
                glass.SetColor("_BaseColor", new Color(0.84f, 0.95f, 0.93f, 0.1f));
            SetFloat(glass, "_Smoothness", 0.95f);
            SetFloat(glass, "_Metallic", 0f);
            SetFloat(glass, "_SpecularHighlights", 1f);
            SetFloat(glass, "_EnvironmentReflections", 1f);
            glass.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            glass.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return glass;
        }

        /// <summary>A runtime copy of <paramref name="source"/> recoloured (water from the glass, the green/red keys, the sparkle).</summary>
        public static Material Tinted(Material source, Color color, string suffix, float smoothness = -1f)
        {
            if (source == null)
                return null;
            var copy = new Material(source) { name = source.name + " (" + suffix + ")" };
            if (copy.HasProperty("_BaseColor"))
                copy.SetColor("_BaseColor", color);
            if (smoothness >= 0f)
                SetFloat(copy, "_Smoothness", smoothness);
            return copy;
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        static Mesh droplet;

        /// <summary>A small shared octahedron (unit radius 0.5) for mesh particles: drops, mist, granule-like sparkle.</summary>
        public static Mesh DropletMesh
        {
            get
            {
                if (droplet != null)
                    return droplet;
                droplet = new Mesh { name = "ChamberDroplet", hideFlags = HideFlags.DontSave };
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                int[] faces = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                var vertices = new Vector3[6];
                for (var i = 0; i < 6; i++)
                    vertices[i] = axes[i] * 0.5f;
                droplet.vertices = vertices;
                droplet.normals = axes;
                droplet.triangles = faces;
                droplet.RecalculateBounds();
                return droplet;
            }
        }

        /// <summary>
        /// A world-space mesh-particle system for the chamber (emission off: owners emit with EmitParams). With
        /// <paramref name="collide"/> its particles collide with the glass, soil and tray only: almost no bounce, high
        /// dampening and a lifetime loss on every hit, so drops stick to the glass and slide a little before they vanish.
        /// </summary>
        public static ParticleSystem CreateParticles(string name, Transform parent, Material material, int maxParticles, bool collide, float gravity = 1f)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.maxParticles = Mathf.Max(1, maxParticles);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startLifetime = 0.8f;
            main.startSpeed = 0f;
            main.startSize = 0.004f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            if (collide)
            {
                var collision = system.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.collidesWith = GlassMask;
                collision.enableDynamicColliders = false;
                collision.quality = ParticleSystemCollisionQuality.High;
                collision.bounce = 0.06f;
                collision.dampen = 0.9f;
                collision.lifetimeLoss = 0.35f;
                collision.radiusScale = 1f;
                collision.sendCollisionMessages = true;
                go.AddComponent<GlassDropletRelay>().Bind(system);
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = DropletMesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(true);
            system.Play();
            return system;
        }

        // ------------------------------------------------------------------ running drops

        void EnsureDrops(Material source)
        {
            if (drops != null)
                return;
            dropMaterial = Tinted(source, new Color(0.88f, 0.96f, 1f, 0.42f), "drops", 0.98f);
            drops = new Transform[MaxDrops];
            dropVelocity = new Vector3[MaxDrops];
            dropLife = new float[MaxDrops];
            dropFloor = new float[MaxDrops];
            dropSize = new float[MaxDrops];
            var holder = new GameObject("RunningDrops").transform;
            holder.SetParent(transform, false);
            for (var i = 0; i < MaxDrops; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Drop" + i;
                var collider = quad.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
                quad.transform.SetParent(holder, false);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = dropMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                quad.SetActive(false);
                drops[i] = quad.transform;
            }
        }

        /// <summary>The material the running drops use (the chamber's glass, a little denser); set once by the chamber.</summary>
        public void SetDropSource(Material glass) => EnsureDrops(glass);

        /// <summary>A droplet hit a vertical pane at <paramref name="point"/> (normal pointing into the chamber): a drop runs down from there.</summary>
        public void SpawnRunningDrop(Vector3 point, Vector3 normal)
        {
            if (drops == null || Mathf.Abs(normal.y) > 0.5f)
                return;
            var i = nextDrop;
            nextDrop = (nextDrop + 1) % MaxDrops;
            var drop = drops[i];
            drop.gameObject.SetActive(true);
            drop.SetPositionAndRotation(point + normal * 0.0015f, Quaternion.LookRotation(-normal, Vector3.up));
            var seed = Mathf.Abs(point.x * 73.1f + point.y * 37.7f + point.z * 11.3f);
            var r = seed - Mathf.Floor(seed);
            dropSize[i] = Mathf.Lerp(0.003f, 0.0055f, r);
            dropVelocity[i] = Vector3.down * Mathf.Lerp(0.012f, 0.04f, 1f - r);
            dropLife[i] = Mathf.Lerp(1.6f, 3.2f, r);
            dropFloor[i] = transform.position.y + 0.01f;
            drop.localScale = new Vector3(dropSize[i], dropSize[i] * 1.7f, 1f);
        }

        void TickDrops(float deltaTime)
        {
            if (drops == null)
                return;
            var live = 0;
            for (var i = 0; i < MaxDrops; i++)
            {
                var drop = drops[i];
                if (!drop.gameObject.activeSelf)
                    continue;
                dropLife[i] -= deltaTime;
                var position = drop.position + dropVelocity[i] * deltaTime;
                // Surface tension: the drop slows as it runs and thins out before it is gone.
                dropVelocity[i] *= 1f - Mathf.Clamp01(deltaTime * 0.35f);
                if (dropLife[i] <= 0f || position.y <= dropFloor[i])
                {
                    drop.gameObject.SetActive(false);
                    continue;
                }

                live++;
                drop.position = position;
                var fade = Mathf.Clamp01(dropLife[i] / 0.6f);
                drop.localScale = new Vector3(dropSize[i] * fade, dropSize[i] * (1.7f + (1f - fade)), 1f);
            }

            LiveDrops = live;
        }
    }

    /// <summary>Forwards a chamber particle system's collisions with the glass to <see cref="ChamberGlassPhysics"/> (running drops).</summary>
    public sealed class GlassDropletRelay : MonoBehaviour
    {
        readonly List<ParticleCollisionEvent> events = new List<ParticleCollisionEvent>(16);
        ParticleSystem system;
        float budget;

        internal void Bind(ParticleSystem source) => system = source;

        void Update() => budget = Mathf.Min(4f, budget + Time.deltaTime * 6f);

        void OnParticleCollision(GameObject other)
        {
            var glass = ChamberGlassPhysics.Active;
            if (glass == null || system == null || !glass.IsPane(other))
                return;
            var count = system.GetCollisionEvents(other, events);
            for (var i = 0; i < count && budget >= 1f; i++)
            {
                budget -= 1f;
                glass.SpawnRunningDrop(events[i].intersection, events[i].normal);
            }
        }
    }
}
