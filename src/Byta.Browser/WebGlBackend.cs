// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Byta.Browser;

/// <summary>
/// 把 GL 调用转发给浏览器里的 WebGL2 上下文。
/// <para>
/// 它实现的是 <see cref="IGlBackend"/>，而 <see cref="GlApi"/> 的门面签名与 GL 完全一致
/// （裸指针），所以这里的职责只有两件事：把指针包成 <c>Span&lt;byte&gt;</c>、
/// 把 <c>uint</c> 收成 <c>int</c>（JS 互操作不支持无符号整数）。
/// 真正的 WebGL 调用在 <c>byta-gl.js</c> 里。
/// </para>
/// <para>
/// 覆盖范围是 GLES 3.0 核心，也就是 WebGL2 能给的全部。桌面 GL 专有的命令
/// （DSA 的 <c>glProgramUniform*</c> 由门面自动退化、<c>glBufferStorage</c>、
/// <c>glMapBuffer</c>、<c>glDebugMessageCallback</c> 等）会抛
/// <see cref="GlNotSupportedException"/>，并在 <see cref="Supports"/> 里如实报告。
/// </para>
/// </summary>
public sealed unsafe class WebGlBackend : WebGlBackendBase
{
    private static readonly HashSet<string> s_supported = new(StringComparer.Ordinal)
    {
        // 状态
        "glEnable", "glDisable", "glIsEnabled", "glDepthFunc", "glDepthMask",
        "glCullFace", "glFrontFace", "glBlendFunc", "glBlendFuncSeparate", "glColorMask",
        "glViewport", "glScissor", "glClear", "glClearColor", "glClearDepth", "glClearStencil",
        "glPixelStorei", "glLineWidth", "glPolygonOffset",
        // 缓冲
        "glGenBuffers", "glDeleteBuffers", "glBindBuffer", "glBufferData", "glBufferSubData",
        // VAO
        "glGenVertexArrays", "glDeleteVertexArrays", "glBindVertexArray",
        "glEnableVertexAttribArray", "glDisableVertexAttribArray",
        "glVertexAttribPointer", "glVertexAttribIPointer", "glVertexAttribDivisor",
        // 着色器
        "glCreateShader", "glShaderSource", "glCompileShader", "glGetShaderiv",
        "glGetShaderInfoLog", "glDeleteShader",
        // 程序
        "glCreateProgram", "glAttachShader", "glDetachShader", "glLinkProgram", "glUseProgram",
        "glDeleteProgram", "glGetProgramiv", "glGetProgramInfoLog",
        "glGetAttribLocation", "glGetUniformLocation", "glUniformBlockBinding",
        // uniform
        "glUniform1f", "glUniform2f", "glUniform3f", "glUniform4f",
        "glUniform1i", "glUniform1ui",
        "glUniform1fv", "glUniform2fv", "glUniform3fv", "glUniform4fv",
        "glUniformMatrix4fv", "glUniformMatrix3x2fv",
        // 纹理
        "glGenTextures", "glDeleteTextures", "glBindTexture", "glActiveTexture",
        "glTexImage2D", "glTexSubImage2D", "glTexParameteri", "glGenerateMipmap",
        // FBO / RBO
        "glGenFramebuffers", "glDeleteFramebuffers", "glBindFramebuffer",
        "glFramebufferTexture2D", "glFramebufferRenderbuffer", "glCheckFramebufferStatus",
        "glDrawBuffers", "glReadBuffer",
        "glGenRenderbuffers", "glDeleteRenderbuffers", "glBindRenderbuffer",
        "glRenderbufferStorage", "glReadPixels",
        // 绘制
        "glDrawArrays", "glDrawElements", "glDrawArraysInstanced", "glDrawElementsInstanced",
        // 杂项
        "glFinish", "glFlush", "glGetError", "glGetString", "glGetStringi",
        "glGetIntegerv", "glGetFloatv",
    };

    private readonly Dictionary<int, nint> _cachedStrings = [];

