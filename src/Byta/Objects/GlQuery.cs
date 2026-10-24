// SPDX-License-Identifier: MIT
using Byta.Bindings;

namespace Byta;

/// <summary>GPU 查询对象，最常用的是测时（<c>GL_TIME_ELAPSED</c>）。</summary>
public readonly unsafe struct GlQuery : IDisposable
{
    public readonly uint Id;
    public readonly QueryTarget Target;

    private GlQuery(uint id, QueryTarget target)
    {
        Id = id;
        Target = target;
    }

    public bool IsValid => Id != 0;

    public static GlQuery Create(QueryTarget target = QueryTarget.TimeElapsed)
    {
        if (!GlApi.HasGenQueries)
            throw new GlNotSupportedException("glGenQueries", "需要 OpenGL 1.5+");

        uint id;
        GlApi.GenQueries(1, &id);
        return new GlQuery(id, target);
    }

    public void Begin() => GlApi.BeginQuery((uint)Target, Id);

    public void End() => GlApi.EndQuery((uint)Target);

    /// <summary>结果是否已经可用（非阻塞）。</summary>
    public bool IsResultAvailable()
    {
        uint available = 0;
        GlApi.GetQueryObjectuiv(Id, GL.QUERY_RESULT_AVAILABLE, &available);
        return available != 0;
    }

    /// <summary>取结果。<paramref name="wait"/> = true 时阻塞等 GPU，false 且结果没好则返回 0。</summary>
    public ulong GetResult(bool wait = true)
    {
        uint ready = 0;
        GlApi.GetQueryObjectuiv(Id, GL.QUERY_RESULT_AVAILABLE, &ready);
        if (ready == 0)
        {
            if (!wait)
                return 0;
            while (ready == 0)
                GlApi.GetQueryObjectuiv(Id, GL.QUERY_RESULT_AVAILABLE, &ready);
        }

        uint value = 0;
        GlApi.GetQueryObjectuiv(Id, GL.QUERY_RESULT, &value);
        return value;
    }

    /// <summary>耗时（毫秒），仅对 <see cref="QueryTarget.TimeElapsed"/> 有意义。</summary>
    public double GetElapsedMilliseconds(bool wait = true) => GetResult(wait) / 1_000_000.0;

    public void Dispose()
    {
        if (Id == 0)
            return;
        uint id = Id;
        GlApi.DeleteQueries(1, &id);
    }
}
