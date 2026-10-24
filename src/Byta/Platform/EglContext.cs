// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Byta.Bindings;

namespace Byta.Platform;

/// <summary>EGL 常量（只列用到的）。</summary>
internal static class Egl
{
    public const int Success = 0x3000;
    public const int NotInitialized = 0x3001;
    public const int BadAccess = 0x3002;
    public const int BadAlloc = 0x3003;
    public const int BadAttribute = 0x3004;
    public const int BadConfig = 0x3005;
    public const int BadContext = 0x3006;
    public const int BadDisplay = 0x3008;
    public const int BadMatch = 0x3009;
    public const int BadSurface = 0x300D;

    public const nint NoContext = 0;
    public const nint DefaultDisplay = 0;

    public const int OpenGLApi = 0x30A2;

    public const int SurfaceType = 0x3033;
    public const int PbufferBit = 0x0001;
    public const int RenderableType = 0x3040;
    public const int OpenGLBit = 0x0008;

    public const int RedSize = 0x3024;
    public const int GreenSize = 0x3023;
    public const int BlueSize = 0x3022;
    public const int AlphaSize = 0x3021;
    public const int DepthSize = 0x3025;
    public const int Width = 0x3057;
    public const int Height = 0x3056;

    public const int ContextMajorVersion = 0x3098;
    public const int ContextMinorVersion = 0x30FB;
    public const int ContextOpenGLProfileMask = 0x30FD;
    public const int ContextOpenGLCoreProfileBit = 0x0001;

    public const int None = 0x3038;

    /// <summary>EGL_PLATFORM_SURFACELESS_MESA —— Mesa 的无窗口平台。</summary>
    public const int PlatformSurfacelessMesa = 0x31DD;

    public static string ErrorName(int error) => error switch
    {
        Success => "EGL_SUCCESS",
        NotInitialized => "EGL_NOT_INITIALIZED",
        BadAccess => "EGL_BAD_ACCESS",
        BadAlloc => "EGL_BAD_ALLOC",
        BadAttribute => "EGL_BAD_ATTRIBUTE",
        BadConfig => "EGL_BAD_CONFIG",
        BadContext => "EGL_BAD_CONTEXT",
        BadDisplay => "EGL_BAD_DISPLAY",
        BadMatch => "EGL_BAD_MATCH",
        BadSurface => "EGL_BAD_SURFACE",
        _ => $"EGL error 0x{error:X4}",
    };
}

/// <summary>
/// 基于 EGL 的离屏上下文 —— 用来加载<b>系统之外</b>的 GL 实现（Mesa 的 libEGL）。
/// <para>
/// 用 1×1 的 pbuffer 做当前表面，真正渲染一律进 FBO，所以完全不依赖窗口系统。
/// 配 Mesa 的 zink 后端，可以在 macOS 上拿到 GL 4.6（GL→Vulkan→Metal），
/// 从而绕开 Apple 那个封顶 4.1 的实现。
/// </para>
/// </summary>
public sealed unsafe class EglContext : IGlContext
{
    private nint _library;
    private nint _display;
    private nint _surface;
    private nint _context;
    private readonly delegate* unmanaged<byte*, nint> _procAddress;

    private EglContext(
        nint library, nint display, nint surface, nint context,
        delegate* unmanaged<byte*, nint> procAddress, string? driver)
    {
        _library = library;
        _display = display;
        _surface = surface;
        _context = context;
        _procAddress = procAddress;
        DriverName = driver ?? "(驱动默认)";
    }

    /// <summary>实际生效的驱动名：<c>zink</c> / <c>llvmpipe</c> / <c>(驱动默认)</c>。</summary>
    public string DriverName { get; }

    public nint Handle => _context;

    public bool IsCurrent
    {
        get
        {
            if (_library == 0)
                return false;
            var getCurrent = (delegate* unmanaged<nint>)NativeLibrary.GetExport(_library, "eglGetCurrentContext");
            return getCurrent() == _context;
        }
    }

    public void MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(_library == 0, this);
        var makeCurrent = (delegate* unmanaged<nint, nint, nint, nint, int>)NativeLibrary.GetExport(_library, "eglMakeCurrent");
        if (makeCurrent(_display, _surface, _surface, _context) == 0)
            throw new GlException($"eglMakeCurrent 失败：{Egl.ErrorName(LastError(_library))}");

