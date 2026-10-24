// SPDX-License-Identifier: MIT
using System;
using System.Runtime.InteropServices.JavaScript;

namespace Byta.Browser;

/// <summary>
/// 到 <c>byta-gl.js</c> 的原始互操作层。
/// <para>
/// 签名故意与 GL 不完全一致，因为 JS 互操作有几条硬限制：
/// <c>uint</c> 不支持（枚举/句柄统一收成 <c>int</c>）；
/// <c>Span&lt;uint&gt;</c> 不支持（内存一律走 <c>Span&lt;byte&gt;</c>，JS 侧按目标类型重解释）；
/// 裸指针不支持（在 <see cref="WebGlBackend"/> 里转成 <c>Span&lt;byte&gt;</c>）。
/// </para>
/// 这层只做搬运，不含逻辑；真正翻译成 WebGL 调用的是 JS。
/// </summary>
internal static partial class WebGl
{
    private const string Module = "byta-gl.v9.js";   // 文件名带版本：改 JS 时同步递增，绕开缓存

    // ---------------------------------------------------------------- 上下文
    [JSImport("attach", Module)] internal static partial int Attach(string canvasId);
    [JSImport("info", Module)] internal static partial string Info();
    [JSImport("setupViewport", Module)] internal static partial void SetupViewport(int width, int height, double pixelRatio);

