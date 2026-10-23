// SPDX-License-Identifier: MIT
using System.Numerics;
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// 链接好的着色器程序，并缓存 uniform 位置查询结果
/// （<c>glGetUniformLocation</c> 每次都是驱动里的字符串查找，不能放进热路径）。
/// </summary>
public sealed unsafe class GlProgram : IDisposable
{
    private readonly Dictionary<string, int> _uniformLocations = new(StringComparer.Ordinal);

    private GlProgram(uint id) => Id = id;

    public uint Id { get; }

    public bool IsValid => Id != 0;

    public static GlProgram Link(params GlShader[] shaders)
    {
        if (shaders is null || shaders.Length == 0)
            throw new ArgumentException("至少需要一个已编译的着色器。", nameof(shaders));

        uint program = GL.CreateProgram();
        if (program == 0)
            throw new GlException("glCreateProgram 返回 0");

        foreach (var shader in shaders)
        {
            if (!shader.IsValid)
                throw new ArgumentException("包含未编译成功的着色器。", nameof(shaders));
            GL.AttachShader(program, shader.Id);
        }

        GL.LinkProgram(program);

        int status = 0;
        GL.GetProgramiv(program, GL.LINK_STATUS, &status);
        if (status == 0)
        {
            string log = GlShader.ReadInfoLog(program, isProgram: true);
            GL.DeleteProgram(program);
            throw new GlException($"着色器程序链接失败：\n{log}");
        }

        // 着色器对象已经可以释放（程序内部保留了自己的一份）
        foreach (var shader in shaders)
        {
            GL.DetachShader(program, shader.Id);
            shader.Dispose();
        }

        return new GlProgram(program);
    }

    /// <summary>从两段源码一步创建（编译 → 链接 → 释放着色器对象）。</summary>
    public static GlProgram FromSource(string vertexSource, string fragmentSource, int glslVersion = 410)
    {
        var vs = GlShader.Compile(GlShaderType.Vertex, vertexSource, glslVersion);
        var fs = GlShader.Compile(GlShaderType.Fragment, fragmentSource, glslVersion);
        return Link(vs, fs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Use() => GlState.UseProgram(Id);

    public static void Unbind() => GlState.UseProgram(0);

    /// <summary>取 uniform 位置（带缓存）。不存在时返回 -1。</summary>
    public int GetUniformLocation(string name)
    {
        if (_uniformLocations.TryGetValue(name, out int cached))
            return cached;

        int location = QueryUniformLocation(Id, name);
        _uniformLocations[name] = location;
        return location;
    }

    public bool TryGetUniform(string name, out GlUniform uniform)
    {
        int location = GetUniformLocation(name);
        uniform = new GlUniform((int)Id, location);
        return location >= 0;
    }

    public GlUniform GetUniform(string name)
    {
        int location = GetUniformLocation(name);
        if (location < 0)
            throw new ArgumentException($"程序里没有名为 '{name}' 的 uniform（可能被优化掉了）。", nameof(name));
        return new GlUniform((int)Id, location);
    }

    public int GetAttributeLocation(string name)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return GL.GetAttribLocation(Id, p);
    }

    public int GetUniformBlockIndex(string name)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return (int)GL.GetUniformBlockIndex(Id, p);
    }

    /// <summary>把 uniform block 绑到某个 binding point（之后 UBO 也绑到同一个点）。</summary>
    public void BindUniformBlock(string blockName, uint bindingPoint)
    {
        int index = GetUniformBlockIndex(blockName);
        if (index < 0)
            throw new ArgumentException($"程序里没有名为 '{blockName}' 的 uniform block。", nameof(blockName));
        GL.UniformBlockBinding(Id, (uint)index, bindingPoint);
    }

    public string GetInfoLog() => GlShader.ReadInfoLog(Id, isProgram: true);

    public void Dispose()
    {
        if (Id == 0)
            return;
        GlState.UseProgram(0);
        uint id = Id;
        GL.DeleteProgram(id);
    }

    private static int QueryUniformLocation(uint program, string name)
    {
        // 只在缓存未命中时走这里，允许一次短生命周期的分配
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return GL.GetUniformLocation(program, p);
    }
}
