using System.Numerics;
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// 一个 uniform 的位置句柄。
/// <para>
/// 用 <c>glProgramUniform*</c>（DSA）直接改任意程序的状态，所以设置 uniform 之前
/// <b>不需要</b>先 <see cref="GlProgram.Use"/> —— 少了多少次绑定切换。
/// </para>
/// </summary>
public readonly unsafe struct GlUniform
{
    public readonly int Program;
    public readonly int Location;

    internal GlUniform(int program, int location)
    {
        Program = program;
        Location = location;
    }

    public bool IsValid => Location >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float value) => GL.ProgramUniform1f((uint)Program, Location, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float x, float y) => GL.ProgramUniform2f((uint)Program, Location, x, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float x, float y, float z) => GL.ProgramUniform3f((uint)Program, Location, x, y, z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(float x, float y, float z, float w) => GL.ProgramUniform4f((uint)Program, Location, x, y, z, w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Vector2 value) => GL.ProgramUniform2f((uint)Program, Location, value.X, value.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Vector3 value) => GL.ProgramUniform3f((uint)Program, Location, value.X, value.Y, value.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(Vector4 value) => GL.ProgramUniform4f((uint)Program, Location, value.X, value.Y, value.Z, value.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(int value) => GL.ProgramUniform1i((uint)Program, Location, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set(uint value) => GL.ProgramUniform1ui((uint)Program, Location, value);

    /// <summary>上传 4x4 矩阵。GL 期望列主序；<see cref="Matrix4x4"/> 是行主序，通常需要 <paramref name="transpose"/> = true。</summary>
    public void Set(in Matrix4x4 value, bool transpose = false)
    {
        Matrix4x4 copy = value; // 拷到栈上再取地址，不需要 pin
        GL.ProgramUniformMatrix4fv((uint)Program, Location, 1, transpose ? (byte)1 : (byte)0, (float*)&copy);
    }

    public void Set(in Matrix3x2 value, bool transpose = false)
    {
        Matrix3x2 copy = value;
        GL.ProgramUniformMatrix3x2fv((uint)Program, Location, 1, transpose ? (byte)1 : (byte)0, (float*)&copy);
    }

    /// <summary>float 数组（<c>uniform float[N]</c> 或 vec 数组）。</summary>
    public void Set(ReadOnlySpan<float> values)
    {
        fixed (float* p = values)
            GL.ProgramUniform1fv((uint)Program, Location, values.Length, p);
    }

    public void Set(ReadOnlySpan<Vector2> values)
    {
        fixed (Vector2* p = values)
            GL.ProgramUniform2fv((uint)Program, Location, values.Length, (float*)p);
    }

    public void Set(ReadOnlySpan<Vector3> values)
    {
        fixed (Vector3* p = values)
            GL.ProgramUniform3fv((uint)Program, Location, values.Length, (float*)p);
    }

    public void Set(ReadOnlySpan<Vector4> values)
    {
        fixed (Vector4* p = values)
            GL.ProgramUniform4fv((uint)Program, Location, values.Length, (float*)p);
    }

    public void Set(ReadOnlySpan<Matrix4x4> values, bool transpose = false)
    {
        fixed (Matrix4x4* p = values)
            GL.ProgramUniformMatrix4fv((uint)Program, Location, values.Length, transpose ? (byte)1 : (byte)0, (float*)p);
    }
}
