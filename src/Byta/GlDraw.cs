// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>绘制与清除。全部薄封装，除了指针转换没有任何额外工作。</summary>
public static unsafe class GlDraw
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Arrays(PrimitiveType mode, int first, int count)
        => GL.DrawArrays((uint)mode, first, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Arrays(PrimitiveType mode, int count) => GL.DrawArrays((uint)mode, 0, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Elements(PrimitiveType mode, int count, IndexType type, int byteOffset = 0)
        => GL.DrawElements((uint)mode, count, (uint)type, (void*)(nint)byteOffset);

    /// <summary>带 baseVertex 的索引绘制（多网格合批进一个 IBO 时用）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ElementsBaseVertex(PrimitiveType mode, int count, IndexType type, int byteOffset, int baseVertex)
        => GL.DrawElementsBaseVertex((uint)mode, count, (uint)type, (void*)(nint)byteOffset, baseVertex);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ArraysInstanced(PrimitiveType mode, int first, int count, int instanceCount)
        => GL.DrawArraysInstanced((uint)mode, first, count, instanceCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ElementsInstanced(PrimitiveType mode, int count, IndexType type, int byteOffset, int instanceCount)
        => GL.DrawElementsInstanced((uint)mode, count, (uint)type, (void*)(nint)byteOffset, instanceCount);

    /// <summary>间接绘制：参数放在 GPU 缓冲里，CPU 完全不参与（适合大量批次）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ElementsIndirect(PrimitiveType mode, IndexType type, int byteOffset = 0)
        => GL.DrawElementsIndirect((uint)mode, (uint)type, (void*)(nint)byteOffset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MultiDrawElementsIndirect(PrimitiveType mode, IndexType type, int drawCount, int byteOffset = 0, int stride = 0)
        => GL.MultiDrawElementsIndirect((uint)mode, (uint)type, (void*)(nint)byteOffset, drawCount, stride);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Clear(ClearMask mask) => GL.Clear((uint)mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearColor(float r, float g, float b, float a) => GL.ClearColor(r, g, b, a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClearDepth(double depth) => GL.ClearDepth(depth);

    public static void Flush() => GL.Flush();

    public static void Finish() => GL.Finish();
}
