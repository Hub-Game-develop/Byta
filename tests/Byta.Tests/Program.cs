using Byta;
using Byta.Bindings;
using Byta.Platform;

namespace Byta.Tests;

/// <summary>
/// 在真实 GL 上下文上跑的断言式测试（不依赖测试框架，进程退出码表示结果）。
/// 每一项都是"真的建了 GL 对象、真的画了、真的把像素读回来比对"，没有 mock。
/// </summary>
internal static unsafe class Program
{
    private static int s_passed;
    private static int s_failed;

    private const string VertexShader = """
        #version 410 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aOffset;

        uniform float uDepth;

        void main()
        {
            gl_Position = vec4(aPosition + aOffset, uDepth, 1.0);
        }
        """;

    private const string FragmentShader = """
        #version 410 core
        uniform vec4 uColor;
        out vec4 fragColor;
        void main() { fragColor = uColor; }
        """;

    // x, y, offsetX, offsetY —— 覆盖画面中央的三角形
    private const int VertexStride = 16;
    private static readonly float[] TriangleVertices =
    [
        -0.55f, -0.45f, 0f, 0f,
         0.55f, -0.45f, 0f, 0f,
         0.00f,  0.50f, 0f, 0f,
    ];

    private static int Main()
    {
        bool forceSystem = Environment.GetCommandLineArgs().Contains("--system");
        using var session = BytaRuntime.Initialize(new BytaOptions { PreferBundledImplementation = !forceSystem });
        var capabilities = session.Capabilities;

        if (Environment.GetCommandLineArgs().Contains("--missing"))
        {
            foreach (var name in GL.Missing)
                Console.WriteLine(name);
            return 0;
        }

        Console.WriteLine($"实现       ：{session.Implementation.Name}（驱动 {session.DriverName}）");
        Console.WriteLine($"上下文     ：{capabilities}");
        Console.WriteLine($"缺失入口点 ：{GL.MissingCount}");
        Console.WriteLine();

        TestImplementationSelection(session);

        TestEntryPoints(capabilities);
        TestCapabilities(capabilities);
        TestBufferRoundtrip();
        TestBufferPartialUpload();
        TestShaderCompileFailure();
        TestProgramUniforms();
        TestTextureRoundtrip();
        TestIncompleteFramebuffer();
        TestIndexBufferIsVertexArrayState();
        TestDepthTest();
        TestInstancing();
        TestNoManagedAllocation();

        Console.WriteLine();
        Console.WriteLine(s_failed == 0
            ? $"全部通过：{s_passed} 项"
            : $"通过 {s_passed} 项，失败 {s_failed} 项");

        var errors = GlDebug.Drain();
        Console.WriteLine(errors.Count == 0
            ? "GL 错误队列：干净"
            : $"遗留 GL 错误：{string.Join(", ", errors.Select(e => $"0x{e:X4}"))}");

        return s_failed == 0 && errors.Count == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 测试

    private static void TestImplementationSelection(BytaSession session)
    {
        Check("拿到了上下文与能力信息", session.Capabilities.Version.Major >= 3);
        Check("实际版本不低于请求版本", session.Capabilities.Version >= session.RequestedVersion
            || session.RequestedVersion == session.Capabilities.Version);
        Check($"自动选中了实现：{session.Implementation.Source}", session.Implementation.LibraryPath.Length > 0);
    }

    private static void TestEntryPoints(GlCapabilities capabilities)
    {
        Check("入口点表已加载", GL.IsLoaded);
        Check("glGetError / glGetString 可用", GL.GetError is not null && GL.GetString is not null);
        Check("glCreateProgram / glGenVertexArrays 可用", GL.CreateProgram is not null && GL.GenVertexArrays is not null);

        // 4.4 才引入的入口点：跟着实现版本走，不写死"必须缺失"
        bool expectsBufferStorage = capabilities.AtLeast(4, 4);
        Check($"glBufferStorage 可用性与实际版本 {capabilities.Version} 相符",
            (GL.BufferStorage is not null) == expectsBufferStorage);
    }

    private static void TestCapabilities(GlCapabilities capabilities)
    {
        Check("版本 >= 4.1", capabilities.AtLeast(4, 1));
        Check("是 core profile", capabilities.IsCoreProfile);
        Check("扩展列表非空", capabilities.Extensions.Count > 0);
        Check("MaxTextureSize >= 4096", capabilities.MaxTextureSize >= 4096);
    }

    private static void TestBufferRoundtrip()
    {
        using var buffer = GlBuffer.Create();
        float[] data = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
        buffer.Allocate(data.AsSpan(), BufferUsage.StaticDraw);

        float[] readBack = new float[data.Length];
        fixed (float* p = readBack)
            GL.GetBufferSubData(GL.ARRAY_BUFFER, 0, data.Length * sizeof(float), p);

        Check("缓冲往返数据一致", data.AsSpan().SequenceEqual(readBack));
    }

    private static void TestBufferPartialUpload()
    {
        using var buffer = GlBuffer.Create();
        buffer.Allocate<float>([0f, 0f, 0f, 0f]);
        buffer.Upload<float>([9f, 8f], elementOffset: 1);

        float[] readBack = new float[4];
        fixed (float* p = readBack)
            GL.GetBufferSubData(GL.ARRAY_BUFFER, 0, 4 * sizeof(float), p);

        Check("部分上传写在正确偏移", readBack is [0f, 9f, 8f, 0f]);
    }

    private static void TestShaderCompileFailure()
    {
        bool threw = false;
        string message = string.Empty;
        try
        {
            GlShader.Compile(GlShaderType.Vertex, "void main() { this is not glsl }");
        }
        catch (GlException ex)
        {
            threw = true;
            message = ex.Message;
        }

        Check("错误的着色器会抛异常", threw);
        Check("异常里带着驱动的编译日志", message.Contains("ERROR", StringComparison.OrdinalIgnoreCase));

        bool ok = GlShader.TryCompile(GlShaderType.Fragment, "void main() { broken }", out var shader, out string log);
        Check("TryCompile 失败时返回 false 且不抛", !ok && !shader.IsValid && log.Length > 0);
    }

    private static void TestProgramUniforms()
    {
        using var program = GlProgram.FromSource(VertexShader, FragmentShader);

        program.GetUniform("uDepth").Set(0.25f);
        float depth = -1;
        GL.GetUniformfv(program.Id, program.GetUniformLocation("uDepth"), &depth);
        Check("uniform float 写入后读回一致", Math.Abs(depth - 0.25f) < 1e-6f);

        program.GetUniform("uColor").Set(0.1f, 0.2f, 0.3f, 0.4f);
        Span<float> color = stackalloc float[4];
        fixed (float* p = color)
            GL.GetUniformfv(program.Id, program.GetUniformLocation("uColor"), p);
        Check("uniform vec4 写入后读回一致", Math.Abs(color[2] - 0.3f) < 1e-6f && Math.Abs(color[3] - 0.4f) < 1e-6f);

        Check("uniform 位置查询有缓存", program.GetUniformLocation("uDepth") == program.GetUniformLocation("uDepth"));
        Check("不存在的 uniform 返回 -1", program.GetUniformLocation("uNotThere") == -1);
    }

    private static void TestTextureRoundtrip()
    {
        const int size = 8;
        using var texture = GlTexture2D.Create();
        texture.Allocate(TextureFormat.Rgba8, size, size);

        var pattern = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            pattern[i * 4 + 0] = (byte)(i * 3);
            pattern[i * 4 + 1] = (byte)(i * 5);
            pattern[i * 4 + 2] = (byte)(i * 7);
            pattern[i * 4 + 3] = 255;
        }
        texture.Upload(TextureFormat.Rgba8, size, size, pattern);

        using var target = RenderTarget.Create(size, size, depth: false);
        target.Framebuffer.AttachTexture(texture);
        target.Framebuffer.EnsureComplete();

        var readBack = new byte[size * size * 4];
        target.Framebuffer.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, readBack);

