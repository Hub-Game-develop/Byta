using Byta.Bindings;

namespace Byta;

/// <summary>帧缓冲对象（离屏渲染目标）。</summary>
public readonly unsafe struct GlFramebuffer : IDisposable
{
    public readonly uint Id;

    private GlFramebuffer(uint id) => Id = id;

    public bool IsValid => Id != 0;

    public static GlFramebuffer Create()
    {
        if (GL.GenFramebuffers is null)
            throw new GlNotSupportedException("glGenFramebuffers", "需要 OpenGL 3.0+ / FBO 扩展");

        uint id;
        GL.GenFramebuffers(1, &id);
        if (id == 0)
            throw new GlException("glGenFramebuffers 返回了 0 号对象");
        return new GlFramebuffer(id);
    }

    public void Bind(FramebufferTarget target = FramebufferTarget.Both) => GlState.BindFramebuffer(target, Id);

    public void AttachTexture(GlTexture2D texture, FramebufferAttachment attachment = FramebufferAttachment.Color0, int level = 0)
    {
        Bind(FramebufferTarget.Draw);
        GL.FramebufferTexture2D(GL.DRAW_FRAMEBUFFER, (uint)attachment, GL.TEXTURE_2D, texture.Id, level);
    }

    public void AttachRenderbuffer(GlRenderbuffer renderbuffer, FramebufferAttachment attachment)
    {
        Bind(FramebufferTarget.Draw);
        GL.FramebufferRenderbuffer(GL.DRAW_FRAMEBUFFER, (uint)attachment, GL.RENDERBUFFER, renderbuffer.Id);
    }

    /// <summary>多个颜色附件（MRT）时指定绘制目标。</summary>
    public void SetDrawBuffers(ReadOnlySpan<FramebufferAttachment> attachments)
    {
        Bind(FramebufferTarget.Draw);
        Span<uint> buffers = attachments.Length <= 8 ? stackalloc uint[attachments.Length] : new uint[attachments.Length];
        for (int i = 0; i < attachments.Length; i++)
            buffers[i] = (uint)attachments[i];

        fixed (uint* p = buffers)
            GL.DrawBuffers(buffers.Length, p);
    }

    public void SetReadBuffer(FramebufferAttachment attachment)
    {
        Bind(FramebufferTarget.Read);
        GL.ReadBuffer((uint)attachment);
    }

    public FramebufferStatus CheckStatus()
    {
        Bind(FramebufferTarget.Draw);
        return (FramebufferStatus)GL.CheckFramebufferStatus(GL.DRAW_FRAMEBUFFER);
    }

    /// <summary>状态不是 COMPLETE 就抛异常（带具体原因）。</summary>
    public void EnsureComplete()
    {
        var status = CheckStatus();
        if (status != FramebufferStatus.Complete)
            throw new GlException($"帧缓冲不完整：{status}（0x{(uint)status:X4}）");
    }

    /// <summary>回读像素到 <paramref name="destination"/>（自动设成紧凑打包）。</summary>
    public void ReadPixels<T>(
        int x, int y, int width, int height,
        PixelFormat format, PixelType type, Span<T> destination) where T : unmanaged
    {
        int channels = TextureFormats.PixelChannels(format);
        long requiredBytes = (long)width * height * channels * sizeof(T);
        long availableBytes = (long)destination.Length * sizeof(T);
        if (availableBytes < requiredBytes)
            throw new ArgumentException($"目标缓冲太小：需要 {requiredBytes} 字节，实际 {availableBytes} 字节。", nameof(destination));

        Bind(FramebufferTarget.Read);
        GL.PixelStorei(GL.PACK_ALIGNMENT, 1);
        fixed (T* p = destination)
            GL.ReadPixels(x, y, width, height, (uint)format, (uint)type, p);
    }

    public void Dispose()
    {
        if (Id == 0)
            return;
        GlState.BindFramebuffer(FramebufferTarget.Both, 0);
        uint id = Id;
        GL.DeleteFramebuffers(1, &id);
    }
}

/// <summary>渲染缓冲（通常用作深度/模板附件，比纹理便宜）。</summary>
public readonly unsafe struct GlRenderbuffer : IDisposable
{
    public readonly uint Id;

    private GlRenderbuffer(uint id) => Id = id;

    public bool IsValid => Id != 0;

    public static GlRenderbuffer Create()
    {
        if (GL.GenRenderbuffers is null)
            throw new GlNotSupportedException("glGenRenderbuffers", "需要 OpenGL 3.0+ / FBO 扩展");

        uint id;
        GL.GenRenderbuffers(1, &id);
        if (id == 0)
            throw new GlException("glGenRenderbuffers 返回了 0 号对象");
        return new GlRenderbuffer(id);
    }

    public void Bind() => GlState.BindRenderbuffer(Id);

    public void Allocate(TextureFormat format, int width, int height)
    {
        var (internalFormat, _, _) = TextureFormats.Describe(format);
        Bind();
        GL.RenderbufferStorage(GL.RENDERBUFFER, internalFormat, width, height);
    }

    public void Dispose()
    {
        if (Id == 0)
            return;
        GlState.BindRenderbuffer(0);
        uint id = Id;
        GL.DeleteRenderbuffers(1, &id);
    }
}
