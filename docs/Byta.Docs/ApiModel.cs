// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Byta.Docs;

/// <summary>api.json 的根。由 <c>tools/Byta.DocsGen</c> 在构建期生成。</summary>
public sealed class ApiRoot
{
    public string GeneratedAt { get; set; } = "";
    public ApiAssembly[] Assemblies { get; set; } = [];

    [JsonIgnore]
    public int TypeCount => Assemblies.Sum(a => a.Types);

    [JsonIgnore]
    public int MemberCount => Assemblies.SelectMany(a => a.Namespaces)
                                        .SelectMany(n => n.Types)
                                        .Sum(t => t.MemberCount);
}

public sealed class ApiAssembly
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int Types { get; set; }
    public ApiNamespace[] Namespaces { get; set; } = [];

    public override string ToString() => $"{Name} v{Version}";
}

public sealed class ApiNamespace
{
    public string Name { get; set; } = "";
    public ApiType[] Types { get; set; } = [];
}

public sealed class ApiType
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsTruncated { get; set; }
    public int MemberCount { get; set; }
    public ApiMember[] Members { get; set; } = [];

    /// <summary>列表里显示的短描述（没有摘要时给出成员数）。</summary>
    public string Subtitle => IsTruncated
        ? $"{Kind} · {MemberCount} 个成员（生成代码，仅列摘要）"
        : $"{Kind} · {MemberCount} 个成员";

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public override string ToString() => FullName;
}

public sealed class ApiMember
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public string Modifiers { get; set; } = "";
    public string Summary { get; set; } = "";

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public string KindLabel => Kind switch
    {
        "method" => "方法",
        "constructor" => "构造",
        "property" => "属性",
        "field" => "字段",
        _ => Kind,
    };
}

/// <summary>加载打包在程序集里的 api.json，并提供过滤。</summary>
public sealed class ApiRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ApiRoot Root { get; }
    public List<ApiType> AllTypes { get; }

    private ApiRepository(ApiRoot root)
    {
        Root = root;
        AllTypes = [.. root.Assemblies
            .SelectMany(a => a.Namespaces)
            .SelectMany(n => n.Types)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];
    }

    public static ApiRepository Load()
    {
        using var stream = Avalonia.Platform.AssetLoader.Open(
            new Uri("avares://Byta.Docs/Assets/api.json"));

        var root = JsonSerializer.Deserialize<ApiRoot>(stream, Options)
                   ?? throw new InvalidOperationException("api.json 解析失败");

        return new ApiRepository(root);
    }

    /// <summary>按类型名 / 完整名 / 成员名过滤。空查询返回全部。</summary>
    public IEnumerable<ApiType> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AllTypes;

        string needle = query.Trim();
        return AllTypes.Where(type =>
            type.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || type.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || type.Members.Any(m => m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)));
    }
}
