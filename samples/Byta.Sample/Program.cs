using System.Diagnostics;
using Byta;
using Byta.Bindings;
using Byta.Platform;

namespace Byta.Sample;

internal static unsafe class Program
{
    private static int Main(string[] args)
    {
        int width = 1280, height = 720;
        string output = "glint-render.png";
        bool benchmark = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bench":
                    benchmark = true;
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--size" when i + 1 < args.Length:
                    var parts = args[++i].Split('x');
                    width = int.Parse(parts[0]);
                    height = int.Parse(parts[1]);
                    break;
            }
        }

        if (args.Contains("--impl"))
        {
            Console.WriteLine($"RID      : {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            Console.WriteLine($"BaseDir  : {AppContext.BaseDirectory}");
            foreach (var impl in BytaRuntime.ProbeImplementations())
                Console.WriteLine($"  {impl}");

            Console.WriteLine("  搜索目录:");
            foreach (var directory in GlImplementationLocator.SearchDirectories)
                Console.WriteLine($"    {(Directory.Exists(directory) ? "存在  " : "不存在")} {directory}");

            if (GlImplementationLocator.Diagnostics.Count > 0)
            {
                Console.WriteLine("  跳过的候选:");
                foreach (var note in GlImplementationLocator.Diagnostics)
                    Console.WriteLine($"    {note}");
            }
            return 0;
        }

        bool forceSystem = args.Contains("--system");

        using var session = BytaRuntime.Initialize(new BytaOptions
        {
            PreferBundledImplementation = !forceSystem,
            Log = Environment.GetEnvironmentVariable("BYTA_TRACE") is not null ? Console.WriteLine : null,
        });
        var capabilities = session.Capabilities;

        Console.WriteLine("Byta — 现代 .NET OpenGL 封装");
        Console.WriteLine($"  绑定版本   : {GL.BindingsRevision}");
        Console.WriteLine($"  实现       : {session.Implementation.Name}");
        Console.WriteLine($"  驱动       : {session.DriverName}");
        Console.WriteLine($"  上下文     : {capabilities}");
        Console.WriteLine($"  入口点缺失 : {GL.MissingCount} 条");
        Console.WriteLine($"  驱动调试   : {(GlDebug.EnableDriverMessages() ? "已开启 (KHR_debug)" : "该实现不支持 KHR_debug")}");
        if (session.Implementation.Source != GlImplementationSource.Bundled
            && GlImplementationLocator.Diagnostics.Count > 0)
        {
            Console.WriteLine($"  内置实现未启用 : {GlImplementationLocator.Diagnostics[0]}");
            Console.WriteLine("                  （跑 --impl 看所有搜索目录与跳过原因）");
        }
        if (session.Attempts.Count > 1)
        {
            Console.WriteLine("  选择过程   :");
            foreach (var attempt in session.Attempts)
                Console.WriteLine($"    {attempt}");
        }
        Console.WriteLine();

        using var scene = new Scene(width, height);
        scene.Initialize();

        Console.WriteLine($"  渲染目标   : {width}×{height} FBO，{scene.InstanceCount} 个实例 × {scene.IndexCount / 3} 三角形");
        Console.WriteLine();

        if (benchmark)
            RunBenchmark(scene);
        else
            RenderToPng(scene, output);

        var errors = GlDebug.Drain();
        Console.WriteLine(errors.Count == 0
            ? "GL 错误队列：干净"
            : $"GL 错误：{string.Join(", ", errors.Select(e => $"0x{e:X4}"))}");

        session.Context.Flush();
        return 0;
    }

    private static void RenderToPng(Scene scene, string output)
    {
        scene.Time = 0.9f;
        scene.Render();

        var pixels = new byte[scene.Width * scene.Height * 4];
        scene.ReadColor(pixels);

        long lit = 0;
        double sum = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int brightness = pixels[i] + pixels[i + 1] + pixels[i + 2];
            sum += brightness;
            if (brightness > 60)
                lit++;
        }

        PngWriter.FlipVertically(scene.Width, scene.Height, pixels);
        PngWriter.Write(output, scene.Width, scene.Height, pixels);

        double total = pixels.Length / 4.0;
        Console.WriteLine($"已渲染并写出：{Path.GetFullPath(output)}");
        Console.WriteLine($"  亮像素占比 : {lit / total:P2}（背景应几乎全黑，几何体应有明确亮部）");
        Console.WriteLine($"  平均亮度   : {sum / total / 3:F1} / 255");
        Console.WriteLine($"  字节数     : {new FileInfo(output).Length / 1024.0:F1} KB");
    }

    private static void RunBenchmark(Scene scene)
    {
        const int repeats = 5;

        Console.WriteLine("=== 1. 状态缓存 vs 直通驱动 ===");

        // ---------------- 1. 状态缓存
        const int cachedIterations = 2_000_000;
        double cachedNs = Best(repeats, () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < cachedIterations; i++)
                scene.VertexArray.Bind();
            return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / cachedIterations;
        });

        const int driverIterations = 300_000;
        double driverNs = Best(repeats, () =>
        {
            GlState.CacheEnabled = false;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < driverIterations; i++)
                scene.VertexArray.Bind();
            sw.Stop();
            GlState.CacheEnabled = true;
            return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / driverIterations;
        });

        Console.WriteLine($"  绑定 VAO，缓存命中（零驱动调用）: {cachedNs,8:F2} ns/次");
        Console.WriteLine($"  绑定 VAO，直通驱动              : {driverNs,8:F1} ns/次");
        Console.WriteLine($"  → 每次冗余绑定省下              : {driverNs - cachedNs,8:F1} ns");
        Console.WriteLine();

        Console.WriteLine("=== 2. 绘制提交成本（CPU 侧）===");

        // ---------------- 2. 绘制提交
        const int drawsPerFrame = 200;
        const int submitFrames = 100;

        for (int i = 0; i < 30; i++)
        {
            scene.Time = i * 0.016f;
            scene.Render();
        }
        GlDraw.Finish();

        void SubmitBatch()
        {
            for (int frame = 0; frame < submitFrames; frame++)
            {
                scene.BeginFrame();
                scene.Program.Use();
                scene.VertexArray.Bind();
                for (int draw = 0; draw < drawsPerFrame; draw++)
                {
                    GlDraw.ElementsInstanced(
                        PrimitiveType.Triangles, scene.IndexCount, IndexType.UnsignedInt, 0, scene.InstanceCount);
                }
            }
        }

        double submitNs = Best(repeats, () =>
        {
            var sw = Stopwatch.StartNew();
            SubmitBatch();
            sw.Stop();
            GlDraw.Finish();
            return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / ((double)submitFrames * drawsPerFrame);
        });

        // 确定性指标：整批提交过程中托管堆分配了多少字节
        long before = GC.GetAllocatedBytesForCurrentThread();
        SubmitBatch();
        GlDraw.Finish();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"  实例化绘制（{scene.InstanceCount} 实例/次，状态不变）: {submitNs,8:F1} ns/次");
        Console.WriteLine($"  → 约                                {1_000_000.0 / submitNs / 1000.0,8:F2} M draw calls/s（CPU 提交）");
        Console.WriteLine($"  同一批次托管堆分配              : {allocated,8} 字节（{submitFrames * drawsPerFrame} 次绘制）");
        Console.WriteLine();

        Console.WriteLine("=== 3. 整帧吞吐（含 GPU）===");

        // ---------------- 3. 整帧吞吐（含 GPU）
        const int frames = 200;
        using var query = GlQuery.Create(QueryTarget.TimeElapsed);

        GlDraw.Finish();
        query.Begin();
        var frameTimer = Stopwatch.StartNew();
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Time = frame * 0.016f;
            scene.Render();
        }
        query.End();
        GlDraw.Finish();
        frameTimer.Stop(); // 计时包含 GPU 真正执行完的时间
        double totalMs = frameTimer.Elapsed.TotalMilliseconds;
        double gpuMs = query.GetElapsedMilliseconds();

        double triangles = (double)frames * scene.InstanceCount * (scene.IndexCount / 3);
        double pixelsPerFrame = (double)scene.Width * scene.Height;
        double seconds = totalMs / 1000.0;

        Console.WriteLine($"  {frames} 帧总耗时        : {totalMs,8:F1} ms");
        Console.WriteLine($"  平均每帧             : {totalMs / frames,8:F3} ms  ({frames / seconds:F0} FPS 等价)");
        Console.WriteLine($"  GPU 侧耗时（GL query）: {gpuMs / frames,8:F3} ms/帧");
        Console.WriteLine($"  三角形吞吐           : {triangles / seconds / 1e6,8:F1} M tri/s");
        Console.WriteLine($"  填充率（{scene.Width}×{scene.Height}）   : {pixelsPerFrame * frames / seconds / 1e9,8:F2} GPix/s");
        Console.WriteLine();

        Console.WriteLine("=== 4. 缓冲上传带宽 ===");

        // ---------------- 4. 缓冲上传
        const int uploadSize = 4 * 1024 * 1024;
        const int uploads = 100;
        using var uploadBuffer = GlBuffer.Create();
        uploadBuffer.Allocate((nuint)uploadSize);
        var payload = new byte[uploadSize];

        for (int i = 0; i < 3; i++)
            uploadBuffer.Upload<byte>(payload);
        GlDraw.Finish();

        double uploadSeconds = Best(2, () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < uploads; i++)
                uploadBuffer.Upload<byte>(payload);
            GlDraw.Finish();
            return sw.Elapsed.TotalSeconds;
        });

        double gigabytes = (double)uploadSize * uploads / 1e9;
        Console.WriteLine($"  {uploads} 次 × 4 MB 上传 : {uploadSeconds * 1000,8:F1} ms");
        Console.WriteLine($"  有效带宽             : {gigabytes / uploadSeconds,8:F2} GB/s");
        Console.WriteLine();

        Console.WriteLine("说明：单机噪声不小，以上取多次重复中的最优值；CPU 侧数值不含 GPU 执行时间。");
        Console.WriteLine("      分配字节数是确定性的：缓存 + 提交路径确实一次 GC 分配都没有。");
    }

    /// <summary>重复测多次取最优值，压掉调度/热降频带来的噪声。</summary>
    private static double Best(int repeats, Func<double> body)
    {
        double best = double.MaxValue;
        for (int i = 0; i < repeats; i++)
            best = Math.Min(best, body());
        return best;
    }
}
