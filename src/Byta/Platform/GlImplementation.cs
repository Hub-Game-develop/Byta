// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Runtime.InteropServices;
using Byta.Bindings;

namespace Byta.Platform;

/// <summary>一个实现从哪里来。</summary>
public enum GlImplementationSource
{
    /// <summary>随库一起分发的实现（Byta 自带）。优先使用。</summary>
    Bundled,

    /// <summary>通过 <c>BYTA_GL_DRIVER</c> 指定的实现。</summary>
    Environment,

    /// <summary>操作系统的默认实现（macOS 上是 Apple 的 OpenGL.framework，上限 4.1）。</summary>
    System,
}

/// <summary>
/// 一个可用的 OpenGL 实现：能提供入口点，也能建上下文。
/// <para>
/// Byta 不认死某一个实现 —— 自带一份（Mesa/zink）优先，其次环境变量指定，最后才是系统默认。
/// 这就是"不管系统那个低版本渲染器、直接按最高版本跑"的落地方式。
/// </para>
/// </summary>
public sealed class GlImplementation
{
    private readonly Func<int, int, IGlContext>? _contextFactory;

    internal GlImplementation(
        string name,
        string libraryPath,
        GlImplementationSource source,
        GlProcResolver resolver,
        Func<int, int, IGlContext>? contextFactory,
        GlVersion maxVersion)
    {
        Name = name;
        LibraryPath = libraryPath;
        Source = source;
        Resolver = resolver;
        _contextFactory = contextFactory;
        MaxVersion = maxVersion;
    }

    public string Name { get; }
    public string LibraryPath { get; }
    public GlImplementationSource Source { get; }

    /// <summary>入口点解析器（已绑定到该实现）。</summary>
    public GlProcResolver Resolver { get; }

    /// <summary>该实现已知的最高 GL 版本（用来决定版本请求从哪一级开始往下试）。</summary>
    public GlVersion MaxVersion { get; }

    /// <summary>该实现是否自己能建上下文（Apple CGL、Mesa EGL 可以；裸 libGL 不行）。</summary>
    public bool CanCreateContext => _contextFactory is not null;

    /// <summary>建一个离屏上下文。版本由调用方给，失败会抛 <see cref="GlException"/>。</summary>
    public IGlContext CreateContext(int major, int minor) =>
        _contextFactory is { } factory
            ? factory(major, minor)
            : throw new GlNotSupportedException(Name, "该实现不提供上下文创建，请使用宿主框架（GLFW/SDL/Avalonia）建的上下文。");

    /// <summary>为"调用方自己建的上下文"造一个占位实现（没有库路径、不能建上下文）。</summary>
    internal static GlImplementation ForHostContext(string name, GlProcResolver resolver) =>
        new(name, "(宿主上下文)", GlImplementationSource.Environment, resolver, null, new GlVersion(4, 6));

    public override string ToString() => $"{Name} [{Source}] {LibraryPath}";
}

/// <summary>发现本机可用的 OpenGL 实现，按优先级排序。</summary>
public static class GlImplementationLocator
{
    /// <summary>显式指定实现的环境变量（指向 libEGL / libGL / 系统框架）。</summary>
    public const string DriverEnvironmentVariable = "BYTA_GL_DRIVER";

    private static readonly List<string> s_diagnostics = [];

    /// <summary>
    /// 上次 <see cref="FindAll"/> 过程中被跳过的候选及原因（架构不匹配、缺依赖、找不到库……）。
    /// 用来回答"为什么没用上内置实现"这类问题。
    /// </summary>
    public static IReadOnlyList<string> Diagnostics => s_diagnostics;

    /// <summary>本次探测实际搜索过的目录（排查路径问题用）。</summary>
    public static IReadOnlyList<string> SearchDirectories { get; private set; } = [];

    /// <summary>
    /// 列出候选实现。顺序即优先级：自带 &gt; 环境变量 &gt; 系统。
    /// </summary>
    public static IReadOnlyList<GlImplementation> FindAll()
    {
        var candidates = new List<GlImplementation>(4);
        s_diagnostics.Clear();

        var bundledDirectories = GetBundledNativeDirectories().ToList();
        SearchDirectories = bundledDirectories;

        // 1) 随库自带的实现
        foreach (var directory in bundledDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            var egl = FindLibrary(directory, "libEGL");
            if (egl is null)
            {
                s_diagnostics.Add($"{directory}：没有 libEGL");
                continue;
            }

            if (!NativeArchitecture.IsCompatible(egl, out string? reason))
            {
                s_diagnostics.Add($"{Path.GetFileName(egl)}：{reason}");
                continue;
            }

            if (TryCreateEglImplementation(egl, GlImplementationSource.Bundled) is { } impl)
                candidates.Add(impl);
        }

        // 2) 环境变量指定
        var explicitPath = Environment.GetEnvironmentVariable(DriverEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            if (TryCreateEglImplementation(explicitPath, GlImplementationSource.Environment) is { } impl)
                candidates.Add(impl);
        }

        // 3) 系统默认
        if (TryCreateSystemImplementation() is { } system)
            candidates.Add(system);

        return candidates;
    }

