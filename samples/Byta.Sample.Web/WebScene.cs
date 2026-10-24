// SPDX-License-Identifier: MIT
using System;
using System.Numerics;
using Byta;
using Byta.Bindings;
using Byta.Browser;

namespace Byta.Sample.Web;

/// <summary>
/// 与原生 <c>Byta.Sample</c> 相同的场景：7 个实例化的立方体 + Blinn-Phong 光照 + 边缘光。
/// <para>
/// 唯一的区别是着色器写成 GLSL ES 3.00（WebGL2 的要求），而 <see cref="GlShader"/>
/// 会自动补上 <c>#version 300 es</c> 和 <c>precision</c> 声明。
/// </para>
/// </summary>
public sealed unsafe class WebScene : IDisposable
{
    private GlProgram _program = default;
    private GlBuffer _vertices;
    private GlBuffer _indices;
    private GlBuffer _instances;
    private GlVertexArray _vao;
    private Matrix4x4 _lastViewProjection;

    /// <summary>诊断：只画一个三角形。</summary>
    public static bool DiagnosticSingleTriangle { get; set; } = true;

    public int IndexCount { get; private set; }
    public int InstanceCount { get; private set; }
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 540;

    private const string VertexShader = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aModel0;
        layout(location = 3) in vec4 aModel1;
        layout(location = 4) in vec4 aModel2;
        layout(location = 5) in vec4 aModel3;

        uniform mat4 uViewProjection;

        out vec3 vNormal;
        out vec3 vWorld;

        void main()
        {
            mat4 model = mat4(aModel0, aModel1, aModel2, aModel3);
            vec4 world = model * vec4(aPosition, 1.0);
            vWorld = world.xyz;
            vNormal = normalize(mat3(model) * aNormal);
            gl_Position = uViewProjection * world;
        }
        """;

    private const string FragmentShader = """
        in vec3 vNormal;
        in vec3 vWorld;

        uniform vec3 uCameraPosition;
        uniform float uTime;

        out vec4 fragColor;

        void main()
        {
            vec3 normal = normalize(vNormal);
            vec3 lightDirection = normalize(vec3(0.45, 0.8, 0.55));
            float diffuse = max(dot(normal, lightDirection), 0.0);

            vec3 viewDirection = normalize(uCameraPosition - vWorld);
            vec3 halfVector = normalize(lightDirection + viewDirection);
            float specular = pow(max(dot(normal, halfVector), 0.0), 48.0);
            float rim = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.0);

            vec3 albedo = 0.42 + 0.42 * cos(uTime + vWorld.xxz * 0.55 + vec3(0.0, 2.1, 4.2));
            vec3 color = albedo * (0.18 + 0.82 * diffuse)
                       + specular * 0.55
                       + rim * vec3(0.25, 0.45, 1.0);

