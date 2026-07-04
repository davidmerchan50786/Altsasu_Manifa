#if VISTA
using Pinwheel.Vista.Graphics;
using System.Collections.Generic;
using UnityEngine;

namespace Pinwheel.Vista.Graph
{
    /// <summary>
    /// Owns the transient render textures and compute buffers created during graph execution.
    /// </summary>
    /// <remarks>
    /// A <see cref="DataPool"/> is attached to one execution flow and is shared through
    /// <see cref="GraphContext"/>. Nodes request textures and buffers from it by slot name, release
    /// references when they no longer need an input, and let the pool recycle resources whose
    /// reference count has reached zero.
    /// </remarks>
    public class DataPool : System.IDisposable
    {
        /// <summary>
        /// Describes a render target in the simplified form used by the graph runtime.
        /// </summary>
        /// <remarks>
        /// The pool intentionally tracks only width, height, and color format because those are the
        /// properties it uses to decide whether an existing <see cref="GraphRenderTexture"/> can be
        /// reused.
        /// </remarks>
        public struct RtDescriptor : System.IEquatable<RtDescriptor>, System.IEquatable<RenderTextureDescriptor>
        {
            /// <summary>
            /// Width, in pixels, required by the render target.
            /// </summary>
            public int width { get; set; }
            /// <summary>
            /// Height, in pixels, required by the render target.
            /// </summary>
            public int height { get; set; }
            /// <summary>
            /// Color format required by the node output that will use the render target.
            /// </summary>
            public RenderTextureFormat format { get; set; }

            /// <summary>
            /// Creates a render-target descriptor for the requested resolution and format.
            /// </summary>
            /// <param name="width">
            /// The render target width in pixels.
            /// </param>
            /// <param name="height">
            /// The render target height in pixels.
            /// </param>
            /// <param name="format">
            /// The color format required by the output. The default matches Vista's common single
            /// channel height or mask textures.
            /// </param>
            /// <returns>
            /// A descriptor that can be passed to <see cref="CreateRenderTarget(RtDescriptor, string, bool)"/>.
            /// </returns>
            public static RtDescriptor Create(int width, int height, RenderTextureFormat format = RenderTextureFormat.RFloat)
            {
                RtDescriptor d = new RtDescriptor();
                d.width = width;
                d.height = height;
                d.format = format;
                return d;
            }

            /// <summary>
            /// Compares two simplified render-target descriptors for pool reuse.
            /// </summary>
            /// <param name="other">
            /// The descriptor to compare against.
            /// </param>
            /// <returns>
            /// <see langword="true"/> when width, height, and format all match; otherwise,
            /// <see langword="false"/>.
            /// </returns>
            public bool Equals(RtDescriptor other)
            {
                return this.width == other.width &&
                    this.height == other.height &&
                    this.format == other.format;
            }

            /// <summary>
            /// Compares this simplified descriptor against a full <see cref="RenderTextureDescriptor"/>.
            /// </summary>
            /// <param name="other">
            /// The Unity descriptor to compare against.
            /// </param>
            /// <returns>
            /// <see langword="true"/> when the pool-relevant fields match; otherwise,
            /// <see langword="false"/>.
            /// </returns>
            public bool Equals(RenderTextureDescriptor other)
            {
                return this.width == other.width &&
                    this.height == other.height &&
                    this.format == other.colorFormat;
            }

            /// <summary>
            /// Converts the simplified descriptor into the Unity descriptor used to allocate a render texture.
            /// </summary>
            /// <param name="desc">
            /// The simplified descriptor to convert.
            /// </param>
            /// <returns>
            /// A <see cref="RenderTextureDescriptor"/> configured for graph execution, with random
            /// write enabled, mipmaps disabled, and linear sampling.
            /// </returns>
            public static implicit operator RenderTextureDescriptor(RtDescriptor desc)
            {
                RenderTextureDescriptor d = new RenderTextureDescriptor(desc.width, desc.height, desc.format);
                d.sRGB = false;
                d.enableRandomWrite = true;
                d.mipCount = 0;
                d.useMipMap = false;
                return d;
            }
        }

