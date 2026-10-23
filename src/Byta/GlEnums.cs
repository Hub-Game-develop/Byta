// SPDX-License-Identifier: MIT
using Byta.Bindings;

namespace Byta;

// 这些枚举全部直接映射到 Khronos 常量，编译期常量折叠，运行时零开销。
// 需要生成表里没有的常量时直接写 GL.XXX。

public enum BufferTarget : uint
{
    ArrayBuffer = GL.ARRAY_BUFFER,
    AtomicCounterBuffer = GL.ATOMIC_COUNTER_BUFFER,
    CopyReadBuffer = GL.COPY_READ_BUFFER,
    CopyWriteBuffer = GL.COPY_WRITE_BUFFER,
    DispatchIndirectBuffer = GL.DISPATCH_INDIRECT_BUFFER,
    DrawIndirectBuffer = GL.DRAW_INDIRECT_BUFFER,
    ElementArrayBuffer = GL.ELEMENT_ARRAY_BUFFER,
    PixelPackBuffer = GL.PIXEL_PACK_BUFFER,
    PixelUnpackBuffer = GL.PIXEL_UNPACK_BUFFER,
    QueryBuffer = GL.QUERY_BUFFER,
    ShaderStorageBuffer = GL.SHADER_STORAGE_BUFFER,
    TextureBuffer = GL.TEXTURE_BUFFER,
    TransformFeedbackBuffer = GL.TRANSFORM_FEEDBACK_BUFFER,
    UniformBuffer = GL.UNIFORM_BUFFER,
}

public enum BufferUsage : uint
{
    StreamDraw = GL.STREAM_DRAW,
    StreamRead = GL.STREAM_READ,
    StreamCopy = GL.STREAM_COPY,
    StaticDraw = GL.STATIC_DRAW,
    StaticRead = GL.STATIC_READ,
    StaticCopy = GL.STATIC_COPY,
    DynamicDraw = GL.DYNAMIC_DRAW,
    DynamicRead = GL.DYNAMIC_READ,
    DynamicCopy = GL.DYNAMIC_COPY,
}

public enum MapAccess : uint
{
    ReadOnly = GL.READ_ONLY,
    WriteOnly = GL.WRITE_ONLY,
    ReadWrite = GL.READ_WRITE,
}

public enum PrimitiveType : uint
{
    Points = GL.POINTS,
    Lines = GL.LINES,
    LineLoop = GL.LINE_LOOP,
    LineStrip = GL.LINE_STRIP,
    Triangles = GL.TRIANGLES,
    TriangleStrip = GL.TRIANGLE_STRIP,
    TriangleFan = GL.TRIANGLE_FAN,
    LinesAdjacency = GL.LINES_ADJACENCY,
    TrianglesAdjacency = GL.TRIANGLES_ADJACENCY,
    Patches = GL.PATCHES,
}

public enum IndexType : uint
{
    UnsignedByte = GL.UNSIGNED_BYTE,
    UnsignedShort = GL.UNSIGNED_SHORT,
    UnsignedInt = GL.UNSIGNED_INT,
}

public enum VertexAttribType : uint
{
    Byte = GL.BYTE,
    UnsignedByte = GL.UNSIGNED_BYTE,
    Short = GL.SHORT,
    UnsignedShort = GL.UNSIGNED_SHORT,
    Int = GL.INT,
    UnsignedInt = GL.UNSIGNED_INT,
    HalfFloat = GL.HALF_FLOAT,
    Float = GL.FLOAT,
    Double = GL.DOUBLE,
    UnsignedInt2101010Rev = GL.UNSIGNED_INT_2_10_10_10_REV,
    Int2101010Rev = GL.INT_2_10_10_10_REV,
}

public enum GlShaderType : uint
{
    Vertex = GL.VERTEX_SHADER,
    Fragment = GL.FRAGMENT_SHADER,
    Geometry = GL.GEOMETRY_SHADER,
    TessControl = GL.TESS_CONTROL_SHADER,
    TessEvaluation = GL.TESS_EVALUATION_SHADER,
    Compute = GL.COMPUTE_SHADER,
}

public enum GlCapability : byte
{
    Blend = 0,
    DepthTest,
    CullFace,
    ScissorTest,
    FramebufferSrgb,
    Multisample,
    StencilTest,
    PolygonOffsetFill,
    PrimitiveRestart,
    RasterizerDiscard,
    DepthClamp,
    SampleAlphaToCoverage,
    SampleCoverage,
    Dither,
}

public enum BlendFactor : uint
{
    Zero = GL.ZERO,
    One = GL.ONE,
    SrcAlpha = GL.SRC_ALPHA,
    OneMinusSrcAlpha = GL.ONE_MINUS_SRC_ALPHA,
    DstAlpha = GL.DST_ALPHA,
    OneMinusDstAlpha = GL.ONE_MINUS_DST_ALPHA,
    SrcColor = GL.SRC_COLOR,
    OneMinusSrcColor = GL.ONE_MINUS_SRC_COLOR,
    DstColor = GL.DST_COLOR,
    OneMinusDstColor = GL.ONE_MINUS_DST_COLOR,
}

public enum DepthFunc : uint
{
    Never = GL.NEVER,
    Less = GL.LESS,
    Equal = GL.EQUAL,
    LessEqual = GL.LEQUAL,
    Greater = GL.GREATER,
    NotEqual = GL.NOTEQUAL,
    GreaterEqual = GL.GEQUAL,
    Always = GL.ALWAYS,
}