        Check("8×8 RGBA8 纹理上传后回读完全一致", pattern.AsSpan().SequenceEqual(readBack));
    }

    private static void TestIncompleteFramebuffer()
    {
        using var framebuffer = GlFramebuffer.Create();
        Check("没挂附件的 FBO 状态不是 COMPLETE", framebuffer.CheckStatus() != FramebufferStatus.Complete);

        bool threw = false;
        try
        {
            framebuffer.EnsureComplete();
        }
        catch (GlException)
        {
            threw = true;
        }
        Check("EnsureComplete 对不完整 FBO 抛异常", threw);
    }

    /// <summary>
    /// 防回归专用：ELEMENT_ARRAY_BUFFER 的绑定属于 VAO 状态。
    /// 缓存若把它当全局状态，切换 VAO 之后就会用错索引缓冲（这个 bug 我在开发时真踩到了）。
    /// </summary>
    private static void TestIndexBufferIsVertexArrayState()
    {
        using var vertices = GlBuffer.Create();
        vertices.Allocate<float>(TriangleVertices);

        using var indicesA = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        indicesA.Allocate<uint>([0u, 1u, 2u]);

        using var indicesB = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        indicesB.Allocate<uint>([0u, 2u, 1u]);

        using var vaoA = CreateVao(vertices, indicesA);
        using var vaoB = CreateVao(vertices, indicesB);

        vaoA.Bind();
        int bound = 0;
        GL.GetIntegerv(GL.ELEMENT_ARRAY_BUFFER_BINDING, &bound);
        Check("VAO A 的索引缓冲是 indicesA", (uint)bound == indicesA.Id);

        vaoB.Bind();
        GL.GetIntegerv(GL.ELEMENT_ARRAY_BUFFER_BINDING, &bound);
        Check("切到 VAO B 后索引缓冲变成 indicesB", (uint)bound == indicesB.Id);

        vaoA.Bind();
        GL.GetIntegerv(GL.ELEMENT_ARRAY_BUFFER_BINDING, &bound);
        Check("切回 VAO A 又变回 indicesA", (uint)bound == indicesA.Id);
    }

    private static void TestDepthTest()
    {
        using var program = GlProgram.FromSource(VertexShader, FragmentShader);
        using var vertices = GlBuffer.Create();
        vertices.Allocate<float>(TriangleVertices);
        using var indices = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        indices.Allocate<uint>([0u, 1u, 2u]);
        using var vao = CreateVao(vertices, indices);

        using var target = RenderTarget.Create(64, 64, depth: true);
        GlState.Viewport(0, 0, 64, 64);
        GlState.Enable(GlCapability.DepthTest);
        GlState.DepthFunc(DepthFunc.Less);
        GlState.DepthMask(true);
        GlDraw.ClearColor(0f, 0f, 0f, 1f);
        GL.ClearDepth(1.0);
        GlDraw.Clear(ClearMask.ColorDepth);

        program.Use();
        vao.Bind();
        var depth = program.GetUniform("uDepth");
        var color = program.GetUniform("uColor");

        var pixel = new byte[4];
        void DrawAt(float z, float r, float g, float b)
        {
            depth.Set(z);
            color.Set(r, g, b, 1f);
            GlDraw.Elements(PrimitiveType.Triangles, 3, IndexType.UnsignedInt);
        }

        DrawAt(0.9f, 0f, 1f, 0f);          // 远的绿
        target.Framebuffer.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        Check("远处的绿色三角形画上了", pixel[1] > 200 && pixel[0] < 60);

        DrawAt(0.1f, 1f, 0f, 0f);          // 近的红，应该盖掉绿
        target.Framebuffer.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        Check("更近的红色通过深度测试并覆盖", pixel[0] > 200 && pixel[1] < 60);

        DrawAt(0.9f, 0f, 1f, 0f);          // 再次画远的绿，应被深度拒绝
        target.Framebuffer.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        Check("远处三角形被深度测试拒绝，红色仍在", pixel[0] > 200 && pixel[1] < 60);

        GlState.Disable(GlCapability.DepthTest);
    }

    private static void TestInstancing()
    {
        using var program = GlProgram.FromSource(VertexShader, FragmentShader);

        // 顶点缓冲：只有一个三角形（每顶点 2 float）
        float[] triangle =
        [
            -0.22f, -0.45f,
             0.22f, -0.45f,
             0.00f,  0.45f,
        ];
        using var vertexBuffer = GlBuffer.Create();
        vertexBuffer.Allocate<float>(triangle);

        // 实例缓冲：每个实例一个偏移（attribute 1，步进 1 个实例）
        float[] offsets =
        [
            -0.55f, 0f,
             0.00f, 0f,
             0.55f, 0f,
        ];
        using var instanceBuffer = GlBuffer.Create();
        instanceBuffer.Allocate<float>(offsets);

        using var vao = GlVertexArray.Create();
        vao.Bind();
        vertexBuffer.Bind();
        GlVertexArray.Attribute(0, 2, VertexAttribType.Float, false, 8, 0);
        instanceBuffer.Bind();
        GlVertexArray.Attribute(1, 2, VertexAttribType.Float, false, 8, 0);
        GlVertexArray.Divisor(1, 1); // 每个实例前进一格

        using var target = RenderTarget.Create(96, 32, depth: false);
        GlState.Viewport(0, 0, 96, 32);
        GlDraw.ClearColor(0f, 0f, 0f, 1f);
        GlDraw.Clear(ClearMask.Color);
        GlState.Disable(GlCapability.DepthTest);

        program.Use();
        program.GetUniform("uDepth").Set(0f);
        program.GetUniform("uColor").Set(1f, 1f, 0f, 1f);
        vao.Bind();
        GlDraw.ArraysInstanced(PrimitiveType.Triangles, 0, 3, 3);

        var pixels = new byte[96 * 32 * 4];
        target.Framebuffer.ReadPixels(0, 0, 96, 32, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        int BrightnessAt(int x)
        {
            int index = (16 * 96 + x) * 4; // y=16：三角形中部
            return pixels[index] + pixels[index + 1] + pixels[index + 2];
        }

        Check("三个实例分别落在左 / 中 / 右（实例化生效）",
            BrightnessAt(21) > 200 && BrightnessAt(48) > 200 && BrightnessAt(74) > 200);
        Check("实例之外的像素仍是背景色", BrightnessAt(2) < 40 && BrightnessAt(32) < 40 && BrightnessAt(94) < 40);
    }

    private static void TestNoManagedAllocation()
    {
        using var program = GlProgram.FromSource(VertexShader, FragmentShader);
        using var vertices = GlBuffer.Create();
        vertices.Allocate<float>(TriangleVertices);
        using var indices = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        indices.Allocate<uint>([0u, 1u, 2u]);
        using var vao = CreateVao(vertices, indices);
        using var target = RenderTarget.Create(16, 16, depth: true);

        program.Use();
        vao.Bind();
        GL.ColorMask(1, 1, 1, 1);
        var depth = program.GetUniform("uDepth");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            program.Use();
            vao.Bind();
            GlState.Viewport(0, 0, 16, 16);
            GlState.Enable(GlCapability.DepthTest);
            depth.Set(0.5f);
            GlDraw.Elements(PrimitiveType.Triangles, 3, IndexType.UnsignedInt);
            GlState.Disable(GlCapability.DepthTest);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Check($"2000 次绘制循环托管分配为 0（实际 {allocated} 字节）", allocated == 0);
    }

    // ---------------------------------------------------------------- 工具

    private static GlVertexArray CreateVao(GlBuffer vertexBuffer, GlBuffer indexBuffer)
    {
        var vao = GlVertexArray.Create();
        vao.Bind();
        vertexBuffer.Bind();
        GlVertexArray.Attribute(0, 2, VertexAttribType.Float, false, VertexStride, 0);
        GlVertexArray.Attribute(1, 2, VertexAttribType.Float, false, VertexStride, 8);
        indexBuffer.Bind();
        return vao;
    }

    private sealed class RenderTarget : IDisposable
    {
        private GlTexture2D _color;
        private GlRenderbuffer _depth;
        private bool _hasDepth;

        public GlFramebuffer Framebuffer { get; private set; }

        public static RenderTarget Create(int width, int height, bool depth)
        {
            var target = new RenderTarget
            {
                _color = GlTexture2D.Create(),
                Framebuffer = GlFramebuffer.Create(),
                _hasDepth = depth,
            };

            target._color.Allocate(TextureFormat.Rgba8, width, height);
            target.Framebuffer.AttachTexture(target._color);

            if (depth)
            {
                target._depth = GlRenderbuffer.Create();
                target._depth.Allocate(TextureFormat.Depth24, width, height);
                target.Framebuffer.AttachRenderbuffer(target._depth, FramebufferAttachment.Depth);
            }

            target.Framebuffer.EnsureComplete();
            return target;
        }

        public void Dispose()
        {
            GlState.BindFramebuffer(FramebufferTarget.Both, 0);
            Framebuffer.Dispose();
            _color.Dispose();
            if (_hasDepth)
                _depth.Dispose();
        }
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
        {
            s_passed++;
            Console.WriteLine($"  [通过] {name}");
        }
        else
        {
            s_failed++;
            Console.WriteLine($"  [失败] {name}");
        }
    }
}
