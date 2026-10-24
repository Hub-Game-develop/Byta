// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// 线程局部的 GL 状态缓存，用来消掉绘制循环里最贵的那批冗余状态切换。
/// <para>
/// 状态是按"上下文 + 当前线程"存在的，所以缓存也按线程存放：整个缓存是一个线程私有对象，
/// 每次访问只做一次线程静态读取 + 普通字段比较（不用每个字段各读一次 TLS）。
/// </para>
/// <para>
/// 如果你绕过本库直接调用 <see cref="GL"/> 改了绑定，请调用 <see cref="Invalidate"/>。
/// </para>
/// </summary>
public static unsafe class GlState
{
    private const uint Unknown = uint.MaxValue;
    private const int CachedTextureUnits = 32;

    private sealed class Cache
    {
        public uint VertexArray = Unknown;
        public uint Program = Unknown;
        public uint ArrayBuffer = Unknown;
        public uint ElementBuffer = Unknown;
        public uint UniformBuffer = Unknown;
        public uint ShaderStorageBuffer = Unknown;
        public uint DrawFramebuffer = Unknown;
        public uint ReadFramebuffer = Unknown;
        public uint Renderbuffer = Unknown;
        public uint ActiveTextureUnit;

        public readonly uint[] Textures2D = CreateTextureUnits();

        public int ViewportX, ViewportY, ViewportWidth = -1, ViewportHeight;

        /// <summary>bit = 某个 <see cref="GlCapability"/> 是否开启。</summary>
        public ulong CapabilitiesEnabled;

        /// <summary>bit = 该能力的状态是否可信（新建上下文默认全关，直接可信）。</summary>
        public ulong CapabilitiesKnown = ulong.MaxValue;

        private static uint[] CreateTextureUnits()
        {
            var units = new uint[CachedTextureUnits];
            Array.Fill(units, Unknown);
            return units;
        }
    }

    [ThreadStatic] private static Cache? t_cache;