        /// <summary>
        /// Describes a compute buffer in the simplified form used by the graph runtime.
        /// </summary>
        public struct BufferDescriptor : System.IEquatable<BufferDescriptor>
        {
            /// <summary>
            /// Default stride, in bytes, used for graph buffers.
            /// </summary>
            /// <remarks>
            /// Vista packs its runtime point and instance buffers as raw floats, so the pool always
            /// allocates buffers with a stride of <c>sizeof(float)</c> and expresses size through the
            /// total float count.
            /// </remarks>
            public const int DEFAULT_STRIDE = sizeof(float);

            /// <summary>
            /// Number of float elements stored in the buffer.
            /// </summary>
            public int count { get; set; }
            /// <summary>
            /// Gets the stride, in bytes, used when allocating the buffer.
            /// </summary>
            public int stride
            {
                get
                {
                    return DEFAULT_STRIDE;
                }
            }

            /// <summary>
            /// Creates a descriptor for a float-packed compute buffer.
            /// </summary>
            /// <param name="size">
            /// The number of float elements required by the buffer.
            /// </param>
            /// <returns>
            /// A descriptor that can be passed to <see cref="CreateBuffer(BufferDescriptor, string, bool)"/>.
            /// </returns>
            public static BufferDescriptor Create(int size)
            {
                BufferDescriptor desc = new BufferDescriptor();
                desc.count = size;
                return desc;
            }

            /// <summary>
            /// Compares two buffer descriptors for pool reuse.
            /// </summary>
            /// <param name="other">
            /// The descriptor to compare against.
            /// </param>
            /// <returns>
            /// <see langword="true"/> when both descriptors request the same float count; otherwise,
            /// <see langword="false"/>.
            /// </returns>
            public bool Equals(BufferDescriptor other)
            {
                return this.count == other.count;
            }
        }

        /// <summary>
        /// Reports the approximate GPU memory currently retained by the pool.
        /// </summary>
        public struct MemoryStats
        {
            /// <summary>
            /// Number of valid render textures currently held by the pool.
            /// </summary>
            public int textureCount { get; set; }
            /// <summary>
            /// Number of valid compute buffers currently held by the pool.
            /// </summary>
            public int bufferCount { get; set; }
            /// <summary>
            /// Estimated memory usage, in megabytes, of all tracked resources.
            /// </summary>
            public float megabyte { get; set; }

            private static Dictionary<RenderTextureFormat, int> memoryMap;

            /// <summary>
            /// Returns the byte cost per pixel used by the memory estimate for a render texture format.
            /// </summary>
            /// <param name="format">
            /// The render texture format to estimate.
            /// </param>
            /// <returns>
            /// The estimated bytes per pixel for <paramref name="format"/>, or <c>0</c> when the
            /// format is not mapped by this utility.
            /// </returns>
            public static int GetBytePerPixel(RenderTextureFormat format)
            {
                if (memoryMap == null)
                {
                    CreateMemoryMap();
                }
                int bpp;
                if (memoryMap.TryGetValue(format, out bpp))
                {
                    return bpp;
                }
                return 0;
            }

