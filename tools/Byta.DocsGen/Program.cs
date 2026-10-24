// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Byta.DocsGen;

/// <summary>
/// 从编译产物提取 API 模型：反射拿签名，XML 文档拿摘要，输出 api.json 供文档站消费。
///
/// 为什么不用运行时反射直接渲染：浏览器里读不到随包分发的 XML 文件（虚拟文件系统
/// 不保证包含它）。在构建期把它烘焙成 JSON，Avalonia 的 AssetLoader 就能可靠地
/// 从程序集里读出来 —— 桌面和 WASM 走同一条路。
/// </summary>
internal static class Program
{
    /// <summary>成员数超过这个数的类型只列摘要，不列明细（GL 有 2651 个命令字段）。</summary>
    private const int MaxMembersPerType = 160;

    private static int Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine("docs", "Byta.Docs", "Assets", "api.json");

        Assembly[] assemblies =
        [
            typeof(Byta.BytaRuntime).Assembly,
            typeof(Byta.Bindings.GL).Assembly,
            typeof(Byta.Browser.BytaBrowserRuntime).Assembly,
        ];

        var comments = new DocComments();
        foreach (var assembly in assemblies)
        {
            string xmlPath = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.xml");
            comments.Load(xmlPath);
        }

        var root = new ApiRoot
        {
            GeneratedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            Assemblies = [.. assemblies.Select(a => BuildAssembly(a, comments))],
        };

        string fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(root, options));

        int typeCount = root.Assemblies.Sum(a => a.Types);
        int memberCount = root.Assemblies.SelectMany(a => a.Namespaces)
                                        .SelectMany(n => n.Types)
                                        .Sum(t => t.Members.Length);

        Console.WriteLine($"[docsgen] {fullPath}");
        Console.WriteLine($"[docsgen] {root.Assemblies.Length} 个程序集 / {typeCount} 个类型 / {memberCount} 个成员");
        Console.WriteLine($"[docsgen] {new FileInfo(fullPath).Length / 1024} KB");
        return 0;
    }

    private static ApiAssembly BuildAssembly(Assembly assembly, DocComments comments)
    {
        var types = assembly.GetExportedTypes()
            .Where(t => !t.IsNested && !IsCompilerGenerated(t))
            .OrderBy(t => t.Namespace, StringComparer.Ordinal)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var namespaces = types
            .GroupBy(t => t.Namespace ?? "(global)")
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ApiNamespace
            {
                Name = g.Key,
                Types = [.. g.Select(t => BuildType(t, comments))],
            })
            .ToArray();

        return new ApiAssembly
        {
            Name = assembly.GetName().Name ?? "?",
            Version = assembly.GetName().Version?.ToString(3) ?? "?",
            Types = types.Length,
            Namespaces = namespaces,
        };
    }

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.StartsWith('<') || type.Name.StartsWith("__") || type.IsSpecialName;

    private static ApiType BuildType(Type type, DocComments comments)
    {
        var members = CollectMembers(type).ToArray();
        bool truncated = members.Length > MaxMembersPerType;

        return new ApiType
        {
            Name = DisplayName(type),
            FullName = (type.FullName ?? type.Name).Replace('+', '.'),
            Kind = KindOf(type),
            Summary = comments.For("T", type.FullName ?? type.Name),
            IsStatic = type.IsAbstract && type.IsSealed,
            IsTruncated = truncated,
            MemberCount = members.Length,
            Members = truncated ? [] : [.. members.Select(m => BuildMember(m, comments))],
        };
    }

    private static IEnumerable<MemberInfo> CollectMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance
                                   | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var ctor in type.GetConstructors(flags))
            yield return ctor;

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)     // 属性访问器、运算符
                continue;
            yield return method;
        }

        foreach (var property in type.GetProperties(flags))
            yield return property;

        foreach (var field in type.GetFields(flags))
        {
            if (field.IsSpecialName)
                continue;
            yield return field;
        }
    }

    private static ApiMember BuildMember(MemberInfo member, DocComments comments)
    {
        switch (member)
        {
            case ConstructorInfo ctor:
                return new ApiMember
                {
                    Kind = "constructor",
                    Name = DisplayName(ctor.DeclaringType!),
                    Signature = $"{DisplayName(ctor.DeclaringType!)}({Parameters(ctor.GetParameters())})",
                    ReturnType = null,
                    Modifiers = ctor.IsStatic ? "static" : "",
                    Summary = comments.For("M", DocId(ctor)),
                };

            case MethodInfo method:
                return new ApiMember
                {
                    Kind = "method",
                    Name = method.Name,
                    Signature = $"{TypeName(method.ReturnType)} {method.Name}({Parameters(method.GetParameters())})",
                    ReturnType = TypeName(method.ReturnType),
                    Modifiers = method.IsStatic ? "static" : "",
                    Summary = comments.For("M", DocId(method)),
                };

            case PropertyInfo property:
                return new ApiMember
                {
                    Kind = "property",
                    Name = property.Name,
                    Signature = $"{TypeName(property.PropertyType)} {property.Name} {{ {((property.CanRead) ? "get; " : "")}{((property.CanWrite) ? "set; " : "")}}}",
                    ReturnType = TypeName(property.PropertyType),
                    Modifiers = (property.GetMethod ?? property.SetMethod)?.IsStatic == true ? "static" : "",
                    Summary = comments.For("P", DocId(property)),
                };

            case FieldInfo field:
                return new ApiMember
                {
                    Kind = "field",
                    Name = field.Name,
                    Signature = $"{field.FieldType.Name} {field.Name}",
                    ReturnType = field.FieldType.Name,
                    Modifiers = field.IsStatic ? "static" : "",
                    Summary = comments.For("F", DocId(field)),
                };

            default:
                return new ApiMember
                {
                    Kind = "member",
                    Name = member.Name,
                    Signature = member.Name,
                    Summary = "",
                    Modifiers = "",
                };
        }
    }

    private static string DocId(MemberInfo member) =>
        member switch
        {
            ConstructorInfo ctor => $"{ctor.DeclaringType?.FullName}.#ctor",
            MethodInfo method => $"{method.DeclaringType?.FullName}.{method.Name}",
            PropertyInfo property => $"{property.DeclaringType?.FullName}.{property.Name}",
            FieldInfo field => $"{field.DeclaringType?.FullName}.{field.Name}",
            _ => member.Name,
        };

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{TypeName(p.ParameterType)} {p.Name}"));

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!) + "&";
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType)
            return type.Name switch
            {
                "Int32" => "int", "UInt32" => "uint", "Int64" => "long", "UInt64" => "ulong",
                "Single" => "float", "Double" => "double", "Boolean" => "bool", "Byte" => "byte",
                "SByte" => "sbyte", "Int16" => "short", "UInt16" => "ushort", "Char" => "char",
                "String" => "string", "Object" => "object", "Void" => "void", "IntPtr" => "nint",
                "UIntPtr" => "nuint",
                _ => type.Name,
            };

        string name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static string DisplayName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;
        string name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(TypeName))}>";
    }

    private static string KindOf(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";
        if (type.IsValueType) return "struct";
        if (type.IsAbstract && type.IsSealed) return "static class";
        if (type.IsAbstract) return "abstract class";
        return "class";
    }
}

