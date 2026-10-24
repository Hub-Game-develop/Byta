// SPDX-License-Identifier: MIT
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Byta.Browser;

/// <summary>浏览器里的初始化入口：建 WebGL2 上下文 → 装上后端 → 读能力。</summary>
public static class BytaBrowserRuntime
{
    private static bool s_loaded;

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
            await JSHost.ImportAsync("byta-gl.js", "./byta-gl.js");
            s_loaded = true;
        }

        if (WebGl.Attach(canvasId) == 0)
            throw new GlException($"无法在 #{canvasId} 上创建 WebGL2 上下文（浏览器不支持或已被占用）");

        WebGl.SetupViewport(0, 0, pixelRatio);

        GlApi.UseBackend(new WebGlBackend());
        GlState.Reset();

        return GlCapabilities.Query();
    }

    /// <summary>回到原生入口点表（浏览器里通常没有意义，主要给测试用）。</summary>
    public static void Detach() => GlApi.UseBackend(null);
}