            private static void CreateMemoryMap()
            {
                //in bytes
                memoryMap = new Dictionary<RenderTextureFormat, int>();
                memoryMap[RenderTextureFormat.ARGB32] = 4;
                memoryMap[RenderTextureFormat.Depth] = 0;
                memoryMap[RenderTextureFormat.ARGBHalf] = 8;
                memoryMap[RenderTextureFormat.Shadowmap] = 0;
                memoryMap[RenderTextureFormat.RGB565] = 2;
                memoryMap[RenderTextureFormat.ARGB4444] = 2;
                memoryMap[RenderTextureFormat.ARGB1555] = 2;
                memoryMap[RenderTextureFormat.Default] = 0;
                memoryMap[RenderTextureFormat.ARGB2101010] = 4;
                memoryMap[RenderTextureFormat.DefaultHDR] = 0;
                memoryMap[RenderTextureFormat.ARGB64] = 8;
                memoryMap[RenderTextureFormat.ARGBFloat] = 16;
                memoryMap[RenderTextureFormat.RGFloat] = 8;
                memoryMap[RenderTextureFormat.RGHalf] = 4;
                memoryMap[RenderTextureFormat.RFloat] = 4;
                memoryMap[RenderTextureFormat.RHalf] = 2;
                memoryMap[RenderTextureFormat.R8] = 1;
                memoryMap[RenderTextureFormat.ARGBInt] = 16;
                memoryMap[RenderTextureFormat.RGInt] = 8;
                memoryMap[RenderTextureFormat.RInt] = 4;
                memoryMap[RenderTextureFormat.BGRA32] = 4;
                memoryMap[RenderTextureFormat.RGB111110Float] = 4;
                memoryMap[RenderTextureFormat.RG32] = 4;
                memoryMap[RenderTextureFormat.RGBAUShort] = 8;
                memoryMap[RenderTextureFormat.RG16] = 2;
                memoryMap[RenderTextureFormat.BGRA10101010_XR] = 10;
                memoryMap[RenderTextureFormat.BGR101010_XR] = 0;
                memoryMap[RenderTextureFormat.R16] = 2;
            }
        }

        /// <summary>
        /// Identifier reserved for the temporary height texture used by output processing.
        /// </summary>
        public static readonly string TEMP_HEIGHT_NAME = "~TempHeight";
        /// <summary>
        /// Default resolution used when allocating the temporary height texture.
        /// </summary>
        public static readonly int TEMP_HEIGHT_RESOLUTION = 512;

        private List<GraphRenderTexture> m_renderTextures;

        private List<GraphBuffer> m_buffers;

        private Dictionary<string, int> m_refCount;

        /// <summary>
        /// Creates an empty pool for one graph execution flow.
        /// </summary>
        public DataPool()
        {
            m_renderTextures = new List<GraphRenderTexture>();
            m_buffers = new List<GraphBuffer>();
            m_refCount = new Dictionary<string, int>();
        }

        ~DataPool()
        {
#if UNITY_EDITOR
            MemoryStats stats = GetMemoryStats();
            if (stats.textureCount > 0 || stats.bufferCount > 0)
            {
                Debug.LogWarning("A Data Pool is not disposed, this may causes a memory leak.");
            }
#endif
        }

        /// <summary>
        /// Returns a render texture matching the requested descriptor and binds it to a pool identifier.
        /// </summary>
        /// <param name="desc">
        /// The size and format required by the caller.
        /// </param>
        /// <param name="name">
        /// The identifier used to retrieve this texture later, usually derived from a node slot.
        /// </param>
        /// <param name="clearContent">
        /// <see langword="true"/> to zero the texture before handing it to the caller; otherwise the
        /// previous contents are left intact.
        /// </param>
        /// <returns>
        /// An existing unreferenced texture reused from the pool when possible; otherwise a newly
        /// allocated <see cref="GraphRenderTexture"/>.
        /// </returns>
        public GraphRenderTexture CreateRenderTarget(RtDescriptor desc, string name, bool clearContent = true)
        {
            GraphRenderTexture result = m_renderTextures.Find(rt => desc.Equals(rt.renderTexture.descriptor) && GetReferenceCount(rt.identifier) <= 0);
            if (result == null)
            {
#if UNITY_EDITOR
                if (desc.width % 8 != 0 || desc.height % 8 != 0)
                {
                    Debug.LogWarning($"Attempt to create a new render target whose resolution is not a multiple of 8 ({desc.width}x{desc.height}), name: {name}");
                }
#endif
                result = new GraphRenderTexture(desc.width, desc.height, desc.format);
                result.renderTexture.wrapMode = TextureWrapMode.Clamp;
                result.renderTexture.filterMode = FilterMode.Bilinear;
                result.renderTexture.enableRandomWrite = true;
                result.renderTexture.antiAliasing = 1;
                result.renderTexture.Create();
                m_renderTextures.Add(result);
            }
            result.identifier = name;

            if (clearContent)
            {
                GraphicsUtils.ClearWithZeros(result);
            }

            return result;
        }

