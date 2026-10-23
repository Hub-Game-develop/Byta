using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// 顶点数组对象：把"哪个缓冲喂哪个 attribute"这套配置存进显存，
/// 绘制时一次 <see cref="Bind"/> 就恢复全部顶点状态。
/// </summary>
public readonly unsafe struct GlVertexArray : IDisposable
{
    public readonly uint Id;

    private GlVertexArray(uint id) => Id = id;

    public bool IsValid => Id != 0;

    public static GlVertexArray Create()
    {
        if (GL.GenVertexArrays is null)
            throw new GlNotSupportedException("glGenVertexArrays", "需要 OpenGL 3.0+ 或 ARB_vertex_array_object");

        uint id;
        GL.GenVertexArrays(1, &id);
        if (id == 0)
            throw new GlException("glGenVertexArrays 返回了 0 号对象");
        return new GlVertexArray(id);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Bind() => GlState.BindVertexArray(Id);

    /// <summary>
    /// 配置一个顶点属性。调用前需要：本 VAO 已绑定 + 对应的 <see cref="GlBuffer"/> 已绑定。
    /// </summary>
    public static void Attribute(
        int location, int components, VertexAttribType type,
        bool normalized = false, int strideBytes = 0, int offsetBytes = 0)
    {
        GL.EnableVertexAttribArray((uint)location);
        GL.VertexAttribPointer(
            (uint)location, components, (uint)type,
            normalized ? (byte)1 : (byte)0,
            strideBytes, (void*)(nint)offsetBytes);
    }

    /// <summary>整数属性（<c>glVertexAttribIPointer</c>），常用于骨骼索引之类。</summary>
    public static void IntegerAttribute(int location, int components, VertexAttribType type, int strideBytes = 0, int offsetBytes = 0)
    {
        GL.EnableVertexAttribArray((uint)location);
        GL.VertexAttribIPointer((uint)location, components, (uint)type, strideBytes, (void*)(nint)offsetBytes);
    }

    /// <summary>实例化绘制的步进除数（0 = 每顶点前进，1 = 每实例前进）。</summary>
    public static void Divisor(int location, uint divisor) => GL.VertexAttribDivisor((uint)location, divisor);

    public static void DisableAttribute(int location) => GL.DisableVertexAttribArray((uint)location);

    /// <summary>
    /// 一步配置一个"交错顶点布局"。
    /// <code>
    /// vao.SetupInterleaved(buffer, [
    ///     new VertexAttributeDescriptor(0, 3, VertexAttribType.Float, strideBytes: 24, offsetBytes: 0),
    ///     new VertexAttributeDescriptor(1, 3, VertexAttribType.Float, strideBytes: 24, offsetBytes: 12),
    /// ]);
    /// </code>
    /// </summary>
    public void SetupInterleaved(GlBuffer buffer, ReadOnlySpan<VertexAttributeDescriptor> attributes)
    {
        Bind();
        buffer.Bind();
        for (int i = 0; i < attributes.Length; i++)
        {
            var a = attributes[i];
            Attribute(a.Location, a.Components, a.Type, a.Normalized, a.StrideBytes, a.OffsetBytes);
        }
    }

    /// <summary>每个属性来自独立缓冲（非交错）。</summary>
    public void SetupSeparate(int location, GlBuffer buffer, int components, VertexAttribType type, bool normalized = false, int offsetBytes = 0)
    {
        Bind();
        buffer.Bind();
        Attribute(location, components, type, normalized, 0, offsetBytes);
    }

    public void Dispose()
    {
        if (Id == 0)
            return;
        GlState.BindVertexArray(0);
        uint id = Id;
        GL.DeleteVertexArrays(1, &id);
    }
}

/// <summary>一个顶点属性的描述（交错布局用）。</summary>
public readonly struct VertexAttributeDescriptor(
    int location, int components, VertexAttribType type,
    bool normalized = false, int strideBytes = 0, int offsetBytes = 0)
{
    public readonly int Location = location;
    public readonly int Components = components;
    public readonly VertexAttribType Type = type;
    public readonly bool Normalized = normalized;
    public readonly int StrideBytes = strideBytes;
    public readonly int OffsetBytes = offsetBytes;
}