    public override string Name => $"WebGL2 ({WebGl.Info()})";

    public override bool Supports(string command) => s_supported.Contains(command);

    // ---------------------------------------------------------------- 状态

    public override void Enable(uint cap) => WebGl.Enable((int)cap);
    public override void Disable(uint cap) => WebGl.Disable((int)cap);
    public override byte IsEnabled(uint cap) => (byte)WebGl.IsEnabled((int)cap);
    public override void DepthFunc(uint func) => WebGl.DepthFunc((int)func);
    public override void DepthMask(byte flag) => WebGl.DepthMask(flag);
    public override void CullFace(uint mode) => WebGl.CullFace((int)mode);
    public override void FrontFace(uint mode) => WebGl.FrontFace((int)mode);
    public override void BlendFunc(uint sfactor, uint dfactor) => WebGl.BlendFunc((int)sfactor, (int)dfactor);

    public override void BlendFuncSeparate(uint sfactorRGB, uint dfactorRGB, uint sfactorAlpha, uint dfactorAlpha)
        => WebGl.BlendFuncSeparate((int)sfactorRGB, (int)dfactorRGB, (int)sfactorAlpha, (int)dfactorAlpha);

    public override void ColorMask(byte red, byte green, byte blue, byte alpha)
        => WebGl.ColorMask(red, green, blue, alpha);

    public override void Viewport(int x, int y, int width, int height) => WebGl.Viewport(x, y, width, height);
    public override void Scissor(int x, int y, int width, int height) => WebGl.Scissor(x, y, width, height);
    public override void Clear(uint mask) => WebGl.Clear((int)mask);
    public override void ClearColor(float red, float green, float blue, float alpha)
        => WebGl.ClearColor(red, green, blue, alpha);
    public override void ClearDepth(double depth) => WebGl.ClearDepth(depth);
    public override void ClearStencil(int s) => WebGl.ClearStencil(s);
    public override void PixelStorei(uint pname, int param) => WebGl.PixelStorei((int)pname, param);
    public override void LineWidth(float width) => WebGl.LineWidth(width);
    public override void PolygonOffset(float factor, float units) => WebGl.PolygonOffset(factor, units);

    // ---------------------------------------------------------------- 缓冲

    public override void GenBuffers(int n, uint* buffers)
        => WebGl.GenBuffers(new Span<byte>(buffers, n * sizeof(uint)));

    public override void DeleteBuffers(int n, uint* buffers)
        => WebGl.DeleteBuffers(new Span<byte>(buffers, n * sizeof(uint)));

    public override void BindBuffer(uint target, uint buffer) => WebGl.BindBuffer((int)target, (int)buffer);

    public override void BufferData(uint target, nint size, void* data, uint usage)
    {
        Span<byte> span = data is null ? Span<byte>.Empty : new Span<byte>(data, checked((int)size));
        WebGl.BufferData((int)target, (int)size, span, (int)usage);
    }

    public override void BufferSubData(uint target, nint offset, nint size, void* data)
        => WebGl.BufferSubData((int)target, (int)offset, (int)size, new Span<byte>(data, checked((int)size)));

    // ---------------------------------------------------------------- VAO

    public override void GenVertexArrays(int n, uint* arrays)
        => WebGl.GenVertexArrays(new Span<byte>(arrays, n * sizeof(uint)));

    public override void DeleteVertexArrays(int n, uint* arrays)
        => WebGl.DeleteVertexArrays(new Span<byte>(arrays, n * sizeof(uint)));

    public override void BindVertexArray(uint array) => WebGl.BindVertexArray((int)array);
    public override void EnableVertexAttribArray(uint index) => WebGl.EnableVertexAttribArray((int)index);
    public override void DisableVertexAttribArray(uint index) => WebGl.DisableVertexAttribArray((int)index);

    public override void VertexAttribPointer(uint index, int size, uint type, byte normalized, int stride, void* pointer)
        => WebGl.VertexAttribPointer((int)index, size, (int)type, normalized, stride, (int)(nint)pointer);

