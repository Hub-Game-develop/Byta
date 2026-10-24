// SPDX-License-Identifier: MIT
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Byta.Browser;

/// <summary>浏览器里的初始化入口：建 WebGL2 上下文 → 装上后端 → 读能力。</summary>
public static class BytaBrowserRuntime
{
    private static bool s_loaded;

    /// <summary>最近一次初始化的诊断信息（排查 JS 互操作问题时用）。</summary>
    public static string LastDiagnostics { get; private set; } = string.Empty;

    /// <summary>
    /// byta-gl.js 的版本号。ES 模块的 URL 是强缓存的，改了 JS 而不改这里的值，
    /// 浏览器会继续用旧文件 —— 表现是"改了代码但错误一模一样"。
    /// 每次修改 byta-gl.js 都要递增它。
    /// </summary>
    private const string JsVersion = "6";

    /// <summary>
    /// 把 <paramref name="canvasId"/> 对应的 canvas 变成 Byta 的渲染目标。
    /// 之后所有 <c>GlBuffer</c> / <c>GlProgram</c> / <c>GlDraw</c> 调用都会走 WebGL2。
    /// </summary>
    /// <param name="canvasId">页面里 canvas 元素的 id。</param>
    /// <param name="pixelRatio">设备像素比；0 表示由 JS 侧按 devicePixelRatio 取。</param>
    public static async Task<GlCapabilities> InitializeAsync(string canvasId = "byta-canvas", double pixelRatio = 0)
    {
        if (!s_loaded)
        {
            // 注意路径基准：Blazor 的模块导入相对于 _framework/ 解析（"../" 也会被归一掉），
            // 所以这个文件必须真的放在 _framework/ 里 —— 由 Byta.Sample.Web.csproj 的
            // CopyBytaGlIntoFramework target 在 publish 后复制进去。
            await JSHost.ImportAsync("byta-gl.js", $"./byta-gl.js?v={JsVersion}");
            s_loaded = true;
        }

        if (WebGl.Attach(canvasId) == 0)
            throw new GlException($"无法在 #{canvasId} 上创建 WebGL2 上下文（浏览器不支持或已被占用）");

        WebGl.SetupViewport(0, 0, pixelRatio);

        // 诊断：把 Span 参数在 JS 侧的真实形态记下来，出错时能定位是编组问题还是逻辑问题
        try
        {
            var probe = new byte[8];
            var description = WebGl.SelfTest(probe);
            Console.WriteLine($"[byta] MemoryView 形态: {description}");
            probe[0] = 0xAB;
            LastDiagnostics = description;
        }
        catch (Exception ex)
        {
            LastDiagnostics = "selfTest 失败: " + ex.Message;
            Console.WriteLine("[byta] " + LastDiagnostics);
        }

        GlApi.UseBackend(new WebGlBackend());
        GlState.Reset();

        return GlCapabilities.Query();
    }

    /// <summary>回到原生入口点表（浏览器里通常没有意义，主要给测试用）。</summary>
    public static void Detach() => GlApi.UseBackend(null);
}
