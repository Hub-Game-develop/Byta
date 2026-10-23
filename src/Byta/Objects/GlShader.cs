using System.Text;
using Byta.Bindings;

namespace Byta;

/// <summary>单个着色器对象。</summary>
public readonly unsafe struct GlShader : IDisposable
{
    public readonly uint Id;
    public readonly GlShaderType Type;

    private GlShader(uint id, GlShaderType type)
    {
        Id = id;
        Type = type;
    }

    public bool IsValid => Id != 0;

    /// <summary>
    /// 编译一个着色器。源码里没有 <c>#version</c> 时自动补上（默认 <c>#version 410</c>，
    /// 因为 macOS 的 GL 4.1 上限对应 GLSL 4.10）。
    /// 编译失败会抛 <see cref="GlException"/>，消息里带完整的驱动日志。
    /// </summary>
    public static GlShader Compile(GlShaderType type, string source, int glslVersion = 410)
    {
        if (GL.CreateShader is null)
            throw new GlNotSupportedException("glCreateShader", "需要 OpenGL 2.0+");

        uint id = GL.CreateShader((uint)type);
        if (id == 0)
            throw new GlException($"glCreateShader({type}) 返回 0");

        string finalSource = EnsureVersionDirective(source, glslVersion);
        byte[] bytes = Encoding.UTF8.GetBytes(finalSource);

        fixed (byte* p = bytes)
        {
            byte* pointers = p;
            int length = bytes.Length;
            GL.ShaderSource(id, 1, &pointers, &length);
        }

        GL.CompileShader(id);

        int status = 0;
        GL.GetShaderiv(id, GL.COMPILE_STATUS, &status);
        if (status == 0)
        {
            string log = ReadInfoLog(id, isProgram: false);
            GL.DeleteShader(id);
            throw new GlException($"{type} 着色器编译失败：\n{log}");
        }

        return new GlShader(id, type);
    }

    public static bool TryCompile(GlShaderType type, string source, out GlShader shader, out string log, int glslVersion = 410)
    {
        try
        {
            shader = Compile(type, source, glslVersion);
            log = string.Empty;
            return true;
        }
        catch (GlException ex)
        {
            shader = default;
            log = ex.Message;
            return false;
        }
    }

    public string GetInfoLog() => ReadInfoLog(Id, isProgram: false);

    public void Dispose()
    {
        if (Id == 0)
            return;
        uint id = Id;
        GL.DeleteShader(id);
    }

    internal static string ReadInfoLog(uint id, bool isProgram)
    {
        int length = 0;
        if (isProgram)
            GL.GetProgramiv(id, GL.INFO_LOG_LENGTH, &length);
        else
            GL.GetShaderiv(id, GL.INFO_LOG_LENGTH, &length);

        if (length <= 1)
            return string.Empty;

        byte[] buffer = new byte[length];
        int written = 0;
        fixed (byte* p = buffer)
        {
            if (isProgram)
                GL.GetProgramInfoLog(id, length, &written, p);
            else
                GL.GetShaderInfoLog(id, length, &written, p);
        }

        int count = written > 0 ? Math.Min(written, length - 1) : length - 1;
        return Encoding.UTF8.GetString(buffer, 0, count).TrimEnd('\0', '\n', '\r');
    }

    private static string EnsureVersionDirective(string source, int glslVersion)
    {
        var span = source.AsSpan().TrimStart();
        if (span.StartsWith("#version", StringComparison.Ordinal))
            return source;
        return $"#version {glslVersion}\n{source}";
    }
}