            fragColor = vec4(pow(max(color, 0.0), vec3(1.0 / 2.2)), 1.0);
        }
        """;

    public void Initialize()
    {
        (float[] vertices, uint[] indices) = BuildCube();
        IndexCount = indices.Length;

        _vertices = GlBuffer.Create();
        _vertices.Allocate(vertices.AsSpan());

        float[] instanceData = BuildInstanceMatrices(7);
        InstanceCount = instanceData.Length / 16;
        _instances = GlBuffer.Create();
        _instances.Allocate(instanceData.AsSpan());

        _vao = GlVertexArray.Create();
        _vao.Bind();

        _vertices.Bind();
        GlVertexArray.Attribute(0, 3, VertexAttribType.Float, false, 24, 0);
        GlVertexArray.Attribute(1, 3, VertexAttribType.Float, false, 24, 12);

        _instances.Bind();
        for (int row = 0; row < 4; row++)
        {
            GlVertexArray.Attribute(2 + row, 4, VertexAttribType.Float, false, 64, row * 16);
            GlVertexArray.Divisor(2 + row, 1);
        }

        // 索引缓冲的绑定属于 VAO 状态，必须在 VAO 绑定时创建并绑定
        _indices = GlBuffer.Create(BufferTarget.ElementArrayBuffer);
        _indices.Allocate(indices.AsSpan());

        _program = GlProgram.FromSource(VertexShader, FragmentShader);

        GlState.Enable(GlCapability.DepthTest);
        GlState.Enable(GlCapability.CullFace);
        GlState.DepthFunc(Byta.DepthFunc.Less);
    }

    public void Render(float time)
    {
        GlState.Viewport(0, 0, Width, Height);
        GlDraw.ClearColor(0.02f, 0.03f, 0.05f, 1f);
        GlDraw.Clear(ClearMask.Color | ClearMask.Depth);

        float angle = time * 0.18f;
        var eye = new Vector3(MathF.Cos(angle) * 46f, 27f, MathF.Sin(angle) * 46f);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(0f, -1f, 0f), Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.6f, (float)Width / Height, 0.5f, 400f);
        var viewProjection = view * projection;

        _program.Use();
        // Matrix4x4 是行主序，GL 要列主序 → transpose: true（后端模式下由 GlUniform 自己转置）
        _lastViewProjection = viewProjection;
        _program.GetUniform("uViewProjection").Set(viewProjection, transpose: true);
        _program.GetUniform("uCameraPosition").Set(eye);
        _program.GetUniform("uTime").Set(time);

        _vao.Bind();

        // 诊断开关：只画第一个三角形的 3 个顶点（绕开索引缓冲与实例化）。
        // 如果这样画面正常，说明问题在 EBO 或实例属性；否则在顶点/矩阵。
        if (DiagnosticSingleTriangle)
        {
            GlDraw.Arrays(PrimitiveType.Triangles, 0, 3);
        }
        else
        {
            GlDraw.ElementsInstanced(
                PrimitiveType.Triangles, IndexCount, IndexType.UnsignedInt, 0, InstanceCount);
        }
    }

    /// <summary>uniform 位置诊断：location = -1 说明名字没对上或程序没链接好。</summary>
    public string UniformReport()
    {
        int viewProjection = _program.GetUniform("uViewProjection").Location;
        int camera = _program.GetUniform("uCameraPosition").Location;
        int time = _program.GetUniform("uTime").Location;
        return $"uViewProjection={viewProjection}, uCameraPosition={camera}, uTime={time}, programId={_program.Id}";
    }

    /// <summary>回读 uViewProjection，和上传的值逐元素对比。</summary>
    public string MatrixProbe()
    {
        var uniform = _program.GetUniform("uViewProjection");
        var actual = BytaBrowserRuntime.ReadMatrixUniform((uint)uniform.Program, uniform.Location);
        if (actual is null)
            return "回读失败（null）";

        var expected = _lastViewProjection;
        // GL 侧存的是转置后的矩阵，所以对角线仍然对应 M11/M22/M33/M44
        double d0 = Math.Abs(actual[0] - expected.M11);
        double d5 = Math.Abs(actual[5] - expected.M22);
        double d10 = Math.Abs(actual[10] - expected.M33);
        double d15 = Math.Abs(actual[15] - expected.M44);

        bool identity = Math.Abs(actual[0] - 1) < 1e-6 && Math.Abs(actual[5] - 1) < 1e-6
                        && Math.Abs(actual[10] - 1) < 1e-6 && Math.Abs(actual[15] - 1) < 1e-6;

        return $"回读{(identity ? "=单位矩阵（上传没生效）" : "成功")}；" +
               $"a0={actual[0]:F4}(期望{expected.M11:F4}) a5={actual[5]:F4}(期望{expected.M22:F4}) " +
               $"a15={actual[15]:F4}(期望{expected.M44:F4}) 偏差={Math.Max(Math.Max(d0, d5), Math.Max(d10, d15)):E2}";
    }

    /// <summary>渲染一帧后读 GL 错误（WebGL 没有 KHR_debug，只能轮询 glGetError）。</summary>
    public string ErrorReport()
    {
        uint first = GlApi.GetError();
        uint second = GlApi.GetError();
        return $"glGetError: 0x{first:X4} / 0x{second:X4}（0 表示无错）";
    }

    /// <summary>从默认帧缓冲读中心像素，用来验证绘制真的落盘了。</summary>
    public string ReadCenterPixel()
    {
        var pixels = new byte[4];
        fixed (byte* p = pixels)
            GlApi.ReadPixels(Width / 2, Height / 2, 1, 1, GL.RGBA, GL.UNSIGNED_BYTE, p);
        return $"中心像素 RGBA = ({pixels[0]}, {pixels[1]}, {pixels[2]}, {pixels[3]})";
    }

    public void Dispose()
    {
        _program.Dispose();
        _vao.Dispose();
        _vertices.Dispose();
        _indices.Dispose();
        _instances.Dispose();
    }

    private static (float[] Vertices, uint[] Indices) BuildCube()
    {
        // 用叉积保证每个面的绕序一致（逆时针 = 正面），否则背面剔除会把该画的剔掉
        (Vector3 Normal, Vector3[] Corners)[] faces =
        [
            (new(0, 0, 1),  [new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)]),
            (new(0, 0, -1), [new(1, -1, -1), new(-1, -1, -1), new(-1, 1, -1), new(1, 1, -1)]),
            (new(1, 0, 0),  [new(1, -1, 1), new(1, -1, -1), new(1, 1, -1), new(1, 1, 1)]),
            (new(-1, 0, 0), [new(-1, -1, -1), new(-1, -1, 1), new(-1, 1, 1), new(-1, 1, -1)]),
            (new(0, 1, 0),  [new(-1, 1, 1), new(1, 1, 1), new(1, 1, -1), new(-1, 1, -1)]),
            (new(0, -1, 0), [new(-1, -1, -1), new(1, -1, -1), new(1, -1, 1), new(-1, -1, 1)]),
        ];

        var vertices = new float[24 * 6];
        var indices = new uint[36];

        int v = 0, i = 0;
        for (int face = 0; face < faces.Length; face++)
        {
            var (normal, corners) = faces[face];
            uint baseIndex = (uint)(face * 4);

            foreach (var corner in corners)
            {
                vertices[v++] = corner.X * 0.5f;
                vertices[v++] = corner.Y * 0.5f;
                vertices[v++] = corner.Z * 0.5f;
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

    private static float[] BuildInstanceMatrices(int count)
    {
        var data = new float[count * count * 16];
        int index = 0;
        float spacing = 3.05f;

        for (int z = 0; z < count; z++)
        {
            for (int x = 0; x < count; x++)
            {
                float phase = x * 0.7f + z * 1.3f;
                float scale = 2.30f + 0.10f * MathF.Sin(phase * 1.7f);
                var position = new Vector3(
                    (x - (count - 1) / 2f) * spacing,
                    0f,
                    (z - (count - 1) / 2f) * spacing);

                var model = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);

                // 实例矩阵走的是**顶点属性**，无法像 uniform 那样靠 transpose 参数纠正。
                // 着色器里 mat4(aModel0..3) 是按列组装的，所以这里必须给列主序 —— 否则
                // 上传的是转置后的矩阵，立方体会被拉爆、相机看起来像站在它内部。
                var columnMajor = Matrix4x4.Transpose(model);
                data[index++] = columnMajor.M11; data[index++] = columnMajor.M12; data[index++] = columnMajor.M13; data[index++] = columnMajor.M14;
                data[index++] = columnMajor.M21; data[index++] = columnMajor.M22; data[index++] = columnMajor.M23; data[index++] = columnMajor.M24;
                data[index++] = columnMajor.M31; data[index++] = columnMajor.M32; data[index++] = columnMajor.M33; data[index++] = columnMajor.M34;
                data[index++] = columnMajor.M41; data[index++] = columnMajor.M42; data[index++] = columnMajor.M43; data[index++] = columnMajor.M44;
            }
        }

        return data;
    }
}
