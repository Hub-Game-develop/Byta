using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Byta.Bindings;

/// <summary>
/// 把入口点名字解析成本进程内的函数地址。只在 <see cref="GL.Load"/> 期间调用，
/// 因此可以用托管委托而不会影响绘制热路径。
/// </summary>
public delegate nint GlProcResolver(string name);

/// <summary>
/// OpenGL 入口点表。
/// <para>
/// 生成代码里每条命令都是一个 <c>delegate* unmanaged&lt;...&gt;</c> 字段，调用方式：
/// <c>GL.GenBuffers(1, &amp;id)</c> —— 直接间接跳转，没有 DllImport stub、没有 marshal 开销。
/// </para>
/// <para>
/// 入口点与"上下文"绑定：换了 GL 上下文（或在另一个线程首次使用）之后需要重新 <see cref="Load"/>。
/// 加载失败（驱动/版本不提供）的入口点会保留为 null，调用前用 <c>?.</c> 或 <see cref="Missing"/> 检查。
/// </para>
/// </summary>
public static unsafe partial class GL
{
    private static readonly List<string> s_missing = new(64);
    private static GlProcResolver? s_resolver;

    /// <summary>是否已经加载过入口点表。</summary>
    public static bool IsLoaded { get; private set; }

    /// <summary>当前上下文没有提供的入口点（按 Khronos 原名）。</summary>
    public static IReadOnlyList<string> Missing => s_missing;

    /// <summary>
    /// 解析全部入口点。缺失项会被记录但不抛异常 —— 因为很多扩展本来就可能不存在。
    /// 只有在连 <c>glGetError</c> 都解析不到时才抛 <see cref="GlBindingsException"/>，
    /// 这通常意味着"没有当前上下文"或解析器用错了。
    /// </summary>
    public static void Load(GlProcResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        s_resolver = resolver;
        s_missing.Clear();
        ResolveAll(resolver, s_missing);
        IsLoaded = true;

        if (GetError is null || GetString is null)
        {
            IsLoaded = false;
            throw new GlBindingsException(
                "关键入口点 glGetError/glGetString 解析失败。通常是当前线程没有绑定 GL 上下文，或解析器指向了错误的库。");
        }
    }

    /// <summary>加载入口点表，并把缺失项交给调用方处理。返回是否全部解析成功。</summary>
    public static bool TryLoad(GlProcResolver resolver, out string[] missing)
    {
        Load(resolver);
        missing = s_missing.ToArray();
        return s_missing.Count == 0;
    }

    /// <summary>
    /// 查询任意入口点地址（可用于生成表之外的扩展）。没有解析器时返回 0。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint GetProcAddress(string name) => s_resolver is null ? 0 : s_resolver(name);

    /// <summary>某个入口点是否可用（缓存友好的一次委托调用，仅适合初始化期使用）。</summary>
    public static bool Has(string name) => GetProcAddress(name) != 0;

    /// <summary>清空入口点表（例如 GL 上下文被销毁后）。</summary>
    public static void Unload()
    {
        ResolveAll(static _ => 0, null);
        s_missing.Clear();
        s_resolver = null;
        IsLoaded = false;
        MissingCount = 0;
    }
}

/// <summary>绑定层异常。</summary>
public sealed class GlBindingsException(string message) : Exception(message);
