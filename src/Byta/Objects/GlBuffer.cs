using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// GPU 缓冲对象（VBO / IBO / UBO / SSBO）。
/// <para>
/// 这是个 <c>readonly struct</c> 的"值句柄"，创建时不产生托管分配；
/// 传参、返回、放进数组都不会有额外开销。别复制之后重复 <see cref="Dispose"/>。
/// </para>
/// </summary>
public readonly unsafe struct GlBuffer : IDisposable
{
    public readonly uint Id;
    public readonly BufferTarget Target;

    private GlBuffer(uint id, BufferTarget target)
    {
        Id = id;
        Target = target;
    }

    public bool IsValid => Id != 0;

    public static GlBuffer Create(BufferTarget target = BufferTarget.ArrayBuffer)
    {
        if (GL.GenBuffers is null)
            throw new GlNotSupportedException("glGenBuffers", "需要 OpenGL 1.5+");

        uint id;
        GL.GenBuffers(1, &id);
        if (id == 0)
            throw new GlException("glGenBuffers 返回了 0 号对象");
        return new GlBuffer(id, target);
    }

    /// <summary>绑定到创建时指定的 target（走状态缓存）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Bind() => GlState.BindBuffer(Target, Id);

    /// <summary>只分配显存，不上传数据。</summary>
    public void Allocate(nuint sizeBytes, BufferUsage usage = BufferUsage.DynamicDraw)
    {
        Bind();
        GL.BufferData((uint)Target, (nint)sizeBytes, null, (uint)usage);
    }

    /// <summary>分配 + 一次上传（<c>fixed</c> 固定 Span，无拷贝、无分配）。</summary>
    public void Allocate<T>(ReadOnlySpan<T> data, BufferUsage usage = BufferUsage.StaticDraw) where T : unmanaged
    {
        Bind();
        fixed (T* p = data)
            GL.BufferData((uint)Target, (nint)((nuint)data.Length * (nuint)sizeof(T)), p, (uint)usage);
    }

    /// <summary>原地更新一段数据，<paramref name="elementOffset"/> 以元素为单位。</summary>
    public void Upload<T>(ReadOnlySpan<T> data, int elementOffset = 0) where T : unmanaged
    {
        Bind();
        fixed (T* p = data)
            GL.BufferSubData((uint)Target, (nint)elementOffset * sizeof(T), (nint)((nuint)data.Length * (nuint)sizeof(T)), p);
    }

    /// <summary>按字节偏移更新。</summary>
    public void Upload(nint source, nuint sizeBytes, nuint destinationOffsetBytes = 0)
    {
        Bind();
        GL.BufferSubData((uint)Target, (nint)destinationOffsetBytes, (nint)sizeBytes, (void*)source);
    }

    /// <summary>
    /// 孤儿化：丢掉旧存储、重新分配，避免驱动为了等 GPU 读完而阻塞（经典的 "buffer orphaning"）。
    /// 每帧重填同一个动态缓冲时用它比 <see cref="Upload{T}"/> 更不容易踩到同步等待。
    /// </summary>
    public void Orphan(nuint sizeBytes, BufferUsage usage = BufferUsage.StreamDraw)
    {
        Bind();
        GL.BufferData((uint)Target, (nint)sizeBytes, null, (uint)usage);
    }

    public void* Map(MapAccess access)
    {
        Bind();
        return GL.MapBuffer((uint)Target, (uint)access);
    }

    public void Unmap()
    {
        Bind();
        GL.UnmapBuffer((uint)Target);
    }

    /// <summary>把另一段区间拷进本缓冲（GPU 侧拷贝，不经过 CPU）。</summary>
    public void CopyFrom(GlBuffer source, nuint sizeBytes, nuint sourceOffsetBytes = 0, nuint destinationOffsetBytes = 0)
    {
        if (source.Id == Id)
            return;
        GL.BindBuffer(GL.COPY_READ_BUFFER, source.Id);
        GL.BindBuffer(GL.COPY_WRITE_BUFFER, Id);
        GL.CopyBufferSubData(GL.COPY_READ_BUFFER, GL.COPY_WRITE_BUFFER, (nint)sourceOffsetBytes, (nint)destinationOffsetBytes, (nint)sizeBytes);
        GlState.Invalidate();
    }

    public void Dispose()
    {
        if (Id == 0)
            return;

        // 解绑后删除：避免缓存在对象已销毁后仍指向它
        GlState.BindBuffer(Target, 0);
        uint id = Id;
        GL.DeleteBuffers(1, &id);
    }
}