    public override void VertexAttribIPointer(uint index, int size, uint type, int stride, void* pointer)
        => WebGl.VertexAttribIPointer((int)index, size, (int)type, stride, (int)(nint)pointer);

    public override void VertexAttribDivisor(uint index, uint divisor)
        => WebGl.VertexAttribDivisor((int)index, (int)divisor);

    // ---------------------------------------------------------------- 着色器

    public override uint CreateShader(uint type) => (uint)WebGl.CreateShader((int)type);
    public override void CompileShader(uint shader) => WebGl.CompileShader((int)shader);
    public override void DeleteShader(uint shader) => WebGl.DeleteShader((int)shader);

    public override void ShaderSource(uint shader, int count, byte** @string, int* length)
    {
        // 库只会传单个源码串（count == 1），所以这里不做多段拼接
        string source = length is null
            ? ReadUtf8(@string[0])
            : ReadUtf8(@string[0], length[0]);
        WebGl.ShaderSource((int)shader, source);
    }

    public override void GetShaderiv(uint shader, uint pname, int* @params)
        => WebGl.GetShaderiv((int)shader, (int)pname, new Span<byte>(@params, sizeof(int)));

    public override void GetShaderInfoLog(uint shader, int bufSize, int* length, byte* infoLog)
        => WriteUtf8(WebGl.GetShaderInfoLog((int)shader), bufSize, length, infoLog);

    // ---------------------------------------------------------------- 程序

    public override uint CreateProgram() => (uint)WebGl.CreateProgram();
    public override void AttachShader(uint program, uint shader) => WebGl.AttachShader((int)program, (int)shader);
    public override void DetachShader(uint program, uint shader) => WebGl.DetachShader((int)program, (int)shader);
    public override void LinkProgram(uint program) => WebGl.LinkProgram((int)program);
    public override void UseProgram(uint program) => WebGl.UseProgram((int)program);
    public override void DeleteProgram(uint program) => WebGl.DeleteProgram((int)program);

    public override void GetProgramiv(uint program, uint pname, int* @params)
        => WebGl.GetProgramiv((int)program, (int)pname, new Span<byte>(@params, sizeof(int)));

    public override void GetProgramInfoLog(uint program, int bufSize, int* length, byte* infoLog)
        => WriteUtf8(WebGl.GetProgramInfoLog((int)program), bufSize, length, infoLog);

    public override int GetAttribLocation(uint program, byte* name)
        => WebGl.GetAttribLocation((int)program, ReadUtf8(name));

    public override int GetUniformLocation(uint program, byte* name)
        => WebGl.GetUniformLocation((int)program, ReadUtf8(name));

    public override void UniformBlockBinding(uint program, uint uniformBlockIndex, uint uniformBlockBinding)
        => WebGl.UniformBlockBinding((int)program, (int)uniformBlockIndex, (int)uniformBlockBinding);

    // ---------------------------------------------------------------- uniform

    public override void Uniform1f(int location, float v0) => WebGl.Uniform1f(location, v0);
    public override void Uniform2f(int location, float v0, float v1) => WebGl.Uniform2f(location, v0, v1);
    public override void Uniform3f(int location, float v0, float v1, float v2) => WebGl.Uniform3f(location, v0, v1, v2);
    public override void Uniform4f(int location, float v0, float v1, float v2, float v3) => WebGl.Uniform4f(location, v0, v1, v2, v3);
    public override void Uniform1i(int location, int v0) => WebGl.Uniform1i(location, v0);
    public override void Uniform1ui(int location, uint v0) => WebGl.Uniform1ui(location, (int)v0);

    public override void Uniform1fv(int location, int count, float* value)
        => WebGl.Uniform1fv(location, new Span<byte>(value, count * sizeof(float)));

    public override void Uniform2fv(int location, int count, float* value)
        => WebGl.Uniform2fv(location, new Span<byte>(value, count * 2 * sizeof(float)));