/// <summary>读取 XML 文档注释。带容错：找不到精确 ID 时按 "类型.成员名" 前缀回退（重载共享摘要）。</summary>
internal sealed class DocComments
{
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byPrefix = new(StringComparer.Ordinal);

    public void Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[docsgen] 跳过（不存在）：{Path.GetFileName(path)}");
            return;
        }

        var document = new System.Xml.XmlDocument();
        document.Load(path);

        var nodes = document.SelectNodes("/doc/members/member");
        if (nodes is null)
            return;

        foreach (System.Xml.XmlNode node in nodes)
        {
            string? name = node.Attributes?["name"]?.Value;
            string? summary = node["summary"]?.InnerText;
            if (name is null || string.IsNullOrWhiteSpace(summary))
                continue;

            string cleaned = Clean(summary);
            _byId[name] = cleaned;

            // "M:Byta.GlBuffer.Create(System.Int32)" → 前缀 "M:Byta.GlBuffer.Create"
            int paren = name.IndexOf('(');
            string prefix = paren > 0 ? name[..paren] : name;
            _byPrefix.TryAdd(prefix, cleaned);
        }
    }

    public string For(string kind, string id)
    {
        string full = $"{kind}:{id}";
        if (_byId.TryGetValue(full, out string? exact))
            return exact;

        string prefix = full;
        int paren = prefix.IndexOf('(');
        if (paren > 0)
            prefix = prefix[..paren];

        return _byPrefix.TryGetValue(prefix, out string? fuzzy) ? fuzzy : "";
    }

    private static string Clean(string text) =>
        string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

internal sealed class ApiRoot
{
    public string GeneratedAt { get; set; } = "";
    public ApiAssembly[] Assemblies { get; set; } = [];
}

internal sealed class ApiAssembly
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int Types { get; set; }
    public ApiNamespace[] Namespaces { get; set; } = [];
}

internal sealed class ApiNamespace
{
    public string Name { get; set; } = "";
    public ApiType[] Types { get; set; } = [];
}

internal sealed class ApiType
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsTruncated { get; set; }
    public int MemberCount { get; set; }
    public ApiMember[] Members { get; set; } = [];
}

internal sealed class ApiMember
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Signature { get; set; } = "";
    public string? ReturnType { get; set; }
    public string Modifiers { get; set; } = "";
    public string Summary { get; set; } = "";
}
