using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTP.Treatment
{
    /// <summary>One drawn fertiliser granule (64 bytes; the shader's GranuleInstance).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GranuleInstance
    {
        /// <summary>World centre (xyz) and radius in metres (w).</summary>
        public Vector4 PositionRadius;
        /// <summary>World rotation (quaternion xyzw).</summary>
        public Vector4 Rotation;
        /// <summary>Linear albedo (rgb) and smoothness (a).</summary>
        public Vector4 Colour;
        /// <summary>x mesh variant (0-7), y speckle amount, z speckle seed, w ground height for contact shading (-1e4: none).</summary>
        public Vector4 Data;
    }

    /// <summary>Something that shows granules (a packet's heap, the scoop's heap, a pour); asked every frame.</summary>
    public interface IGranuleSource
    {
        void CollectGranules(GranuleRenderer sink);
    }

    /// <summary>
    /// Draws every fertiliser granule of the guided treatment with GPU instancing: the granules of all registered
    /// <see cref="IGranuleSource"/>s are gathered once per frame into two structured buffers, near (the 80-triangle mesh)
    /// and far (the 20-triangle mesh, beyond <see cref="LodDistance"/> from the player's head), and each buffer is one
    /// <c>Graphics.RenderMeshPrimitives</c> call with the BTP/Fertiliser Granule shader, which reads the instance's
    /// position, rotation, radius, colour and shape from the buffer (so 2 draw calls for any number of granules, single-pass
    /// stereo included). At most <see cref="MaxGranules"/> are drawn; nothing is allocated per frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GranuleRenderer : MonoBehaviour
    {
        public const int MaxGranules = 3000;
        public const float DefaultLodDistance = 1.5f, CullDistance = 8f;
        static readonly int GranulesId = Shader.PropertyToID("_Granules");

        public static GranuleRenderer Active { get; private set; }

        readonly List<IGranuleSource> sources = new List<IGranuleSource>();
        Material material;
        GraphicsBuffer nearBuffer, farBuffer;
        NativeArray<GranuleInstance> nearData, farData;
        MaterialPropertyBlock nearBlock, farBlock;
        Vector3 head;
        float lodSqr, cullSqr;
        Bounds bounds;
        bool any;

        public float LodDistance { get; set; } = DefaultLodDistance;
        public int NearCount { get; private set; }
        public int FarCount { get; private set; }
        public int Visible => NearCount + FarCount;
        /// <summary>Granules offered this frame beyond <see cref="MaxGranules"/> (not drawn).</summary>
        public int Dropped { get; private set; }
        /// <summary>Instanced draw calls per camera this frame (0-2).</summary>
        public int DrawCalls => (NearCount > 0 ? 1 : 0) + (FarCount > 0 ? 1 : 0);
        public Material Material => material;
        public bool Supported { get; private set; }

        /// <summary>The granule renderer (one per scene), drawing with <paramref name="granuleMaterial"/>.</summary>
        public static GranuleRenderer Create(Transform parent, Material granuleMaterial)
        {
            var go = new GameObject("GranuleRenderer");
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<GranuleRenderer>();
            renderer.material = granuleMaterial;
            return renderer;
        }

        void OnEnable()
        {
            Active = this;
            // Structured buffers in the vertex stage: Vulkan (Quest), D3D11/12, Metal, GLES 3.1+ with vertex SSBOs.
            Supported = SystemInfo.supportsComputeShaders && SystemInfo.maxComputeBufferInputsVertex > 0;
            if (!Supported)
                Debug.LogWarning("[BTP] Fertiliser granules: structured buffers are not available in the vertex stage on this device; granules are hidden.");
            nearData = new NativeArray<GranuleInstance>(MaxGranules, Allocator.Persistent);
            farData = new NativeArray<GranuleInstance>(MaxGranules, Allocator.Persistent);
            var stride = Marshal.SizeOf<GranuleInstance>();
            nearBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxGranules, stride);
            farBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxGranules, stride);
            nearBlock = new MaterialPropertyBlock();
            farBlock = new MaterialPropertyBlock();
            nearBlock.SetBuffer(GranulesId, nearBuffer);
            farBlock.SetBuffer(GranulesId, farBuffer);
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            if (Active == this)
                Active = null;
            nearBuffer?.Release();
            farBuffer?.Release();
            nearBuffer = farBuffer = null;
            if (nearData.IsCreated)
                nearData.Dispose();
            if (farData.IsCreated)
                farData.Dispose();
            NearCount = FarCount = 0;
        }

        public void Register(IGranuleSource source)
        {
            if (source != null && !sources.Contains(source))
                sources.Add(source);
        }

        public void Unregister(IGranuleSource source) => sources.Remove(source);

        void LateUpdate() => Gather();

        /// <summary>Collects every source's granules and uploads them (LateUpdate; verification may call it).</summary>
        public void Gather()
        {
            NearCount = FarCount = 0;
            Dropped = 0;
            any = false;
            var cam = Camera.main;
            head = cam != null ? cam.transform.position : Vector3.zero;
            lodSqr = LodDistance * LodDistance;
            cullSqr = CullDistance * CullDistance;
            for (var i = sources.Count - 1; i >= 0; i--)
            {
                var source = sources[i];
                if (source == null || (source is Object unityObject && unityObject == null))
                {
                    sources.RemoveAt(i);
                    continue;
                }

                source.CollectGranules(this);
            }

            if (!Supported || nearBuffer == null)
                return;
            if (NearCount > 0)
                nearBuffer.SetData(nearData, 0, 0, NearCount);
            if (FarCount > 0)
                farBuffer.SetData(farData, 0, 0, FarCount);
        }

        /// <summary>Adds one granule this frame; false when the budget is used up (or it is too far to see).</summary>
        public bool Add(Vector3 position, Quaternion rotation, float radius, Vector4 colour, float variant, float speckle, float seed, float groundY)
        {
            var d = (position - head).sqrMagnitude;
            if (d > cullSqr)
                return false;
            if (NearCount + FarCount >= MaxGranules)
            {
                Dropped++;
                return false;
            }

            var instance = new GranuleInstance
            {
                PositionRadius = new Vector4(position.x, position.y, position.z, radius),
                Rotation = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w),
                Colour = colour,
                Data = new Vector4(variant, speckle, seed, groundY),
            };
            if (d <= lodSqr)
                nearData[NearCount++] = instance;
            else
                farData[FarCount++] = instance;
            if (!any)
            {
                bounds = new Bounds(position, Vector3.one * 0.02f);
                any = true;
            }
            else
            {
                bounds.Encapsulate(position);
            }

            return true;
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!Supported || material == null || !any || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return;
            var drawBounds = bounds;
            drawBounds.Expand(0.05f);
            var rp = new RenderParams(material)
            {
                worldBounds = drawBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                camera = cam,
                layer = gameObject.layer,
            };
            if (NearCount > 0)
            {
                rp.matProps = nearBlock;
                Graphics.RenderMeshPrimitives(rp, GranuleMeshes.Near, 0, NearCount);
            }

            if (FarCount > 0)
            {
                rp.matProps = farBlock;
                Graphics.RenderMeshPrimitives(rp, GranuleMeshes.Far, 0, FarCount);
            }
        }
    }
}