    public override void Uniform3fv(int location, int count, float* value)
        => WebGl.Uniform3fv(location, new Span<byte>(value, count * 3 * sizeof(float)));

    public override void Uniform4fv(int location, int count, float* value)
        => WebGl.Uniform4fv(location, new Span<byte>(value, count * 4 * sizeof(float)));

    public override void UniformMatrix4fv(int location, int count, byte transpose, float* value)
        => WebGl.UniformMatrix4fv(location, transpose, new Span<byte>(value, count * 16 * sizeof(float)));

    public override void UniformMatrix3x2fv(int location, int count, byte transpose, float* value)
        => WebGl.UniformMatrix3x2fv(location, transpose, new Span<byte>(value, count * 6 * sizeof(float)));

    // ---------------------------------------------------------------- 纹理

    public override void GenTextures(int n, uint* textures)
        => WebGl.GenTextures(new Span<byte>(textures, n * sizeof(uint)));

    public override void DeleteTextures(int n, uint* textures)
        => WebGl.DeleteTextures(new Span<byte>(textures, n * sizeof(uint)));

    public override void BindTexture(uint target, uint texture) => WebGl.BindTexture((int)target, (int)texture);
    public override void ActiveTexture(uint texture) => WebGl.ActiveTexture((int)texture);
    public override void TexParameteri(uint target, uint pname, int param) => WebGl.TexParameteri((int)target, (int)pname, param);
    public override void GenerateMipmap(uint target) => WebGl.GenerateMipmap((int)target);

    public override void TexImage2D(uint target, int level, int internalformat, int width, int height,
        int border, uint format, uint type, void* pixels)
    {
        Span<byte> span = pixels is null ? Span<byte>.Empty : new Span<byte>(pixels, EstimateImageBytes(width, height, format, type));
        WebGl.TexImage2D((int)target, level, internalformat, width, height, border, (int)format, (int)type, span);
    }

    public override void TexSubImage2D(uint target, int level, int xoffset, int yoffset, int width, int height,
        uint format, uint type, void* pixels)
    {
        Span<byte> span = pixels is null ? Span<byte>.Empty : new Span<byte>(pixels, EstimateImageBytes(width, height, format, type));
        WebGl.TexSubImage2D((int)target, level, xoffset, yoffset, width, height, (int)format, (int)type, span);
    }

    // ---------------------------------------------------------------- FBO / RBO

    public override void GenFramebuffers(int n, uint* framebuffers)
        => WebGl.GenFramebuffers(new Span<byte>(framebuffers, n * sizeof(uint)));

    public override void DeleteFramebuffers(int n, uint* framebuffers)
        => WebGl.DeleteFramebuffers(new Span<byte>(framebuffers, n * sizeof(uint)));

    public override void BindFramebuffer(uint target, uint framebuffer)
        => WebGl.BindFramebuffer((int)target, (int)framebuffer);

