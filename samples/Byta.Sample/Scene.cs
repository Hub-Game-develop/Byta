using System.Numerics;
using System.Runtime.CompilerServices;
using Byta.Bindings;

namespace Byta.Sample;

/// <summary>
/// 一个用 Byta 搭出来的最小渲染场景：1000 个实例化立方体 + 透视相机 + Blinn 风格着色，
/// 渲染到离屏 FBO（这样不需要窗口，能直接在 CI/后台线程跑）。
/// </summary>
internal sealed unsafe class Scene : IDisposable
{
    private const string VertexShader = """
        #version 410 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aModel0;
        layout(location = 3) in vec4 aModel1;
        layout(location = 4) in vec4 aModel2;
        layout(location = 5) in vec4 aModel3;

        uniform mat4 uViewProjection;
        uniform float uTime;

        out vec3 vNormal;
        out vec3 vWorld;

        void main()
        {
            mat4 model = mat4(aModel0, aModel1, aModel2, aModel3);

            // 从实例位置向外扩散的"呼吸波"——纯 GPU 侧计算，CPU 每帧零工作量
            vec3 center = aModel3.xyz;
            float wave = 0.5 + 0.5 * sin(length(center) * 0.62 - uTime * 1.7);
            float pulse = mix(0.86, 1.13, wave);

            vec4 world = model * vec4(aPosition * pulse, 1.0);
            vWorld = world.xyz;
            vNormal = normalize(mat3(model) * aNormal);
            gl_Position = uViewProjection * world;
        }
        """;

    private const string FragmentShader = """
        #version 410 core
        in vec3 vNormal;
        in vec3 vWorld;

        uniform vec3 uCameraPosition;
        uniform float uTime;

        out vec4 fragColor;

        void main()
        {
            vec3 normal = normalize(vNormal);
            vec3 lightDirection = normalize(vec3(0.45, 0.82, 0.55));
            float diffuse = max(dot(normal, lightDirection), 0.0);

            vec3 viewDirection = normalize(uCameraPosition - vWorld);
            vec3 halfVector = normalize(lightDirection + viewDirection);
            float specular = pow(max(dot(normal, halfVector), 0.0), 56.0);
            float rim = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.0);

            // 按高度取色的冷→暖渐变，比逐像素彩虹干净得多
            vec3 albedo = mix(vec3(0.10, 0.40, 0.92), vec3(0.97, 0.34, 0.66),
                              clamp(vWorld.y * 0.042 + 0.5, 0.0, 1.0));

            vec3 color = albedo * (0.16 + 0.84 * diffuse)
                       + specular * 0.6
                       + rim * vec3(0.22, 0.42, 0.95);

            fragColor = vec4(pow(max(color, 0.0), vec3(1.0 / 2.2)), 1.0);
        }
        """;

    private GlTexture2D _color = default!;
    private GlRenderbuffer _depth;
    private GlVertexArray _vao;
    private GlBuffer _vertices, _indices, _instances;
    private GlProgram _program = default!;
    private GlUniform _uViewProjection, _uCameraPosition, _uTime;

    public Scene(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int InstanceCount { get; private set; }
    public int IndexCount { get; private set; }
    public float Time { get; set; }

    public GlFramebuffer Framebuffer { get; private set; }
    public GlVertexArray VertexArray => _vao;
    public GlProgram Program => _program;

    public void Initialize()
    {
        // ---- 离屏目标：RGBA8 颜色 + 24 位深度
        Framebuffer = GlFramebuffer.Create();
        _color = GlTexture2D.Create();
        _color.Allocate(TextureFormat.Rgba8, Width, Height);
        _color.SetFilters(TextureFilter.Linear, TextureFilter.Linear);
        _depth = GlRenderbuffer.Create();
        _depth.Allocate(TextureFormat.Depth24, Width, Height);

        Framebuffer.AttachTexture(_color, FramebufferAttachment.Color0);
        Framebuffer.AttachRenderbuffer(_depth, FramebufferAttachment.Depth);
        Framebuffer.EnsureComplete();
       

        // ---- 几何：立方体（24 顶点，每面独立法线）
        // 注意顺序：core profile 里没有"默认 VAO"，绑定 ELEMENT_ARRAY_BUFFER 之前必须先绑一个 VAO。
        _vao = GlVertexArray.Create();
        _vao.Bind();

        (float[] vertices, uint[] indices) = BuildCube();
        IndexCount = indices.Length;

        _vertices = GlBuffer.Create();
        _vertices.Allocate(vertices.AsSpan());

        GlVertexArray.Attribute(0, 3, VertexAttribType.Float, false, 24, 0);   // position
        GlVertexArray.Attribute(1, 3, VertexAttribType.Float, false, 24, 12);  // normal

        float[] instanceData = BuildInstanceMatrices(7);
        InstanceCount = instanceData.Length / 16;
        _instances = GlBuffer.Create();
        _instances.Allocate(instanceData.AsSpan());

        for (int row = 0; row < 4; row++)
        {
            GlVertexArray.Attribute(2 + row, 4, VertexAttribType.Float, false, 64, row * 16);
            GlVertexArray.Divisor(2 + row, 1); // 每个实例前进一格
        }

        _indices = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        _indices.Allocate(indices.AsSpan()); // 索引缓冲的绑定会被当前 VAO 记住
       

        // ---- 着色器
        _program = GlProgram.FromSource(VertexShader, FragmentShader);
       
        _uViewProjection = _program.GetUniform("uViewProjection");
        _uCameraPosition = _program.GetUniform("uCameraPosition");
        _uTime = _program.GetUniform("uTime");

        GlState.BindVertexArray(0);
        GlState.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GlState.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

       
        GlDebug.Check("场景初始化");
    }

    public Matrix4x4 BuildViewProjection(out Vector3 eye)
    {
        float angle = Time * 0.18f;
        eye = new Vector3(MathF.Cos(angle) * 46f, 27f, MathF.Sin(angle) * 46f);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(0f, -1f, 0f), Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.6f, (float)Width / Height, 0.5f, 400f);

        // System.Numerics 是行向量约定（v * V * P），与下面 shader 里的 `uViewProjection * world`
        // 恰好等价：矩阵不转置上传时，GLSL 的列主序解释刚好把它转置回来。
        return view * projection;
    }