    /// <summary>本机是否存在"比系统实现更新"的可加载实现。</summary>
    public static bool HasBundledOrOverride =>
        FindAll().Any(c => c.Source is GlImplementationSource.Bundled or GlImplementationSource.Environment);

    // ---------------------------------------------------------------- 内部

    private static IEnumerable<string> GetBundledNativeDirectories()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string rid = RuntimeInformation.RuntimeIdentifier;

        // 在 Apple Silicon 上跑 x64 进程（Rosetta）时 RID 是 osx-x64，但原生库通常是 arm64；
        // 两个目录都试一遍，架构不对的话加载会失败并自动降级，不会静默出错。
        var rids = new List<string> { rid };
        if (OperatingSystem.IsMacOS())
        {
            rids.Add(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-arm64");
            rids.Add("osx-x64");
        }
        rids = rids.Distinct().ToList();

        foreach (var candidateRid in rids)
            yield return Path.Combine(baseDirectory, "runtimes", candidateRid, "native");
        yield return Path.Combine(baseDirectory, "native");

        // 从源码树直接运行时（输出目录在 samples/xxx/bin/...），往上找仓库里的 runtimes
        var probe = new DirectoryInfo(baseDirectory);
        for (int i = 0; i < 7 && probe is not null; i++)
        {
            foreach (var candidateRid in rids)
            foreach (var candidate in new[]
                     {
                         Path.Combine(probe.FullName, "runtimes", candidateRid, "native"),
                         Path.Combine(probe.FullName, "src", "Byta", "runtimes", candidateRid, "native"),
                     })
            {
                if (Directory.Exists(candidate))
                    yield return candidate;
            }
            probe = probe.Parent;
        }
    }

    private static string? FindLibrary(string directory, string name)
    {
        if (!Directory.Exists(directory))
            return null;

        foreach (var suffix in new[] { ".dylib", ".1.dylib", ".so", ".so.1", ".dll" })
        {
            var path = Path.Combine(directory, name + suffix);
            if (File.Exists(path))
                return path;
        }

        // 兜底：目录里带同名前缀的版本化文件
        var match = Directory.EnumerateFiles(directory, name + "*").FirstOrDefault();
        return match;
    }

    private static GlImplementation? TryCreateEglImplementation(string eglPath, GlImplementationSource source)
    {
        try
        {
            string name = source == GlImplementationSource.Bundled
                ? $"Byta 内置实现（Mesa/EGL：{Path.GetFileName(eglPath)}）"
                : $"外部实现（{eglPath}）";

            return new GlImplementation(
                name, eglPath, source,
                EglContext.CreateResolver(eglPath),
                (major, minor) => EglContext.Create(eglPath, major, minor),
                new GlVersion(4, 6));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or GlException or BadImageFormatException)
        {
            s_diagnostics.Add($"{Path.GetFileName(eglPath)}：{ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }

    private static GlImplementation? TryCreateSystemImplementation()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var resolver = GlProcResolvers.MacOS();
                return new GlImplementation(
                    "系统实现：Apple OpenGL.framework（上限 4.1）",
                    "/System/Library/Frameworks/OpenGL.framework/OpenGL",
                    GlImplementationSource.System,
                    resolver,
                    (major, minor) => MacCglContext.Create(major, minor),
                    new GlVersion(4, 1));
            }

            if (OperatingSystem.IsWindows())
            {
                return new GlImplementation(
                    "系统实现：opengl32.dll",
                    "opengl32.dll",
                    GlImplementationSource.System,
                    GlProcResolvers.Windows(),
                    null,
                    new GlVersion(4, 6));
            }

            return new GlImplementation(
                "系统实现：libGL",
                "libGL.so.1",
                GlImplementationSource.System,
                GlProcResolvers.Linux(),
                null,
                new GlVersion(4, 6));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or GlException)
        {
            return null;
        }
    }
}