        /// <summary>
        /// Returns a render texture and binds it to the identifier derived from a slot reference.
        /// </summary>
        public GraphRenderTexture CreateRenderTarget(RtDescriptor desc, SlotRef slotRef, bool clearContent = true)
        {
            return CreateRenderTarget(desc, GetName(slotRef.nodeId, slotRef.slotId), clearContent);
        }

        /// <summary>
        /// Returns a compute buffer matching the requested descriptor and binds it to a pool identifier.
        /// </summary>
        public GraphBuffer CreateBuffer(BufferDescriptor desc, string name, bool clearContent = true)
        {
            if (desc.count % 8 != 0)
            {
                Debug.LogWarning($"Allocating a graph buffer '{name}' with count {desc.count} that is not a multiple of 8. Compute kernels dispatched at 8 threads per group may read or write out of bounds.");
            }
            GraphBuffer result = m_buffers.Find(b => b.buffer.count == desc.count && GetReferenceCount(b.identifier) <= 0);
            if (result == null)
            {
                result = new GraphBuffer(desc.count, desc.stride);
                m_buffers.Add(result);
            }
            result.identifier = name;

            if (clearContent)
            {
                GraphicsUtils.ClearWithZeros(result.buffer);
            }

            return result;
        }

        /// <summary>
        /// Returns a compute buffer and binds it to the identifier derived from a slot reference.
        /// </summary>
        public GraphBuffer CreateBuffer(BufferDescriptor desc, SlotRef slotRef, bool clearContent = true)
        {
            return CreateBuffer(desc, GetName(slotRef.nodeId, slotRef.slotId), clearContent);
        }

        /// <summary>
        /// Creates or reuses a temporary render texture that is protected from immediate pool reuse.
        /// </summary>
        /// <param name="desc">
        /// The size and format required by the temporary texture.
        /// </param>
        /// <param name="uniqueName">
        /// A caller-defined identifier that should not collide with regular slot output names.
        /// </param>
        /// <param name="clearContent">
        /// <see langword="true"/> to clear the texture to zero before use.
        /// </param>
        /// <returns>
        /// A temporary texture whose reference count is initialized to <c>1</c>.
        /// </returns>
        public GraphRenderTexture CreateTemporaryRT(RtDescriptor desc, string uniqueName, bool clearContent = true)
        {
            GraphRenderTexture rt = CreateRenderTarget(desc, uniqueName, clearContent);
            if (m_refCount != null)
            {
                m_refCount[uniqueName] = 1;
            }

            return rt;
        }

        /// <summary>
        /// Creates or reuses a temporary compute buffer that is protected from immediate pool reuse.
        /// </summary>
        public GraphBuffer CreateTemporaryBuffer(BufferDescriptor desc, string uniqueName, bool clearContent = true)
        {
            GraphBuffer b = CreateBuffer(desc, uniqueName, clearContent);
            if (m_refCount != null)
            {
                m_refCount[uniqueName] = 1;
            }

            return b;
        }

        /// <summary>
        /// Looks up a pooled render texture by identifier.
        /// </summary>
        public GraphRenderTexture GetRT(string name)
        {
            GraphRenderTexture rt = m_renderTextures.Find(t => t.identifier.Equals(name));
            return rt;
        }