    [JSImport("selfTest", Module)]
    internal static partial string SelfTest([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    // ---------------------------------------------------------------- 状态
    [JSImport("enable", Module)] internal static partial void Enable(int capability);
    [JSImport("disable", Module)] internal static partial void Disable(int capability);
    [JSImport("isEnabled", Module)] internal static partial int IsEnabled(int capability);
    [JSImport("depthFunc", Module)] internal static partial void DepthFunc(int func);
    [JSImport("depthMask", Module)] internal static partial void DepthMask(int flag);
    [JSImport("cullFace", Module)] internal static partial void CullFace(int mode);
    [JSImport("frontFace", Module)] internal static partial void FrontFace(int mode);
    [JSImport("blendFunc", Module)] internal static partial void BlendFunc(int source, int destination);
    [JSImport("blendFuncSeparate", Module)] internal static partial void BlendFuncSeparate(int srcRgb, int dstRgb, int srcAlpha, int dstAlpha);
    [JSImport("colorMask", Module)] internal static partial void ColorMask(int r, int g, int b, int a);
    [JSImport("viewport", Module)] internal static partial void Viewport(int x, int y, int width, int height);
    [JSImport("scissor", Module)] internal static partial void Scissor(int x, int y, int width, int height);
    [JSImport("clear", Module)] internal static partial void Clear(int mask);
    [JSImport("clearColor", Module)] internal static partial void ClearColor(double r, double g, double b, double a);
    [JSImport("clearDepth", Module)] internal static partial void ClearDepth(double depth);
    [JSImport("clearStencil", Module)] internal static partial void ClearStencil(int value);
    [JSImport("pixelStorei", Module)] internal static partial void PixelStorei(int pname, int value);
    [JSImport("lineWidth", Module)] internal static partial void LineWidth(double width);
    [JSImport("polygonOffset", Module)] internal static partial void PolygonOffset(double factor, double units);

    // ---------------------------------------------------------------- 缓冲
    [JSImport("genBuffers", Module)] internal static partial void GenBuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("deleteBuffers", Module)] internal static partial void DeleteBuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("bindBuffer", Module)] internal static partial void BindBuffer(int target, int buffer);
    [JSImport("bufferData", Module)] internal static partial void BufferData(int target, int size,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> data, int usage);
    [JSImport("bufferSubData", Module)] internal static partial void BufferSubData(int target, int offset, int size,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    // ---------------------------------------------------------------- VAO / 顶点属性
    [JSImport("genVertexArrays", Module)] internal static partial void GenVertexArrays([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("deleteVertexArrays", Module)] internal static partial void DeleteVertexArrays([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("bindVertexArray", Module)] internal static partial void BindVertexArray(int array);
    [JSImport("enableVertexAttribArray", Module)] internal static partial void EnableVertexAttribArray(int index);
    [JSImport("disableVertexAttribArray", Module)] internal static partial void DisableVertexAttribArray(int index);
    [JSImport("vertexAttribPointer", Module)] internal static partial void VertexAttribPointer(int index, int size, int type, int normalized, int stride, int offset);
    [JSImport("vertexAttribIPointer", Module)] internal static partial void VertexAttribIPointer(int index, int size, int type, int stride, int offset);
    [JSImport("vertexAttribDivisor", Module)] internal static partial void VertexAttribDivisor(int index, int divisor);

    // ---------------------------------------------------------------- 着色器
    [JSImport("createShader", Module)] internal static partial int CreateShader(int type);
    [JSImport("shaderSource", Module)] internal static partial void ShaderSource(int shader, string source);
    [JSImport("compileShader", Module)] internal static partial void CompileShader(int shader);
    [JSImport("getShaderiv", Module)] internal static partial void GetShaderiv(int shader, int pname, [JSMarshalAs<JSType.MemoryView>] Span<byte> output);
    [JSImport("getShaderInfoLog", Module)] internal static partial string GetShaderInfoLog(int shader);
    [JSImport("deleteShader", Module)] internal static partial void DeleteShader(int shader);

    // ---------------------------------------------------------------- 程序
    [JSImport("createProgram", Module)] internal static partial int CreateProgram();
    [JSImport("attachShader", Module)] internal static partial void AttachShader(int program, int shader);
    [JSImport("detachShader", Module)] internal static partial void DetachShader(int program, int shader);
    [JSImport("linkProgram", Module)] internal static partial void LinkProgram(int program);
    [JSImport("useProgram", Module)] internal static partial void UseProgram(int program);
    [JSImport("deleteProgram", Module)] internal static partial void DeleteProgram(int program);
    [JSImport("getProgramiv", Module)] internal static partial void GetProgramiv(int program, int pname, [JSMarshalAs<JSType.MemoryView>] Span<byte> output);
    [JSImport("getProgramInfoLog", Module)] internal static partial string GetProgramInfoLog(int program);
    [JSImport("getAttribLocation", Module)] internal static partial int GetAttribLocation(int program, string name);
    [JSImport("getUniformLocation", Module)] internal static partial int GetUniformLocation(int program, string name);
    [JSImport("getUniformMatrix4", Module)]
    internal static partial double[]? GetUniformMatrix4(int program, int location);

    [JSImport("uniformBlockBinding", Module)] internal static partial void UniformBlockBinding(int program, int blockIndex, int binding);

    // ---------------------------------------------------------------- uniform（非 DSA）
    [JSImport("uniform1f", Module)] internal static partial void Uniform1f(int location, double v0);
    [JSImport("uniform2f", Module)] internal static partial void Uniform2f(int location, double v0, double v1);
    [JSImport("uniform3f", Module)] internal static partial void Uniform3f(int location, double v0, double v1, double v2);
    [JSImport("uniform4f", Module)] internal static partial void Uniform4f(int location, double v0, double v1, double v2, double v3);
    [JSImport("uniform1i", Module)] internal static partial void Uniform1i(int location, int v0);
    [JSImport("uniform1ui", Module)] internal static partial void Uniform1ui(int location, int v0);
    [JSImport("uniform1fv", Module)] internal static partial void Uniform1fv(int location, [JSMarshalAs<JSType.MemoryView>] Span<byte> values);
    [JSImport("uniform2fv", Module)] internal static partial void Uniform2fv(int location, [JSMarshalAs<JSType.MemoryView>] Span<byte> values);
    [JSImport("uniform3fv", Module)] internal static partial void Uniform3fv(int location, [JSMarshalAs<JSType.MemoryView>] Span<byte> values);
    [JSImport("uniform4fv", Module)] internal static partial void Uniform4fv(int location, [JSMarshalAs<JSType.MemoryView>] Span<byte> values);
    [JSImport("uniformMatrix4fv", Module)] internal static partial void UniformMatrix4fv(int location, int transpose,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> values);
    [JSImport("uniformMatrix3x2fv", Module)] internal static partial void UniformMatrix3x2fv(int location, int transpose,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> values);

    // ---------------------------------------------------------------- 纹理
    [JSImport("genTextures", Module)] internal static partial void GenTextures([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("deleteTextures", Module)] internal static partial void DeleteTextures([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("bindTexture", Module)] internal static partial void BindTexture(int target, int texture);
    [JSImport("activeTexture", Module)] internal static partial void ActiveTexture(int unit);
    [JSImport("texImage2D", Module)] internal static partial void TexImage2D(int target, int level, int internalFormat, int width, int height,
        int border, int format, int type, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
    [JSImport("texSubImage2D", Module)] internal static partial void TexSubImage2D(int target, int level, int xOffset, int yOffset, int width, int height,
        int format, int type, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
    [JSImport("texParameteri", Module)] internal static partial void TexParameteri(int target, int pname, int value);
    [JSImport("generateMipmap", Module)] internal static partial void GenerateMipmap(int target);

    // ---------------------------------------------------------------- FBO / RBO
    [JSImport("genFramebuffers", Module)] internal static partial void GenFramebuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("deleteFramebuffers", Module)] internal static partial void DeleteFramebuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("bindFramebuffer", Module)] internal static partial void BindFramebuffer(int target, int framebuffer);
    [JSImport("framebufferTexture2D", Module)] internal static partial void FramebufferTexture2D(int target, int attachment, int textureTarget, int texture, int level);
    [JSImport("framebufferRenderbuffer", Module)] internal static partial void FramebufferRenderbuffer(int target, int attachment, int renderbufferTarget, int renderbuffer);
    [JSImport("checkFramebufferStatus", Module)] internal static partial int CheckFramebufferStatus(int target);
    [JSImport("drawBuffers", Module)] internal static partial void DrawBuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> buffers);
    [JSImport("readBuffer", Module)] internal static partial void ReadBuffer(int source);
    [JSImport("genRenderbuffers", Module)] internal static partial void GenRenderbuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("deleteRenderbuffers", Module)] internal static partial void DeleteRenderbuffers([JSMarshalAs<JSType.MemoryView>] Span<byte> ids);
    [JSImport("bindRenderbuffer", Module)] internal static partial void BindRenderbuffer(int target, int renderbuffer);
    [JSImport("renderbufferStorage", Module)] internal static partial void RenderbufferStorage(int target, int internalFormat, int width, int height);
    [JSImport("readPixels", Module)] internal static partial void ReadPixels(int x, int y, int width, int height, int format, int type,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);

    // ---------------------------------------------------------------- 绘制
    [JSImport("drawArrays", Module)] internal static partial void DrawArrays(int mode, int first, int count);
    [JSImport("drawElements", Module)] internal static partial void DrawElements(int mode, int count, int type, int offset);
    [JSImport("drawArraysInstanced", Module)] internal static partial void DrawArraysInstanced(int mode, int first, int count, int instanceCount);
    [JSImport("drawElementsInstanced", Module)] internal static partial void DrawElementsInstanced(int mode, int count, int type, int offset, int instanceCount);

    // ---------------------------------------------------------------- 杂项
    [JSImport("finish", Module)] internal static partial void Finish();
    [JSImport("flush", Module)] internal static partial void Flush();
    [JSImport("getError", Module)] internal static partial int GetError();
    [JSImport("getString", Module)] internal static partial string GetString(int name);
    [JSImport("getStringi", Module)] internal static partial string GetStringi(int name, int index);
    [JSImport("getParameter", Module)] internal static partial void GetParameter(int pname, [JSMarshalAs<JSType.MemoryView>] Span<byte> output);
    [JSImport("getParameterFloat", Module)] internal static partial void GetParameterFloat(int pname, [JSMarshalAs<JSType.MemoryView>] Span<byte> output);
}