    public override void FramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level)
        => WebGl.FramebufferTexture2D((int)target, (int)attachment, (int)textarget, (int)texture, level);

    public override void FramebufferRenderbuffer(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer)
        => WebGl.FramebufferRenderbuffer((int)target, (int)attachment, (int)renderbuffertarget, (int)renderbuffer);

    public override uint CheckFramebufferStatus(uint target) => (uint)WebGl.CheckFramebufferStatus((int)target);

    public override void DrawBuffers(int n, uint* bufs)
        => WebGl.DrawBuffers(new Span<byte>(bufs, n * sizeof(uint)));

    public override void ReadBuffer(uint src) => WebGl.ReadBuffer((int)src);

    public override void GenRenderbuffers(int n, uint* renderbuffers)
        => WebGl.GenRenderbuffers(new Span<byte>(renderbuffers, n * sizeof(uint)));

    public override void DeleteRenderbuffers(int n, uint* renderbuffers)
        => WebGl.DeleteRenderbuffers(new Span<byte>(renderbuffers, n * sizeof(uint)));

    public override void BindRenderbuffer(uint target, uint renderbuffer)
        => WebGl.BindRenderbuffer((int)target, (int)renderbuffer);

    public override void RenderbufferStorage(uint target, uint internalformat, int width, int height)
        => WebGl.RenderbufferStorage((int)target, (int)internalformat, width, height);

    public override void ReadPixels(int x, int y, int width, int height, uint format, uint type, void* pixels)
        => WebGl.ReadPixels(x, y, width, height, (int)format, (int)type,
            new Span<byte>(pixels, EstimateImageBytes(width, height, format, type)));

    // ---------------------------------------------------------------- 绘制

    public override void DrawArrays(uint mode, int first, int count)
        => WebGl.DrawArrays((int)mode, first, count);

    public override void DrawElements(uint mode, int count, uint type, void* indices)
        => WebGl.DrawElements((int)mode, count, (int)type, (int)(nint)indices);

    public override void DrawArraysInstanced(uint mode, int first, int count, int instancecount)
        => WebGl.DrawArraysInstanced((int)mode, first, count, instancecount);

    public override void DrawElementsInstanced(uint mode, int count, uint type, void* indices, int instancecount)
        => WebGl.DrawElementsInstanced((int)mode, count, (int)type, (int)(nint)indices, instancecount);

    // ---------------------------------------------------------------- 杂项

    public override void Finish() => WebGl.Finish();
    public override void Flush() => WebGl.Flush();
    public override uint GetError() => (uint)WebGl.GetError();

    public override byte* GetString(uint name) => CacheString((int)name, WebGl.GetString((int)name));

    public override byte* GetStringi(uint name, uint index)
        => CacheString((int)name * 100_000 + (int)index, WebGl.GetStringi((int)name, (int)index));

    public override void GetIntegerv(uint pname, int* data)
        => WebGl.GetParameter((int)pname, new Span<byte>(data, 16 * sizeof(int)));

    public override void GetFloatv(uint pname, float* data)
        => WebGl.GetParameterFloat((int)pname, new Span<byte>(data, 16 * sizeof(float)));

    // ---------------------------------------------------------------- 辅助

    private static int EstimateImageBytes(int width, int height, uint format, uint type)
    {
        // RGBA8 = 4，RGB8 = 3；type 只有 UNSIGNED_BYTE(0x1401) 是 1 字节。
        // 这里只在把像素传给 JS 时用来确定 Span 长度，宁大勿小。
        int channels = format switch
        {
            0x1908 or 0x1907 or 0x190A => 4, // RGBA / RGB / RGBA(时序) 都按 4 算
            0x1903 or 0x1909 => 4,           // RED / LUMINANCE
            _ => 4,
        };
        int elementSize = type switch
        {
            0x1401 => 1, // UNSIGNED_BYTE
            0x1403 => 2, // UNSIGNED_SHORT
            0x1406 => 4, // FLOAT
            0x140B => 2, // HALF_FLOAT
            _ => 1,
        };
        return Math.Max(0, width * height * channels * elementSize);
    }

    private byte* CacheString(int key, string value)
    {
        if (!_cachedStrings.TryGetValue(key, out nint pointer))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value + "\0");
            pointer = (nint)NativeMemory.Alloc((nuint)bytes.Length);
            bytes.AsSpan().CopyTo(new Span<byte>((void*)pointer, bytes.Length));
            _cachedStrings[key] = pointer;
        }
        return (byte*)pointer;
    }

    private static string ReadUtf8(byte* text)
    {
        if (text is null)
            return string.Empty;
        return Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text));
    }

    private static string ReadUtf8(byte* text, int length)
    {
        if (text is null || length <= 0)
            return string.Empty;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text, length));
    }

    private static void WriteUtf8(string text, int bufferSize, int* length, byte* target)
    {
        int written = 0;
        if (bufferSize > 0 && target is not null)
        {
            var destination = new Span<byte>(target, bufferSize - 1);
            written = Encoding.UTF8.GetBytes(text, destination);
            target[written] = 0;
        }
        if (length is not null)
            *length = written + 1;
    }
}
