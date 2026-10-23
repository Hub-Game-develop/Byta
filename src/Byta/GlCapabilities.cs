using Byta.Bindings;

namespace Byta;

/// <summary>GL 版本号，例如 <c>4.1</c>。</summary>
public readonly struct GlVersion(int major, int minor) : IComparable<GlVersion>, IEquatable<GlVersion>
{
    public readonly int Major = major;
    public readonly int Minor = minor;

    public bool AtLeast(int major, int minor) => Major > major || (Major == major && Minor >= minor);
    public bool AtLeast(GlVersion other) => AtLeast(other.Major, other.Minor);

    /// <summary>解析 <c>glGetString(GL_VERSION)</c>，例如 "4.1 Metal - 91.7"。</summary>
    public static GlVersion Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return default;

        int end = text.IndexOf(' ');
        var token = end < 0 ? text : text[..end];
        var parts = token.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0;
        return new GlVersion(major, minor);
    }

    public int CompareTo(GlVersion other) => Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);
    public bool Equals(GlVersion other) => Major == other.Major && Minor == other.Minor;
    public override bool Equals(object? obj) => obj is GlVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor);
    public static bool operator >=(GlVersion a, GlVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(GlVersion a, GlVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >(GlVersion a, GlVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(GlVersion a, GlVersion b) => a.CompareTo(b) < 0;
    public static bool operator ==(GlVersion a, GlVersion b) => a.Equals(b);
    public static bool operator !=(GlVersion a, GlVersion b) => !a.Equals(b);
    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>
/// 当前上下文的静态能力信息。只在初始化阶段查询一次，之后全是内存读取。
/// </summary>
public sealed unsafe class GlCapabilities
{
    private readonly HashSet<string> _extensions;

    private GlCapabilities(
        GlVersion version, bool isCoreProfile,
        string vendor, string renderer, string versionString, string glslVersion,
        HashSet<string> extensions)
    {
        Version = version;
        IsCoreProfile = isCoreProfile;
        Vendor = vendor;
        Renderer = renderer;
        VersionString = versionString;
        GlslVersion = glslVersion;
        _extensions = extensions;
    }

    public GlVersion Version { get; }
    public bool IsCoreProfile { get; }
    public string Vendor { get; }
    public string Renderer { get; }
    public string VersionString { get; }
    public string GlslVersion { get; }

    /// <summary>当前上下文暴露的扩展（来自 <c>glGetStringi(GL_EXTENSIONS, i)</c>）。</summary>
    public IReadOnlyCollection<string> Extensions => _extensions;

    public bool Has(string extension) => _extensions.Contains(extension);

    public bool AtLeast(int major, int minor) => Version.AtLeast(major, minor);

    /// <summary>是否支持直接状态访问（DSA，4.5 或 ARB_direct_state_access）。</summary>
    public bool SupportsDirectStateAccess => AtLeast(4, 5) || Has("GL_ARB_direct_state_access");

    /// <summary>是否支持 <c>glBufferStorage</c> 持久映射（4.4 或 ARB_buffer_storage）。</summary>
    public bool SupportsBufferStorage => AtLeast(4, 4) || Has("GL_ARB_buffer_storage");

    public int MaxTextureSize => GlNative.GetInt(GL.MAX_TEXTURE_SIZE);
    public int MaxCubeMapSize => GlNative.GetInt(GL.MAX_CUBE_MAP_TEXTURE_SIZE);
    public int MaxRenderbufferSize => GlNative.GetInt(GL.MAX_RENDERBUFFER_SIZE);
    public int MaxVertexAttributes => GlNative.GetInt(GL.MAX_VERTEX_ATTRIBS);
    public int MaxTextureUnits => GlNative.GetInt(GL.MAX_COMBINED_TEXTURE_IMAGE_UNITS);
    public int MaxUniformBufferBindings => GlNative.GetInt(GL.MAX_UNIFORM_BUFFER_BINDINGS);

    /// <summary>读取当前上下文的版本、厂商、扩展列表。调用前必须已经 <see cref="GL.Load"/> 并且上下文已 current。</summary>
    public static GlCapabilities Query()
    {
        var versionString = GlNative.GetString(GL.VERSION) ?? string.Empty;
        var version = GlVersion.Parse(versionString);

        bool core;
        if (version.AtLeast(3, 2) && GL.GetIntegerv is not null)
        {
            int mask = GlNative.GetInt(GL.CONTEXT_PROFILE_MASK);
            core = (mask & (int)GL.CONTEXT_CORE_PROFILE_BIT) != 0;
        }
        else
        {
            core = version.AtLeast(3, 2);
        }

        var extensions = new HashSet<string>(StringComparer.Ordinal);
        if (GL.GetStringi is not null && version.AtLeast(3, 0))
        {
            int count = GlNative.GetInt(GL.NUM_EXTENSIONS);
            for (uint i = 0; i < (uint)count; i++)
            {
                var name = GlNative.GetStringi(GL.EXTENSIONS, i);
                if (!string.IsNullOrEmpty(name))
                    extensions.Add(name);
            }
        }

        return new GlCapabilities(
            version,
            core,
            GlNative.GetString(GL.VENDOR) ?? "?",
            GlNative.GetString(GL.RENDERER) ?? "?",
            versionString,
            GlNative.GetString(GL.SHADING_LANGUAGE_VERSION) ?? "?",
            extensions);
    }

    public override string ToString() =>
        $"GL {Version} core={IsCoreProfile} | {Renderer} | GLSL {GlslVersion} | {_extensions.Count} ext";
}