    private static Cache C
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => t_cache ??= new Cache();
    }

    /// <summary>关掉后所有状态调用直通驱动，便于定位"是不是缓存骗了我"。</summary>
    public static bool CacheEnabled { get; set; } = true;

    public static uint BoundVertexArray => C.VertexArray;
    public static uint BoundProgram => C.Program;
    public static uint ActiveTextureUnit => C.ActiveTextureUnit;

    /// <summary>把缓存重置成"全新上下文"的样子（换上下文后调用）。</summary>
    public static void Reset() => t_cache = new Cache();

    /// <summary>把缓存全部标成"未知"，之后每次调用都会真的下发到驱动。</summary>
    public static void Invalidate() => t_cache = new Cache();

    /// <summary>贴图被删除后清掉单元缓存（单元数量少，整体失效最省心也最安全）。</summary>
    public static void InvalidateTextures() => Array.Fill(C.Textures2D, Unknown);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BindVertexArray(uint id)
    {
        var cache = C;
        if (CacheEnabled && cache.VertexArray == id)
            return;

        GlApi.BindVertexArray(id);
        cache.VertexArray = id;
        // 索引缓冲绑定是 VAO 状态而不是全局状态：换 VAO 必须让该缓存失效，
        // 否则会把上一个 VAO 的索引缓冲当成这一个的（很隐蔽的 bug）。
        cache.ElementBuffer = Unknown;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void UseProgram(uint id)
    {
        var cache = C;
        if (CacheEnabled && cache.Program == id)
            return;
        GlApi.UseProgram(id);
        cache.Program = id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BindBuffer(BufferTarget target, uint id)
    {
        var cache = C;
        if (CacheEnabled)
        {
            switch (target)
            {
                case BufferTarget.ArrayBuffer:
                    if (cache.ArrayBuffer == id) return;
                    cache.ArrayBuffer = id;
                    break;
                case BufferTarget.ElementArrayBuffer:
                    if (cache.ElementBuffer == id) return;
                    cache.ElementBuffer = id;
                    break;
                case BufferTarget.UniformBuffer:
                    if (cache.UniformBuffer == id) return;
                    cache.UniformBuffer = id;
                    break;
                case BufferTarget.ShaderStorageBuffer:
                    if (cache.ShaderStorageBuffer == id) return;
                    cache.ShaderStorageBuffer = id;
                    break;
                default:
                    break;
            }
        }

        GlApi.BindBuffer((uint)target, id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BindTexture2D(uint unit, uint id)
    {
        var cache = C;
        var units = cache.Textures2D;

        if (unit >= (uint)units.Length)
        {
            // 超出缓存范围的单元：直接直通，不进缓存
            if (cache.ActiveTextureUnit != unit)
            {
                GlApi.ActiveTexture(GL.TEXTURE0 + unit);
                cache.ActiveTextureUnit = unit;
            }
            GlApi.BindTexture(GL.TEXTURE_2D, id);
            return;
        }

        if (CacheEnabled && units[unit] == id)
        {
            if (cache.ActiveTextureUnit != unit)
            {
                GlApi.ActiveTexture(GL.TEXTURE0 + unit);
                cache.ActiveTextureUnit = unit;
            }
            return;
        }

        if (cache.ActiveTextureUnit != unit)
        {
            GlApi.ActiveTexture(GL.TEXTURE0 + unit);
            cache.ActiveTextureUnit = unit;
        }
        GlApi.BindTexture(GL.TEXTURE_2D, id);
        if (CacheEnabled)
            units[unit] = id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BindFramebuffer(FramebufferTarget target, uint id)
    {
        var cache = C;
        switch (target)
        {
            case FramebufferTarget.Draw:
                if (CacheEnabled && cache.DrawFramebuffer == id) return;
                GlApi.BindFramebuffer(GL.DRAW_FRAMEBUFFER, id);
                cache.DrawFramebuffer = id;
                return;
            case FramebufferTarget.Read:
                if (CacheEnabled && cache.ReadFramebuffer == id) return;
                GlApi.BindFramebuffer(GL.READ_FRAMEBUFFER, id);
                cache.ReadFramebuffer = id;
                return;
            default:
                if (CacheEnabled && cache.DrawFramebuffer == id && cache.ReadFramebuffer == id) return;
                GlApi.BindFramebuffer(GL.FRAMEBUFFER, id);
                cache.DrawFramebuffer = id;
                cache.ReadFramebuffer = id;
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BindRenderbuffer(uint id)
    {
        var cache = C;
        if (CacheEnabled && cache.Renderbuffer == id)
            return;
        GlApi.BindRenderbuffer(GL.RENDERBUFFER, id);
        cache.Renderbuffer = id;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Viewport(int x, int y, int width, int height)
    {
        var cache = C;
        if (CacheEnabled && cache.ViewportX == x && cache.ViewportY == y
            && cache.ViewportWidth == width && cache.ViewportHeight == height)
            return;

        GlApi.Viewport(x, y, width, height);
        cache.ViewportX = x;
        cache.ViewportY = y;
        cache.ViewportWidth = width;
        cache.ViewportHeight = height;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Enable(GlCapability capability)
    {
        var cache = C;
        ulong bit = 1UL << (int)capability;
        if (CacheEnabled && (cache.CapabilitiesKnown & bit) != 0 && (cache.CapabilitiesEnabled & bit) != 0)
            return;

        GlApi.Enable(CapabilityToGl(capability));
        cache.CapabilitiesEnabled |= bit;
        cache.CapabilitiesKnown |= bit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Disable(GlCapability capability)
    {
        var cache = C;
        ulong bit = 1UL << (int)capability;
        if (CacheEnabled && (cache.CapabilitiesKnown & bit) != 0 && (cache.CapabilitiesEnabled & bit) == 0)
            return;

        GlApi.Disable(CapabilityToGl(capability));
        cache.CapabilitiesEnabled &= ~bit;
        cache.CapabilitiesKnown |= bit;
    }

    public static bool IsEnabled(GlCapability capability)
    {
        var cache = C;
        ulong bit = 1UL << (int)capability;
        if (CacheEnabled && (cache.CapabilitiesKnown & bit) != 0)
            return (cache.CapabilitiesEnabled & bit) != 0;

        bool enabled = GlApi.IsEnabled(CapabilityToGl(capability)) != 0;
        if (enabled)
            cache.CapabilitiesEnabled |= bit;
        else
            cache.CapabilitiesEnabled &= ~bit;
        cache.CapabilitiesKnown |= bit;
        return enabled;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BlendFunc(BlendFactor source, BlendFactor destination)
        => GlApi.BlendFunc((uint)source, (uint)destination);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DepthFunc(DepthFunc func) => GlApi.DepthFunc((uint)func);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DepthMask(bool write) => GlApi.DepthMask(write ? (byte)1 : (byte)0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CullFace(FaceCulling face) => GlApi.CullFace((uint)face);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearColor(float r, float g, float b, float a) => GlApi.ClearColor(r, g, b, a);

    private static uint CapabilityToGl(GlCapability capability) => capability switch
    {
        GlCapability.Blend => GL.BLEND,
        GlCapability.DepthTest => GL.DEPTH_TEST,
        GlCapability.CullFace => GL.CULL_FACE,
        GlCapability.ScissorTest => GL.SCISSOR_TEST,
        GlCapability.FramebufferSrgb => GL.FRAMEBUFFER_SRGB,
        GlCapability.Multisample => GL.MULTISAMPLE,
        GlCapability.StencilTest => GL.STENCIL_TEST,
        GlCapability.PolygonOffsetFill => GL.POLYGON_OFFSET_FILL,
        GlCapability.PrimitiveRestart => GL.PRIMITIVE_RESTART,
        GlCapability.RasterizerDiscard => GL.RASTERIZER_DISCARD,
        GlCapability.DepthClamp => GL.DEPTH_CLAMP,
        GlCapability.SampleAlphaToCoverage => GL.SAMPLE_ALPHA_TO_COVERAGE,
        GlCapability.SampleCoverage => GL.SAMPLE_COVERAGE,
        GlCapability.Dither => GL.DITHER,
        _ => GL.BLEND,
    };
}
