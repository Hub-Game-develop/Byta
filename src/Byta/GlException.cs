// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta;

/// <summary>GL 调用失败（<c>glGetError</c> 非 0）时抛出。</summary>
public sealed class GlException : Exception
{
    public uint ErrorCode { get; }

    public GlException(string operation, uint errorCode)
        : base($"{operation} 失败：GL 错误 0x{errorCode:X4}（{GlNative.ErrorName(errorCode)}）")
    {
        ErrorCode = errorCode;
    }

    public GlException(string message) : base(message) { }
}

/// <summary>当前上下文/驱动不支持某个能力时抛出。</summary>
public sealed class GlNotSupportedException : Exception
{
    public GlNotSupportedException(string feature, string? detail = null)
        : base(detail is null ? $"当前 OpenGL 上下文不支持 {feature}。" : $"当前 OpenGL 上下文不支持 {feature}：{detail}")
    {
    }
}

internal static unsafe class GlNative
{
    public static string? GetString(uint name)
    {
        if (!GlApi.HasGetString)
            return null;
        var p = GlApi.GetString(name);
        return p is null ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)p);
    }

    public static string? GetStringi(uint name, uint index)
    {
        if (!GlApi.HasGetStringi)
            return null;
        var p = GlApi.GetStringi(name, index);
        return p is null ? null : System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)p);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetInt(uint pname)
    {
        int value = 0;
        if (GlApi.HasGetIntegerv)
            GlApi.GetIntegerv(pname, &value);
        return value;
    }

    public static uint GetError()
    {
        if (!GlApi.HasGetError)
            return 0;
        return GlApi.GetError();
    }

    public static string ErrorName(uint error) => error switch
    {
        0x0000 => "NO_ERROR",
        0x0500 => "INVALID_ENUM",
        0x0501 => "INVALID_VALUE",
        0x0502 => "INVALID_OPERATION",
        0x0503 => "STACK_OVERFLOW",
        0x0504 => "STACK_UNDERFLOW",
        0x0505 => "OUT_OF_MEMORY",
        0x0506 => "INVALID_FRAMEBUFFER_OPERATION",
        0x0507 => "CONTEXT_LOST",
        _ => "UNKNOWN_ERROR",
    };

    public static string SeverityName(uint severity) => severity switch
    {
        0x826B => "high",
        0x826C => "medium",
        0x826D => "low",
        0x826E => "info",
        _ => "?",
    };
}