    public void BeginFrame()
    {
        Framebuffer.Bind(FramebufferTarget.Both);
        GlState.Viewport(0, 0, Width, Height);
        GlDraw.ClearColor(0.014f, 0.016f, 0.026f, 1f);
        GlDraw.Clear(ClearMask.ColorDepth);

        GlState.Enable(GlCapability.DepthTest);
        GlState.DepthFunc(DepthFunc.Less);
        GlState.Enable(GlCapability.CullFace);
        GlState.CullFace(FaceCulling.Back);
    }

    public void Draw()
    {
        _program.Use();
        _uViewProjection.Set(BuildViewProjection(out var eye));
        _uCameraPosition.Set(eye);
        _uTime.Set(Time);

        _vao.Bind();
        GlDraw.ElementsInstanced(PrimitiveType.Triangles, IndexCount, IndexType.UnsignedInt, 0, InstanceCount);
    }

    public void Render()
    {
        BeginFrame();
        Draw();
    }

    /// <summary>读回颜色附件（bottom-up，调用方自己决定要不要翻）。</summary>
    public void ReadColor(Span<byte> destination)
    {
        GlDraw.Finish();
        Framebuffer.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, destination);
    }

    public void Dispose()
    {
        GlState.BindFramebuffer(FramebufferTarget.Both, 0);
        Framebuffer.Dispose();
        _depth.Dispose();
        _color.Dispose();
        _vao.Dispose();
        _vertices.Dispose();
        _indices.Dispose();
        _instances.Dispose();
        _program.Dispose();
    }

    private static (float[] Vertices, uint[] Indices) BuildCube()
    {
        // 用 (u × v = n) 的正交基生成每个面，绕序天然是"从法线方向看逆时针"，
        // 不靠手工排列顶点去猜，配合背面剔除不会出现半个面消失。
        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        [
            (new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)),
            (new(0, 0, -1), new(-1, 0, 0), new(0, 1, 0)),
            (new(1, 0, 0), new(0, 0, -1), new(0, 1, 0)),
            (new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
            (new(0, 1, 0), new(1, 0, 0), new(0, 0, -1)),
            (new(0, -1, 0), new(1, 0, 0), new(0, 0, 1)),
        ];

        var vertices = new float[24 * 6];
        var indices = new uint[36];

        int v = 0, i = 0;
        for (int face = 0; face < faces.Length; face++)
        {
            var (normal, u, w) = faces[face];
            Span<Vector3> corners =
            [
                (0.5f * (-u - w)) + 0.5f * normal,
                (0.5f * (u - w)) + 0.5f * normal,
                (0.5f * (u + w)) + 0.5f * normal,
                (0.5f * (-u + w)) + 0.5f * normal,
            ];

            uint baseIndex = (uint)(face * 4);
            foreach (var p in corners)
            {
                vertices[v++] = p.X;
                vertices[v++] = p.Y;
                vertices[v++] = p.Z;
                vertices[v++] = normal.X;
                vertices[v++] = normal.Y;
                vertices[v++] = normal.Z;
            }

            indices[i++] = baseIndex;
            indices[i++] = baseIndex + 1;
            indices[i++] = baseIndex + 2;
            indices[i++] = baseIndex;
            indices[i++] = baseIndex + 2;
            indices[i++] = baseIndex + 3;
        }

        return (vertices, indices);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static float[] BuildInstanceMatrices(int gridSize)
    {
        int count = gridSize * gridSize * gridSize;
        var data = new float[count * 16];

        float spacing = 3.05f;
        float offset = (gridSize - 1) * spacing * 0.5f;

        fixed (float* p = data)
        {
            int index = 0;
            for (int x = 0; x < gridSize; x++)
            for (int y = 0; y < gridSize; y++)
            for (int z = 0; z < gridSize; z++)
            {
                var position = new Vector3(x * spacing - offset, y * spacing - offset, z * spacing - offset);
                float phase = (x * 7 + y * 13 + z * 23) * 0.37f;
                float scale = 2.30f + 0.10f * MathF.Sin(phase * 1.7f); // 边长 = 缩放值，必须 < 格距 3.05 否则立方体互相穿插

                var model = Matrix4x4.CreateScale(scale)
                          * Matrix4x4.CreateFromYawPitchRoll(phase, phase * 0.6f, phase * 0.3f)
                          * Matrix4x4.CreateTranslation(position);

                *(Matrix4x4*)(p + index * 16) = model;
                index++;
            }
        }

        return data;
    }
}
