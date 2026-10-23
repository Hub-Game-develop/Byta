using System.Runtime.InteropServices;
using Byta.Bindings;

namespace Byta.Platform;

/// <summary>
/// 各平台"当前进程"里找 GL 入口点的默认做法。
/// <para>
/// 注意：GL 的入口点其实是<b>跟上下文走的</b>。托管运行时无法参与上下文的创建，
/// 所以这里用的是各平台最可靠的进程级查询方式：
/// </para>
/// <list type="bullet">
/// <item>macOS：<c>OpenGL.framework</c> 导出了全部核心函数符号，直接 dlsym。</item>
/// <item>Windows：<c>opengl32.dll</c> 只有 GL 1.1 的符号，其余必须问 <c>wglGetProcAddress</c>（依赖当前上下文）。</item>
/// <item>Linux：<c>glXGetProcAddressARB</c> 或 EGL 的 <c>eglGetProcAddress</c>。</item>
/// </list>
/// 如果你用的是 GLFW / SDL / Avalonia 之类的宿主，用它们自带的 getProcAddress 更稳，
/// 直接把那个函数的地址包成 <see cref="GlProcResolver"/> 即可。
/// </summary>
public static unsafe class GlProcResolvers
{
    private static nint s_macOpenGL;
    private static nint s_opengl32;

    /// <summary>按当前操作系统选择解析器。</summary>
    public static GlProcResolver ForCurrentProcess()
    {
        if (OperatingSystem.IsMacOS())
            return MacOS();
        if (OperatingSystem.IsWindows())
            return Windows();
        return Linux();
    }

    /// <summary>macOS：OpenGL.framework 用 dlsym 直接找符号（GL 4.1 的函数全都导出）。</summary>
    public static GlProcResolver MacOS()
    {
        if (s_macOpenGL == 0)
        {
            s_macOpenGL = NativeLibrary.Load("/System/Library/Frameworks/OpenGL.framework/OpenGL");
        }
        return name =>
            NativeLibrary.TryGetExport(s_macOpenGL, name, out nint address) ? address : 0;
    }

    /// <summary>
    /// Windows：先试 <c>wglGetProcAddress</c>，失败再回落到 <c>opengl32.dll</c> 的导出表
    /// （GL 1.1 那批函数不在 wglGetProcAddress 里）。
    /// </summary>
    public static GlProcResolver Windows()
    {
        if (s_opengl32 == 0)
        {
            s_opengl32 = NativeLibrary.Load("opengl32.dll");
        }

        nint wglGetProcAddress = NativeLibrary.TryGetExport(s_opengl32, "wglGetProcAddress", out nint p) ? p : 0;
        var getProc = wglGetProcAddress == 0
            ? null
            : (delegate* unmanaged<byte*, nint>)wglGetProcAddress;

        nint glHandle = s_opengl32;
        return name =>
        {
            if (getProc is not null)
            {
                byte* ascii = stackalloc byte[128];
                int length = System.Text.Encoding.ASCII.GetBytes(name, new Span<byte>(ascii, 127));
                ascii[length] = 0;
                nint address = getProc(ascii);
                // wglGetProcAddress 对不支持的名字会返回 1/2/3/-1 这些哨兵值
                if (address > 3)
                    return address;
            }
            return NativeLibrary.TryGetExport(glHandle, name, out nint fallback) ? fallback : 0;
        };
    }

    /// <summary>Linux：glXGetProcAddressARB 是事实标准（EGL 也能用同样的签名）。</summary>
    public static GlProcResolver Linux()
    {
        nint libGL = NativeLibrary.TryLoad("libGL.so.1", out nint handle) ? handle : 0;
        if (libGL == 0 && NativeLibrary.TryLoad("libGL.so", out nint handle2))
            libGL = handle2;

        if (libGL == 0)
            throw new GlNotSupportedException("libGL", "找不到 libGL.so.1");

        nint glXGetProcAddressARB = NativeLibrary.TryGetExport(libGL, "glXGetProcAddressARB", out nint p) ? p : 0;
        if (glXGetProcAddressARB == 0 && NativeLibrary.TryGetExport(libGL, "glXGetProcAddress", out nint p2))
            glXGetProcAddressARB = p2;

        var getProc = glXGetProcAddressARB == 0
            ? null
            : (delegate* unmanaged<byte*, nint>)glXGetProcAddressARB;

        nint lib = libGL;
        return name =>
        {
            if (getProc is not null)
            {
                byte* ascii = stackalloc byte[128];
                int length = System.Text.Encoding.ASCII.GetBytes(name, new Span<byte>(ascii, 127));
                ascii[length] = 0;
                nint address = getProc(ascii);
                if (address != 0)
                    return address;
            }
            return NativeLibrary.TryGetExport(lib, name, out nint fallback) ? fallback : 0;
        };
    }
}
