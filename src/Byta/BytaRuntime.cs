using Byta.Bindings;
using Byta.Platform;

namespace Byta;

/// <summary>初始化选项。</summary>
public sealed class BytaOptions
{
    /// <summary>愿意接受的最高版本。实际会从这个版本往下逐级试。</summary>
    public GlVersion MaxVersion { get; set; } = new(4, 6);

    /// <summary>往下试到哪一版就不再试了。</summary>
    public GlVersion MinVersion { get; set; } = new(3, 3);

    /// <summary>是否优先用随库分发的实现（默认 true；设 false 就乖乖用系统那个）。</summary>
    public bool PreferBundledImplementation { get; set; } = true;

    /// <summary>诊断输出回调，用来观察"试了哪些实现 / 哪些版本 / 为什么失败"。</summary>
    public Action<string>? Log { get; set; }
}

/// <summary>一次成功的初始化：实现 + 上下文 + 能力。</summary>
public sealed class BytaSession : IDisposable
{
    internal BytaSession(
        GlImplementation implementation,
        IGlContext context,
        GlCapabilities capabilities,
        GlVersion requestedVersion,
        string driverName,
        IReadOnlyList<string> attempts)
    {
        Implementation = implementation;
        Context = context;
        Capabilities = capabilities;
        RequestedVersion = requestedVersion;
        DriverName = driverName;
        Attempts = attempts;
    }

    public GlImplementation Implementation { get; }
    public IGlContext Context { get; }
    public GlCapabilities Capabilities { get; }

    /// <summary>实际请求到的版本（可能低于 <see cref="BytaOptions.MaxVersion"/>）。</summary>
    public GlVersion RequestedVersion { get; }

    /// <summary>驱动名：zink / llvmpipe / CGL / (驱动默认)。</summary>
    public string DriverName { get; }

    /// <summary>初始化过程中尝试过的实现与版本（便于排查"为什么没上 4.6"）。</summary>
    public IReadOnlyList<string> Attempts { get; }

    /// <summary>当前是否跑在系统自带实现上。</summary>
    public bool IsSystemImplementation => Implementation.Source == GlImplementationSource.System;

    public string Describe() =>
        $"{Capabilities.Version} core={Capabilities.IsCoreProfile} | {Capabilities.Renderer} | GLSL {Capabilities.GlslVersion}"
        + $" | 实现={Implementation.Source} | 驱动={DriverName}";

    public void Dispose() => Context.Dispose();
}

/// <summary>
/// Byta 的初始化入口：挑实现 → 从最高版本往下试 → 载入口点 → 查能力。
/// <para>
/// 关键点：<b>不会</b>因为系统自带的实现只支持 4.1 就把自己限制在 4.1。
/// 只要随库分发的实现（或 <c>BYTA_GL_DRIVER</c> 指定的实现）能给出更高版本，就用更高的。
/// </para>
/// </summary>
public static class BytaRuntime
{
    /// <summary>桌面 GL 存在的版本，从高到低。</summary>
    private static readonly GlVersion[] s_knownVersions =
    [
        new(4, 6), new(4, 5), new(4, 4), new(4, 3), new(4, 2), new(4, 1), new(4, 0), new(3, 3),
    ];

    /// <summary>自动挑实现并初始化。</summary>
    public static BytaSession Initialize(BytaOptions? options = null)
    {
        options ??= new BytaOptions();

        var implementations = GlImplementationLocator.FindAll().ToList();
        if (!options.PreferBundledImplementation)
        {
            implementations = implementations
                .OrderBy(i => i.Source == GlImplementationSource.System ? 0 : 1)
                .ToList();
        }

        if (implementations.Count == 0)
            throw new GlException("找不到任何可用的 OpenGL 实现（既没有自带实现，也没有系统实现）。");

        var attempts = new List<string>();

        foreach (var implementation in implementations)
        {
            foreach (var version in GetCandidates(options, implementation.MaxVersion))
            {
                IGlContext? context = null;
                try
                {
                    context = implementation.CreateContext(version.Major, version.Minor);
                    context.MakeCurrent();

                    GL.Load(implementation.Resolver);
                    var capabilities = GlCapabilities.Query();

                    string driverName = context switch
                    {
                        // 以实际渲染器字符串为准：Mesa 在指定驱动不可用时会静默回退，
                        // 只看环境变量会报出根本没生效的驱动名。
                        EglContext => capabilities.Renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)
                            ? "llvmpipe (CPU)"
                            : capabilities.Renderer.Contains("zink", StringComparison.OrdinalIgnoreCase)
                                ? "zink (Vulkan)"
                                : capabilities.Renderer,
                        MacCglContext => "CGL",
                        _ => "(宿主上下文)",
                    };

                    attempts.Add($"✔ {implementation.Name} → {version}（实际 {capabilities.Version}，驱动 {driverName}）");
                    options.Log?.Invoke(attempts[^1]);

                    return new BytaSession(implementation, context, capabilities, version, driverName, attempts);
                }
                catch (Exception ex) when (ex is GlException or GlNotSupportedException or DllNotFoundException or EntryPointNotFoundException)
                {
                    context?.Dispose();
                    attempts.Add($"✘ {implementation.Name} → {version}：{FirstLine(ex.Message)}");
                    options.Log?.Invoke(attempts[^1]);
                }
            }
        }

        throw new GlException("所有实现都没能建出上下文：\n  " + string.Join("\n  ", attempts));
    }

    /// <summary>用调用方自己建的上下文（GLFW / SDL / Avalonia / 已有 GL 环境）初始化。</summary>
    public static BytaSession Initialize(IGlContext context, GlProcResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        resolver ??= GlProcResolvers.ForCurrentProcess();
        context.MakeCurrent();
        GL.Load(resolver);

        var capabilities = GlCapabilities.Query();
        var implementation = GlImplementation.ForHostContext(context.GetType().Name, resolver);
        return new BytaSession(implementation, context, capabilities, capabilities.Version, "(宿主上下文)", []);
    }

    /// <summary>列出本机所有可用实现（不建上下文，纯探测）。</summary>
    public static IReadOnlyList<GlImplementation> ProbeImplementations() => GlImplementationLocator.FindAll();

    private static IEnumerable<GlVersion> GetCandidates(BytaOptions options, GlVersion implementationMax)
    {
        var upper = options.MaxVersion <= implementationMax ? options.MaxVersion : implementationMax;
        foreach (var version in s_knownVersions)
        {
            if (version <= upper && version >= options.MinVersion)
                yield return version;
        }
    }

    private static string FirstLine(string text)
    {
        int index = text.IndexOf('\n');
        return index < 0 ? text : text[..index];
    }
}
