// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Byta.Bindings;

namespace Byta.Platform;

/// <summary>
/// macOS 上的无窗口 OpenGL 上下文（CGL）。
/// <para>
/// 不需要 NSWindow / NSOpenGLView，也就绕开了 AppKit 必须在主线程跑的限制：
/// 可以纯后台线程渲染 + 把结果读回内存（离屏渲染、贴图预处理、CI 里跑渲染测试都够用）。
/// 渲染目标需要自己挂 FBO —— 这正是 <see cref="GlFramebuffer"/> 的用途。
/// </para>
/// <para>
/// 上限是 GL 4.1 core（Apple 的实现建立在 Metal 之上，不再往上加了）。
/// </para>
/// </summary>
public sealed unsafe class MacCglContext : IGlContext
{
    // CGL 常量
    private const uint kCGLPFAAllRenderers = 1;
    private const uint kCGLPFAColorSize = 8;
    private const uint kCGLPFAAlphaSize = 11;
    private const uint kCGLPFADepthSize = 12;
    private const uint kCGLPFAStencilSize = 13;
    private const uint kCGLPFASampleBuffers = 55;
    private const uint kCGLPFASamples = 56;
    private const uint kCGLPFAAccelerated = 73;
    private const uint kCGLPFAOpenGLProfile = 99;

    private const uint kCGLOGLPVersion_3_2_Core = 0x3200;
    private const uint kCGLOGLPVersion_4_1_Core = 0x4100;

    private static delegate* unmanaged<uint*, nint*, int*, int> s_choosePixelFormat;
    private static delegate* unmanaged<nint, nint, nint*, int> s_createContext;
    private static delegate* unmanaged<nint, int> s_destroyContext;
    private static delegate* unmanaged<nint, int> s_setCurrentContext;
    private static delegate* unmanaged<nint> s_getCurrentContext;
    private static delegate* unmanaged<nint, int> s_destroyPixelFormat;
    private static bool s_loaded;

    private nint _pixelFormat;
    private nint _context;
    private bool _disposed;

    private MacCglContext(nint pixelFormat, nint context)
    {
        _pixelFormat = pixelFormat;
        _context = context;
    }

    public bool IsCurrent => s_getCurrentContext() == _context;
    public nint Handle => _context;

    /// <summary>
    /// 创建一个离屏上下文。<paramref name="version"/> 目前只支持 (3,2) 和 (4,1)。
    /// </summary>
    public static MacCglContext Create(int major = 4, int minor = 1, bool depthBuffer = true, int samples = 0)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("MacCglContext 只在 macOS 上可用。");

        EnsureLoaded();

        uint profile = (major, minor) switch
        {
            (4, _) => kCGLOGLPVersion_4_1_Core,
            (3, 2) => kCGLOGLPVersion_3_2_Core,
            _ => throw new ArgumentException("macOS 只支持 3.2 core 或 4.1 core 上下文。", nameof(major)),
        };

        Span<uint> attributes = stackalloc uint[16];
        int i = 0;
        attributes[i++] = kCGLPFAOpenGLProfile;
        attributes[i++] = profile;
        attributes[i++] = kCGLPFAAccelerated;
        attributes[i++] = kCGLPFAColorSize;
        attributes[i++] = 24;
        attributes[i++] = kCGLPFAAlphaSize;
        attributes[i++] = 8;
        if (depthBuffer)
        {
            attributes[i++] = kCGLPFADepthSize;
            attributes[i++] = 24;
            attributes[i++] = kCGLPFAStencilSize;
            attributes[i++] = 8;
        }
        if (samples > 1)
        {
            attributes[i++] = kCGLPFASampleBuffers;
            attributes[i++] = 1;
            attributes[i++] = kCGLPFASamples;
            attributes[i++] = (uint)samples;
        }
        attributes[i] = 0; // 结尾哨兵

        nint pixelFormat = 0;
        int count = 0;
        int error;
        fixed (uint* attrs = attributes)
            error = s_choosePixelFormat(attrs, &pixelFormat, &count);

        if (error != 0)
            throw new GlException($"CGLChoosePixelFormat 失败：{CglErrorName(error)}");
        if (pixelFormat == 0)
            throw new GlException("CGLChoosePixelFormat 没有返回可用的像素格式（这台机器可能禁用了硬件加速）。");

        nint context = 0;
        error = s_createContext(pixelFormat, 0, &context);
        if (error != 0 || context == 0)
        {
            s_destroyPixelFormat(pixelFormat);
            throw new GlException($"CGLCreateContext 失败：{CglErrorName(error)}");
        }

        return new MacCglContext(pixelFormat, context);
    }

    public void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int error = s_setCurrentContext(_context);
        if (error != 0)
            throw new GlException($"CGLSetCurrentContext 失败：{CglErrorName(error)}");

        // 新上下文 = GL 默认状态，缓存直接按默认值重置，不用发任何 GL 调用
        GlState.Reset();
    }

    public void Flush()
    {
        if (GL.Flush is not null)
            GL.Flush();
    }

    /// <summary>解除当前线程的上下文绑定（退出渲染线程前调用）。</summary>
    public static void ClearCurrent() => s_setCurrentContext(0);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (s_getCurrentContext() == _context)
            s_setCurrentContext(0);

        if (_context != 0)
        {
            s_destroyContext(_context);
            _context = 0;
        }
        if (_pixelFormat != 0)
        {
            s_destroyPixelFormat(_pixelFormat);
            _pixelFormat = 0;
        }
    }

    private static void EnsureLoaded()
    {
        if (s_loaded)
            return;

        nint framework = NativeLibrary.Load("/System/Library/Frameworks/OpenGL.framework/OpenGL");

        nint Get(string name) =>
            NativeLibrary.TryGetExport(framework, name, out nint address)
                ? address
                : throw new GlException($"OpenGL.framework 里找不到 {name}。");

        s_choosePixelFormat = (delegate* unmanaged<uint*, nint*, int*, int>)Get("CGLChoosePixelFormat");
        s_createContext = (delegate* unmanaged<nint, nint, nint*, int>)Get("CGLCreateContext");
        s_destroyContext = (delegate* unmanaged<nint, int>)Get("CGLDestroyContext");
        s_setCurrentContext = (delegate* unmanaged<nint, int>)Get("CGLSetCurrentContext");
        s_getCurrentContext = (delegate* unmanaged<nint>)Get("CGLGetCurrentContext");
        s_destroyPixelFormat = (delegate* unmanaged<nint, int>)Get("CGLDestroyPixelFormat");
        s_loaded = true;
    }

    private static string CglErrorName(int error) => error switch
    {
        10000 => "kCGLBadAttribute",
        10001 => "kCGLBadProperty",
        10002 => "kCGLBadPixelFormat",
        10003 => "kCGLBadRendererInfo",
        10004 => "kCGLBadContext",
        10005 => "kCGLBadDrawable",
        10006 => "kCGLBadDisplay",
        10007 => "kCGLBadState",
        10008 => "kCGLBadValue",
        10009 => "kCGLBadMatch",
        10010 => "kCGLBadEnumeration",
        10011 => "kCGLBadOffScreen",
        10012 => "kCGLBadFullScreen",
        10013 => "kCGLBadWindow",
        10014 => "kCGLBadAddress",
        10015 => "kCGLBadCodeModule",
        10016 => "kCGLBadAlloc",
        10017 => "kCGLInvalidPixelFormat",
        10018 => "kCGLInvalidRenderer",
        10019 => "kCGLInvalidContext",
        10020 => "kCGLInvalidDrawable",
        10021 => "kCGLGLVersionMismatch",
        _ => $"CGLError({error})",
    };
}