        /// <summary>
        /// Looks up a pooled render texture by slot reference.
        /// </summary>
        public GraphRenderTexture GetRT(SlotRef slotRef)
        {
            return GetRT(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Looks up a pooled compute buffer by identifier.
        /// </summary>
        public GraphBuffer GetBuffer(string name)
        {
            GraphBuffer buffer = m_buffers.Find(b => b.identifier.Equals(name));
            return buffer;
        }

        /// <summary>
        /// Looks up a pooled compute buffer by slot reference.
        /// </summary>
        public GraphBuffer GetBuffer(SlotRef slotRef)
        {
            return GetBuffer(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Detaches a render texture from the pool and returns ownership to the caller.
        /// </summary>
        /// <param name="name">
        /// The identifier of the texture to detach.
        /// </param>
        /// <returns>
        /// The detached texture when found; otherwise <see langword="null"/>.
        /// </returns>
        /// <remarks>
        /// Removed resources are no longer disposed by the pool. This is how final graph outputs are
        /// transferred into higher-level result objects such as <see cref="Core.BiomeData"/>.
        /// </remarks>
        public GraphRenderTexture RemoveRTFromPool(string name)
        {
            GraphRenderTexture rt = m_renderTextures.Find(t => t.identifier.Equals(name));
            if (rt != null)
            {
                m_renderTextures.Remove(rt);
                m_refCount.Remove(name);
            }
            return rt;
        }

        /// <summary>
        /// Detaches a render texture from the pool by slot reference.
        /// </summary>
        public GraphRenderTexture RemoveRTFromPool(SlotRef slotRef)
        {
            return RemoveRTFromPool(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Detaches a compute buffer from the pool and returns ownership to the caller.
        /// </summary>
        public GraphBuffer RemoveBufferFromPool(string name)
        {
            GraphBuffer b = m_buffers.Find(t => t.identifier.Equals(name));
            if (b != null)
            {
                m_buffers.Remove(b);
                m_refCount.Remove(name);
            }
            return b;
        }

        /// <summary>
        /// Detaches a compute buffer from the pool by slot reference.
        /// </summary>
        public GraphBuffer RemoveBufferFromPool(SlotRef slotRef)
        {
            return RemoveBufferFromPool(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Disposes every resource still owned by the pool.
        /// </summary>
        /// <remarks>
        /// This should be called when an execution handle or immediate execution result is no longer
        /// needed. Resources previously removed from the pool are not affected because ownership has
        /// already been transferred to the caller.
        /// </remarks>
        public void Dispose()
        {
            for (int i = 0; i < m_renderTextures.Count; ++i)
            {

                if (m_renderTextures[i] != null)
                {
                    m_renderTextures[i].Dispose();
                    Object.DestroyImmediate(m_renderTextures[i]);
                    m_renderTextures[i] = null;
                }
            }
            m_renderTextures.Clear();

            for (int i = 0; i < m_buffers.Count; ++i)
            {
                if (m_buffers[i] != null)
                {
                    m_buffers[i].Dispose();
                    m_buffers[i] = null;
                }
            }
            m_buffers.Clear();
        }

        /// <summary>
        /// Disposes resources that no longer have active references.
        /// </summary>
        /// <remarks>
        /// This is the pool's incremental cleanup path. It also force-disposes the internal
        /// temporary-height texture even if its identifier is still present.
        /// </remarks>
        public void DisposeUnused()
        {
            for (int i = 0; i < m_renderTextures.Count; ++i)
            {
                if (m_renderTextures[i] != null && GetReferenceCount(m_renderTextures[i].identifier) <= 0)
                {
                    m_renderTextures[i].Dispose();
                    Object.DestroyImmediate(m_renderTextures[i]);
                    m_renderTextures[i] = null;
                }
                else if (m_renderTextures[i] != null && m_renderTextures[i].identifier.Equals(TEMP_HEIGHT_NAME))
                {

                    m_renderTextures[i].Dispose();
                    Object.DestroyImmediate(m_renderTextures[i]);
                    m_renderTextures[i] = null;
                }
            }
            m_renderTextures.RemoveAll(rt => rt == null);

            for (int i = 0; i < m_buffers.Count; ++i)
            {
                if (m_buffers[i] != null && GetReferenceCount(m_buffers[i].identifier) <= 0)
                {
                    m_buffers[i].Dispose();
                    m_buffers[i] = null;
                }
            }
            m_buffers.RemoveAll(b => b == null);
        }

        /// <summary>
        /// Calculates an approximate memory snapshot for the resources currently retained by the pool.
        /// </summary>
        public MemoryStats GetMemoryStats()
        {
            MemoryStats stats = new MemoryStats();

            foreach (GraphRenderTexture rt in m_renderTextures)
            {
                if (rt != null && rt.renderTexture.IsCreated())
                {
                    stats.textureCount += 1;
                    float bytePerPixel = MemoryStats.GetBytePerPixel(rt.renderTexture.format);
                    stats.megabyte += bytePerPixel * rt.renderTexture.width * rt.renderTexture.height / 1048576f;
                }
            }

            foreach (GraphBuffer b in m_buffers)
            {
                if (b != null && b.buffer != null && b.buffer.IsValid())
                {
                    stats.bufferCount += 1;
                    stats.megabyte += b.buffer.count * sizeof(float) / 1048576f;
                }
            }

            return stats;
        }

        internal void SetReferenceCount(Dictionary<string, int> refCount)
        {
            this.m_refCount = refCount;
        }

        internal void SetReferenceCount(string name, int value)
        {
            m_refCount[name] = value;
        }

        internal void SetReferenceCount(SlotRef slotRef, int value)
        {
            string name = GetName(slotRef.nodeId, slotRef.slotId);
            m_refCount[name] = value;
        }

        /// <summary>
        /// Decrements the recorded reference count for a pooled resource.
        /// </summary>
        /// <param name="name">
        /// The identifier of the resource being released.
        /// </param>
        /// <remarks>
        /// The count is decremented only when the resource is already tracked in
        /// <see cref="m_refCount"/>. Once the count reaches zero, the resource becomes eligible for
        /// reuse or disposal.
        /// </remarks>
        public void ReleaseReference(string name)
        {
            if (m_refCount.ContainsKey(name))
            {
                m_refCount[name] -= 1;
            }
        }

        /// <summary>
        /// Decrements the recorded reference count for the resource bound to a slot reference.
        /// </summary>
        public void ReleaseReference(SlotRef slotRef)
        {
            ReleaseReference(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Returns the tracked reference count for a pooled resource.
        /// </summary>
        /// <param name="name">
        /// The identifier of the resource to query.
        /// </param>
        /// <returns>
        /// The tracked reference count, or <c>0</c> when the resource is not currently tracked.
        /// </returns>
        public int GetReferenceCount(string name)
        {
            if (m_refCount.ContainsKey(name))
            {
                return m_refCount[name];
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// Returns the tracked reference count for the resource bound to a slot reference.
        /// </summary>
        public int GetUsageCount(SlotRef slotRef)
        {
            return GetReferenceCount(GetName(slotRef.nodeId, slotRef.slotId));
        }

        /// <summary>
        /// Builds the canonical pool identifier used for a node output slot.
        /// </summary>
        /// <param name="nodeId">
        /// The node identifier.
        /// </param>
        /// <param name="slotId">
        /// The output slot identifier on that node.
        /// </param>
        /// <returns>
        /// A stable string key in the form <c>nodeId_slotId</c>.
        /// </returns>
        public static string GetName(string nodeId, int slotId)
        {
            string s = string.Format("{0}_{1}",
                       nodeId,
                       slotId);
            return s;
        }

        internal void BindTexture(SlotRef slotRef, GraphRenderTexture texture)
        {
            string name = GetName(slotRef.nodeId, slotRef.slotId);
            texture.identifier = name;
            m_renderTextures.Add(texture);
        }

        internal void BindBuffer(SlotRef slotRef, GraphBuffer buffer)
        {
            string name = GetName(slotRef.nodeId, slotRef.slotId);
            buffer.identifier = name;
            m_buffers.Add(buffer);
        }
    }
}
#endif