public enum FaceCulling : uint
{
    Front = GL.FRONT,
    Back = GL.BACK,
    FrontAndBack = GL.FRONT_AND_BACK,
}

public enum ClearMask : uint
{
    Color = GL.COLOR_BUFFER_BIT,
    Depth = GL.DEPTH_BUFFER_BIT,
    Stencil = GL.STENCIL_BUFFER_BIT,
    ColorDepth = GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT,
    All = GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT | GL.STENCIL_BUFFER_BIT,
}

public enum PixelFormat : uint
{
    Red = GL.RED,
    Rg = GL.RG,
    Rgb = GL.RGB,
    Rgba = GL.RGBA,
    Bgra = GL.BGRA,
    DepthComponent = GL.DEPTH_COMPONENT,
    DepthStencil = GL.DEPTH_STENCIL,
    RedInteger = GL.RED_INTEGER,
    RgbaInteger = GL.RGBA_INTEGER,
}

public enum PixelType : uint
{
    UnsignedByte = GL.UNSIGNED_BYTE,
    Byte = GL.BYTE,
    UnsignedShort = GL.UNSIGNED_SHORT,
    Short = GL.SHORT,
    UnsignedInt = GL.UNSIGNED_INT,
    Int = GL.INT,
    Float = GL.FLOAT,
    UnsignedInt8888Rev = GL.UNSIGNED_INT_8_8_8_8_REV,
}

public enum TextureFilter : uint
{
    Nearest = GL.NEAREST,
    Linear = GL.LINEAR,
    NearestMipmapNearest = GL.NEAREST_MIPMAP_NEAREST,
    LinearMipmapNearest = GL.LINEAR_MIPMAP_NEAREST,
    NearestMipmapLinear = GL.NEAREST_MIPMAP_LINEAR,
    LinearMipmapLinear = GL.LINEAR_MIPMAP_LINEAR,
}

public enum TextureWrap : uint
{
    Repeat = GL.REPEAT,
    MirroredRepeat = GL.MIRRORED_REPEAT,
    ClampToEdge = GL.CLAMP_TO_EDGE,
    ClampToBorder = GL.CLAMP_TO_BORDER,
}

public enum FramebufferTarget : uint
{
    Read = GL.READ_FRAMEBUFFER,
    Draw = GL.DRAW_FRAMEBUFFER,
    Both = GL.FRAMEBUFFER,
}

public enum FramebufferAttachment : uint
{
    Color0 = GL.COLOR_ATTACHMENT0,
    Color1 = GL.COLOR_ATTACHMENT1,
    Color2 = GL.COLOR_ATTACHMENT2,
    Color3 = GL.COLOR_ATTACHMENT3,
    Depth = GL.DEPTH_ATTACHMENT,
    Stencil = GL.STENCIL_ATTACHMENT,
    DepthStencil = GL.DEPTH_STENCIL_ATTACHMENT,
}

public enum FramebufferStatus : uint
{
    Complete = GL.FRAMEBUFFER_COMPLETE,
    Undefined = GL.FRAMEBUFFER_UNDEFINED,
    IncompleteAttachment = GL.FRAMEBUFFER_INCOMPLETE_ATTACHMENT,
    IncompleteMissingAttachment = GL.FRAMEBUFFER_INCOMPLETE_MISSING_ATTACHMENT,
    IncompleteDrawBuffer = GL.FRAMEBUFFER_INCOMPLETE_DRAW_BUFFER,
    IncompleteReadBuffer = GL.FRAMEBUFFER_INCOMPLETE_READ_BUFFER,
    Unsupported = GL.FRAMEBUFFER_UNSUPPORTED,
    IncompleteMultisample = GL.FRAMEBUFFER_INCOMPLETE_MULTISAMPLE,
    IncompleteLayerTargets = GL.FRAMEBUFFER_INCOMPLETE_LAYER_TARGETS,
}

public enum QueryTarget : uint
{
    TimeElapsed = GL.TIME_ELAPSED,
    SamplesPassed = GL.SAMPLES_PASSED,
    PrimitivesGenerated = GL.PRIMITIVES_GENERATED,
    AnySamplesPassed = GL.ANY_SAMPLES_PASSED,
}

/// <summary>
/// 纹理格式：把 internalFormat / format / type 这三元组收敛成一个枚举，
/// 省掉每次上传时手写三个容易写错的 GLenum。
/// </summary>
public enum TextureFormat
{
    R8,
    R16F,
    R32F,
    Rg8,
    Rg16F,
    Rgba8,
    Rgba16,
    Rgba16F,
    Rgba32F,
    Rgb8,
    Srgb8Alpha8,
    Depth16,
    Depth24,
    Depth32F,
    Depth24Stencil8,
    R11G11B10F,
}

public enum GlSeverity : uint
{
    Notification = GL.DEBUG_SEVERITY_NOTIFICATION,
    Low = GL.DEBUG_SEVERITY_LOW,
    Medium = GL.DEBUG_SEVERITY_MEDIUM,
    High = GL.DEBUG_SEVERITY_HIGH,
}

public enum GlErrorCode : uint
{
    NoError = GL.NO_ERROR,
    InvalidEnum = GL.INVALID_ENUM,
    InvalidValue = GL.INVALID_VALUE,
    InvalidOperation = GL.INVALID_OPERATION,
    InvalidFramebufferOperation = GL.INVALID_FRAMEBUFFER_OPERATION,
    OutOfMemory = GL.OUT_OF_MEMORY,
}
