// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Byta.Bindings;

namespace Byta;

/// <summary>
/// 错误检查与驱动调试消息（KHR_debug / ARB_debug_output）接入。
/// <para>
/// <see cref="ThrowOnError"/> 默认开启：每一次封装层调用之后都会检查一次 <c>glGetError</c>。
/// 发布版里把它关掉（或只在 Debug 构建里打开），就完全没有额外开销。
/// </para>
/// </summary>
public static unsafe class GlDebug
{
    private static Action<string>? s_handler;

    /// <summary>出错时直接抛 <see cref="GlException"/>；关掉则只返回错误码。</summary>
    public static bool ThrowOnError { get; set; } = true;

    /// <summary>同时把错误打到控制台。</summary>
    public static bool EchoToConsole { get; set; }

    /// <summary>驱动调试消息是否已开启。</summary>
    public static bool DriverMessagesEnabled { get; private set; }

    /// <summary>自定义消息接收器；默认写 <see cref="Console.Error"/>。</summary>
    public static Action<string>? MessageHandler
    {
        get => s_handler;
        set => s_handler = value;
    }

    /// <summary>检查上一次调用是否产生了 GL 错误。热路径上建议只在 Debug 里调用。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Check(string operation)
    {
        if (!ThrowOnError || GL.GetError is null)
            return;
        uint error = GL.GetError();
        if (error != 0)
            throw new GlException(operation, error);
    }

    /// <summary>把当前队列里所有错误取出来（最多 <paramref name="max"/> 个），不清空则一直取到 NO_ERROR。</summary>
    public static List<uint> Drain(int max = 16)
    {
        var list = new List<uint>(4);
        if (GL.GetError is null)
            return list;

        for (int i = 0; i < max; i++)
        {
            uint error = GL.GetError();
            if (error == 0)
                break;
            list.Add(error);
        }
        return list;
    }

    /// <summary>
    /// 开启驱动端的调试输出（KHR_debug）。不支持的上下文返回 false。
    /// 之后 GL 自己发现的问题（包括 API 误用、性能提示）会通过 <see cref="MessageHandler"/> 送出来。
    /// </summary>
    public static bool EnableDriverMessages(GlSeverity minimumSeverity = GlSeverity.Low)
    {
        if (GL.DebugMessageCallback is null)
            return false;

        GL.Enable(GL.DEBUG_OUTPUT);
        GL.Enable(GL.DEBUG_OUTPUT_SYNCHRONOUS);

        // 先把所有消息关掉，再按级别逐条打开。
        // 注意 DebugMessageControl 的 severity 只接受**单个** DEBUG_SEVERITY_* 枚举值（或 DONT_CARE），
        // 不是位掩码 —— 传位掩码会直接 INVALID_ENUM。
        GL.DebugMessageControl(GL.DONT_CARE, GL.DONT_CARE, GL.DONT_CARE, 0, null, 0);

        int lowestAllowed = minimumSeverity switch
        {
            GlSeverity.High => 0,
            GlSeverity.Medium => 1,
            GlSeverity.Low => 2,
            _ => 3,
        };

        for (int index = 0; index <= lowestAllowed; index++)
        {
            GL.DebugMessageControl(
                GL.DONT_CARE, GL.DONT_CARE, s_severities[index], 0, null, 1);
        }

        GL.DebugMessageCallback(&OnNativeMessage, null);
        DriverMessagesEnabled = true;
        return true;
    }

    /// <summary>从高到低的严重级别，用来做"最低级别"过滤。</summary>
    private static readonly uint[] s_severities =
    [
        GL.DEBUG_SEVERITY_HIGH,
        GL.DEBUG_SEVERITY_MEDIUM,
        GL.DEBUG_SEVERITY_LOW,
        GL.DEBUG_SEVERITY_NOTIFICATION,
    ];

    [UnmanagedCallersOnly]
    private static void OnNativeMessage(uint source, uint type, uint id, int severity, byte* message, void* userParam)
    {
        try
        {
            string text = message is null ? string.Empty : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
            string line = $"[GL {GlNative.SeverityName((uint)severity)}] 0x{id:X8} {text}";
            var handler = s_handler;
            if (handler is not null)
                handler(line);
            else if (EchoToConsole || severity == (int)GL.DEBUG_SEVERITY_HIGH)
                Console.Error.WriteLine(line);
        }
        catch
        {
            // 驱动回调里绝不能让异常穿出去
        }
    }
}
