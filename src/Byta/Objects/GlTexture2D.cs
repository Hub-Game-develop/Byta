using Byta.Bindings;

namespace Byta;

/// <summary>2D 纹理。</summary>
public readonly unsafe struct GlTexture2D : IDisposable
{
    public readonly uint Id;

    private GlTexture2D(uint id) => Id = id;

    public bool IsValid => Id != 0;

    public static GlTexture2D Create()
    {
        if (GL.GenTextures is null)
            throw new GlNotSupportedException("glGenTextures", "需要 OpenGL 1.1+");

        uint id;
        GL.GenTextures(1, &id);
        if (id == 0)
            throw new GlException("glGenTextures 返回了 0 号对象");
        return new GlTexture2D(id);
    }

    /// <summary>绑定到指定纹理单元（走状态缓存，重复绑定会被吃掉）。</summary>
    public void Bind(uint unit = 0) => GlState.BindTexture2D(unit, Id);

    /// <summary>
    /// 分配存储（用 <c>glTexImage2D</c> 而不是 <c>glTexStorage2D</c>：
    /// 后者是 GL 4.2，macOS 的 4.1 上限用不了）。
    /// </summary>
    public void Allocate(TextureFormat format, int width, int height, int levels = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(levels, 1);

        var (internalFormat, pixelFormat, pixelType) = TextureFormats.Describe(format);
        Bind();

        int w = width, h = height;
        for (int level = 0; level < levels; level++)
        {
            GL.TexImage2D(GL.TEXTURE_2D, level, (int)internalFormat, w, h, 0, pixelFormat, pixelType, null);
            w = Math.Max(1, w >> 1);
            h = Math.Max(1, h >> 1);
        }
    }

    /// <summary>上传像素。<paramref name="pixels"/> 的字节数必须正好是 width*height*像素字节数。</summary>
    public void Upload(TextureFormat format, int width, int height, ReadOnlySpan<byte> pixels, int level = 0)
    {
        var (internalFormat, pixelFormat, pixelType) = TextureFormats.Describe(format);
        int bytesPerPixel = TextureFormats.BytesPerPixel(format);
        int expected = width * height * bytesPerPixel;
        if (pixels.Length < expected)
            throw new ArgumentException($"像素数据不足：需要 {expected} 字节，实际 {pixels.Length} 字节。", nameof(pixels));

        Bind();
        GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            GL.TexImage2D(GL.TEXTURE_2D, level, (int)internalFormat, width, height, 0, pixelFormat, pixelType, p);
    }

    public void Upload<T>(TextureFormat format, int width, int height, ReadOnlySpan<T> pixels, int level = 0)
        where T : unmanaged
        => Upload(format, width, height, System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels), level);

    /// <summary>只更新子区域（比整张重传省带宽）。</summary>
    public void UploadRegion(
        TextureFormat format, int x, int y, int width, int height,
        ReadOnlySpan<byte> pixels, int level = 0)
    {
        var (_, pixelFormat, pixelType) = TextureFormats.Describe(format);
        int expected = width * height * TextureFormats.BytesPerPixel(format);
        if (pixels.Length < expected)
            throw new ArgumentException($"像素数据不足：需要 {expected} 字节，实际 {pixels.Length} 字节。", nameof(pixels));

        Bind();
        GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            GL.TexSubImage2D(GL.TEXTURE_2D, level, x, y, width, height, pixelFormat, pixelType, p);
    }

    public void SetFilters(TextureFilter minification, TextureFilter magnification)
    {
        Bind();
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)minification);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)magnification);
    }

    public void SetWrap(TextureWrap s, TextureWrap t)
    {
        Bind();
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)s);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)t);
    }

    public void GenerateMipmaps()
    {
        Bind();
        GL.GenerateMipmap(GL.TEXTURE_2D);
    }

    public void Dispose()
    {
        if (Id == 0)
            return;
        // 贴图删除后驱动会自动把它从所有单元解绑，缓存必须一起失效
        GlState.InvalidateTextures();
        uint id = Id;
        GL.DeleteTextures(1, &id);
    }
}