        // 新上下文 = GL 默认状态
        GlState.Reset();
    }

    public void Flush()
    {
        if (_library == 0)
            return;
        if (_surface != 0)
        {
            var swap = (delegate* unmanaged<nint, nint, int>)NativeLibrary.GetExport(_library, "eglSwapBuffers");
            swap(_display, _surface);
        }
        else if (GlApi.HasFlush)
        {
            GlApi.Flush();
        }
    }

    public void Dispose()
    {
        if (_library == 0)
            return;

        var makeCurrent = (delegate* unmanaged<nint, nint, nint, nint, int>)NativeLibrary.GetExport(_library, "eglMakeCurrent");
        makeCurrent(_display, 0, 0, 0);

        if (_context != 0)
        {
            var destroyContext = (delegate* unmanaged<nint, nint, int>)NativeLibrary.GetExport(_library, "eglDestroyContext");
            destroyContext(_display, _context);
            _context = 0;
        }
        if (_surface != 0)
        {
            var destroySurface = (delegate* unmanaged<nint, nint, int>)NativeLibrary.GetExport(_library, "eglDestroySurface");
            destroySurface(_display, _surface);
            _surface = 0;
        }
        if (_display != 0)
        {
            var terminate = (delegate* unmanaged<nint, int>)NativeLibrary.GetExport(_library, "eglTerminate");
            terminate(_display);
            _display = 0;
        }

        _library = 0;
    }

    /// <summary>
    /// 不建上下文，先拿一个入口点解析器（eglGetProcAddress 是库级的，和 display 无关）。
    /// </summary>
    public static GlProcResolver CreateResolver(string libraryPath)
    {
        nint library = NativeLibrary.Load(libraryPath);
        var getProcAddress = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(library, "eglGetProcAddress");
        return name =>
        {
            byte* ascii = stackalloc byte[256];
            int length = System.Text.Encoding.ASCII.GetBytes(name, new Span<byte>(ascii, 255));
            ascii[length] = 0;
            return getProcAddress(ascii);
        };
    }

    /// <summary>EGL 的 <c>eglGetProcAddress</c> → <see cref="GlProcResolver"/>。</summary>
    public GlProcResolver CreateResolver() => name =>
    {
        byte* ascii = stackalloc byte[256];
        int length = System.Text.Encoding.ASCII.GetBytes(name, new Span<byte>(ascii, 255));
        ascii[length] = 0;
        return _procAddress(ascii);
    };

    /// <summary>
    /// 按顺序尝试多个驱动（zink 走 Vulkan，llvmpipe 走 CPU），返回第一个能建出上下文的。
    /// </summary>
    public static EglContext Create(
        string libraryPath, int major, int minor, IReadOnlyList<string?>? drivers = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(libraryPath);
        // zink 在 MoltenVK 上会因为缺 VK_EXT_robustness2 的 nullDescriptor 而被拒绝，
        // 所以默认只用 llvmpipe；想试 zink 就设 BYTA_EGL_DRIVER=zink。
        drivers ??= [Environment.GetEnvironmentVariable("BYTA_EGL_DRIVER") ?? "llvmpipe"];

        var failures = new List<string>(drivers.Count);
        foreach (var driver in drivers)
        {
            try
            {
                return CreateOnce(libraryPath, major, minor, driver);
            }
            catch (GlException ex)
            {
                failures.Add($"{(driver ?? "默认")}: {ex.Message}");
            }
        }

        throw new GlException(
            $"用 {Path.GetFileName(libraryPath)} 建 {major}.{minor} core 上下文失败：\n  " + string.Join("\n  ", failures));
    }

    private static EglContext CreateOnce(string libraryPath, int major, int minor, string? driverOverride)
    {
        // Mesa 靠这个环境变量找 *_dri.dylib
        if (Environment.GetEnvironmentVariable("LIBGL_DRIVERS_PATH") is null)
        {
            var driversDirectory = Path.Combine(Path.GetDirectoryName(libraryPath)!, "dri");
            if (Directory.Exists(driversDirectory))
                Environment.SetEnvironmentVariable("LIBGL_DRIVERS_PATH", driversDirectory);
        }

        if (driverOverride is not null)
        {
            Environment.SetEnvironmentVariable("MESA_LOADER_DRIVER_OVERRIDE", driverOverride);

            // zink 需要 Vulkan ICD；把它指到随库分发的 MoltenVK（如果存在）
            if (driverOverride == "zink"
                && Environment.GetEnvironmentVariable("VK_ICD_FILENAMES") is null
                && Environment.GetEnvironmentVariable("VK_DRIVER_FILES") is null)
            {
                var directory = Path.GetDirectoryName(libraryPath)!;
                foreach (var candidate in new[]
                         {
                             Path.Combine(directory, "vulkan", "icd.d", "MoltenVK_icd.json"),
                             Path.Combine(directory, "MoltenVK_icd.json"),
                         })
                {
                    if (File.Exists(candidate))
                    {
                        Environment.SetEnvironmentVariable("VK_ICD_FILENAMES", candidate);
                        break;
                    }
                }
            }
        }

        nint library = NativeLibrary.Load(libraryPath);

        nint GetExport(string name)
        {
            if (!NativeLibrary.TryGetExport(library, name, out nint address))
            {
                NativeLibrary.Free(library);
                throw new GlException($"{Path.GetFileName(libraryPath)} 缺少导出 {name}。");
            }
            return address;
        }

        try
        {
            // EGL 1.5 的核心函数；1.4 驱动只有 EXT 扩展版；再不行退回默认显示
            nint getPlatformDisplayCorePtr = NativeLibrary.TryGetExport(library, "eglGetPlatformDisplay", out nint gpd) ? gpd : 0;
            nint getPlatformDisplayPtr = NativeLibrary.TryGetExport(library, "eglGetPlatformDisplayEXT", out nint pgd) ? pgd : 0;
            var getDisplay = (delegate* unmanaged<nint, nint>)GetExport("eglGetDisplay");
            var initialize = (delegate* unmanaged<nint, int*, int*, int>)GetExport("eglInitialize");
            var bindApi = (delegate* unmanaged<int, int>)GetExport("eglBindAPI");
            var chooseConfig = (delegate* unmanaged<nint, int*, nint*, int, int*, int>)GetExport("eglChooseConfig");
            var createContext = (delegate* unmanaged<nint, nint, nint, int*, nint>)GetExport("eglCreateContext");
            var createPbuffer = (delegate* unmanaged<nint, nint, int*, nint>)GetExport("eglCreatePbufferSurface");
            var makeCurrent = (delegate* unmanaged<nint, nint, nint, nint, int>)GetExport("eglMakeCurrent");
            var getProcAddress = (delegate* unmanaged<byte*, nint>)GetExport("eglGetProcAddress");

            // 1) 显示连接：优先 Mesa 的无窗口平台（surfaceless），退回默认显示
            nint display = 0;
            if (getPlatformDisplayCorePtr != 0)
            {
                var getPlatformDisplay = (delegate* unmanaged<uint, nint, int*, nint>)getPlatformDisplayCorePtr;
                display = getPlatformDisplay(Egl.PlatformSurfacelessMesa, Egl.DefaultDisplay, null);
            }
            if (display == 0 && getPlatformDisplayPtr != 0)
            {
                var getPlatformDisplayExt = (delegate* unmanaged<uint, nint, int*, nint>)getPlatformDisplayPtr;
                display = getPlatformDisplayExt(Egl.PlatformSurfacelessMesa, Egl.DefaultDisplay, null);
            }
            if (display == 0)
                display = getDisplay(Egl.DefaultDisplay);
            if (display == 0)
                throw new GlException("eglGetDisplay 返回了 EGL_NO_DISPLAY（该实现可能没有编译进 surfaceless 平台）");

            int eglMajor = 0, eglMinor = 0;
            if (initialize(display, &eglMajor, &eglMinor) == 0)
                throw new GlException($"eglInitialize 失败：{Egl.ErrorName(LastError(library))}");

            if (bindApi(Egl.OpenGLApi) == 0)
                throw new GlException($"eglBindAPI(EGL_OPENGL_API) 失败：{Egl.ErrorName(LastError(library))}");

            // 2) 配置：1×1 pbuffer + 桌面 GL
            int* configAttributes = stackalloc int[]
            {
                Egl.SurfaceType, Egl.PbufferBit,
                Egl.RenderableType, Egl.OpenGLBit,
                Egl.RedSize, 8, Egl.GreenSize, 8, Egl.BlueSize, 8, Egl.AlphaSize, 8,
                Egl.DepthSize, 24,
                Egl.None,
            };
            nint config = 0;
            int configCount = 0;
            if (chooseConfig(display, configAttributes, &config, 1, &configCount) == 0 || configCount == 0)
                throw new GlException($"eglChooseConfig 没有可用配置：{Egl.ErrorName(LastError(library))}");

            int* surfaceAttributes = stackalloc int[] { Egl.Width, 1, Egl.Height, 1, Egl.None };
            nint surface = createPbuffer(display, config, surfaceAttributes);

            // 3) core profile 上下文，版本由调用方指定
            int* contextAttributes = stackalloc int[]
            {
                Egl.ContextMajorVersion, major,
                Egl.ContextMinorVersion, minor,
                Egl.ContextOpenGLProfileMask, Egl.ContextOpenGLCoreProfileBit,
                Egl.None,
            };
            nint context = createContext(display, config, Egl.NoContext, contextAttributes);
            if (context == 0)
                throw new GlException($"eglCreateContext({major}.{minor} core) 失败：{Egl.ErrorName(LastError(library))}");

            if (makeCurrent(display, surface, surface, context) == 0)
                throw new GlException($"eglMakeCurrent 失败：{Egl.ErrorName(LastError(library))}");

            return new EglContext(library, display, surface, context, getProcAddress, driverOverride);
        }
        catch
        {
            NativeLibrary.Free(library);
            throw;
        }
    }

    private static int LastError(nint library)
    {
        var getError = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "eglGetError");
        return getError();
    }
}
