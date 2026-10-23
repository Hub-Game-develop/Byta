using Byta.Bindings;

namespace Byta;

/// <summary>
/// 一个 OpenGL 上下文的最小契约。
/// <para>
/// Byta 不要求你一定要用它的实现：上下文既可以由 <see cref="BytaRuntime.Initialize"/> 自动挑一个
/// 最高版本的实现来建（自带 Mesa/zink 或系统 CGL），也可以由宿主提供
/// （GLFW / SDL / Avalonia / Win32 ...）。库的核心始终只是"入口点表 + 对象封装 + 状态缓存"。
/// </para>
/// </summary>
public interface IGlContext : IDisposable
{
    /// <summary>把上下文设为当前线程的当前上下文，并重置状态缓存。</summary>
    void MakeCurrent();

    /// <summary>提交命令（不等待 GPU）。</summary>
    void Flush();

    /// <summary>本上下文当前是否就是当前线程的当前上下文。</summary>
    bool IsCurrent { get; }
}