/// <summary><see cref="TextureFormat"/> → GL 三元组（internalFormat / format / type）。</summary>
public static class TextureFormats
{
    public static (uint InternalFormat, uint PixelFormat, uint PixelType) Describe(TextureFormat format) => format switch
    {
        TextureFormat.R8 => (GL.R8, GL.RED, GL.UNSIGNED_BYTE),
        TextureFormat.R16F => (GL.R16F, GL.RED, GL.HALF_FLOAT),
        TextureFormat.R32F => (GL.R32F, GL.RED, GL.FLOAT),
        TextureFormat.Rg8 => (GL.RG8, GL.RG, GL.UNSIGNED_BYTE),
        TextureFormat.Rg16F => (GL.RG16F, GL.RG, GL.HALF_FLOAT),
        TextureFormat.Rgba8 => (GL.RGBA8, GL.RGBA, GL.UNSIGNED_BYTE),
        TextureFormat.Rgba16 => (GL.RGBA16, GL.RGBA, GL.UNSIGNED_SHORT),
        TextureFormat.Rgba16F => (GL.RGBA16F, GL.RGBA, GL.HALF_FLOAT),
        TextureFormat.Rgba32F => (GL.RGBA32F, GL.RGBA, GL.FLOAT),
        TextureFormat.Rgb8 => (GL.RGB8, GL.RGB, GL.UNSIGNED_BYTE),
        TextureFormat.Srgb8Alpha8 => (GL.SRGB8_ALPHA8, GL.RGBA, GL.UNSIGNED_BYTE),
        TextureFormat.Depth16 => (GL.DEPTH_COMPONENT16, GL.DEPTH_COMPONENT, GL.UNSIGNED_SHORT),
        TextureFormat.Depth24 => (GL.DEPTH_COMPONENT24, GL.DEPTH_COMPONENT, GL.UNSIGNED_INT),
        TextureFormat.Depth32F => (GL.DEPTH_COMPONENT32F, GL.DEPTH_COMPONENT, GL.FLOAT),
        TextureFormat.Depth24Stencil8 => (GL.DEPTH24_STENCIL8, GL.DEPTH_STENCIL, GL.UNSIGNED_INT_24_8),
        TextureFormat.R11G11B10F => (GL.R11F_G11F_B10F, GL.RGB, GL.FLOAT),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "未知纹理格式"),
    };

    /// <summary>像素格式的分量个数（回读时用来校验目标缓冲大小）。</summary>
    public static int PixelChannels(PixelFormat format) => format switch
    {
        PixelFormat.Red => 1,
        PixelFormat.RedInteger => 1,
        PixelFormat.Rg => 2,
        PixelFormat.Rgb => 3,
        PixelFormat.Rgba => 4,
        PixelFormat.Bgra => 4,
        PixelFormat.RgbaInteger => 4,
        PixelFormat.DepthComponent => 1,
        PixelFormat.DepthStencil => 1,
        _ => 4,
    };

    public static int BytesPerPixel(TextureFormat format) => format switch
    {        TextureFormat.R8 => 1,
        TextureFormat.R16F => 2,
        TextureFormat.R32F => 4,
        TextureFormat.Rg8 => 2,
        TextureFormat.Rg16F => 4,
        TextureFormat.Rgba8 => 4,
        TextureFormat.Rgba16 => 8,
        TextureFormat.Rgba16F => 8,
        TextureFormat.Rgba32F => 16,
        TextureFormat.Rgb8 => 3,
        TextureFormat.Srgb8Alpha8 => 4,
        TextureFormat.Depth16 => 2,
        TextureFormat.Depth24 => 4,
        TextureFormat.Depth32F => 4,
        TextureFormat.Depth24Stencil8 => 4,
        TextureFormat.R11G11B10F => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "未知纹理格式"),
    };
}
