// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Byta.Docs.Views;

public partial class DocsView : UserControl
{
    private ApiRepository? _repository;
    private List<ApiType> _visible = [];

    public DocsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_repository is not null)
            return;

        try
        {
            _repository = ApiRepository.Load();
        }
        catch (Exception error)
        {
            StatsBar.Severity = FluentAvalonia.UI.Controls.FAInfoBarSeverity.Error;
            StatsBar.Title = "无法加载 API 数据";
            StatsBar.Message = error.Message;
            return;
        }

        var root = _repository.Root;

        // 三个程序集版本号相同，去重后只显示一次
        var versions = root.Assemblies.Select(a => a.Version).Distinct().ToArray();
        VersionText.Text = versions.Length == 1 ? $"v{versions[0]}" : string.Join(" / ", versions.Select(v => $"v{v}"));

        // 只统计"真正列出来"的成员：GlApi/GL 那些几千条生成代码被折叠了，
        // 算进总数会给出一个没有意义的 14940。
        int listed = root.Assemblies
            .SelectMany(a => a.Namespaces)
            .SelectMany(n => n.Types)
            .Where(t => !t.IsTruncated)
            .Sum(t => t.MemberCount);

        StatsBar.Title = $"{root.TypeCount} 个公开类型 · {listed} 个可浏览成员";
        StatsBar.Message = $"{string.Join("、", root.Assemblies.Select(a => a.Name))}　生成于 {root.GeneratedAt}";

        ApplyFilter("");
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) =>
        ApplyFilter(SearchBox.Text ?? "");

    private void ApplyFilter(string query)
    {
        if (_repository is null)
            return;

        _visible = [.. _repository.Search(query)];
        TypeList.ItemsSource = _visible;
        ListHeader.Text = string.IsNullOrWhiteSpace(query)
            ? $"全部类型（{_visible.Count}）"
            : $"匹配 “{query.Trim()}”（{_visible.Count}）";

        if (_visible.Count == 0)
        {
            EmptyState.IsVisible = true;
            DetailPanel.IsVisible = false;
        }
    }

    /// <summary>按短名或完整名选中一个类型（无头预览与截图用）。</summary>
    public void SelectTypeByName(string name)
    {
        var match = _visible.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    ?? _visible.FirstOrDefault(t => t.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            TypeList.SelectedItem = match;
    }

    /// <summary>设置搜索词（无头预览用）。</summary>
    public void Search(string query)
    {
        SearchBox.Text = query;
        ApplyFilter(query);
    }

    private void OnTypeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (TypeList.SelectedItem is not ApiType type)
            return;

        EmptyState.IsVisible = false;
        DetailPanel.IsVisible = true;

        // Kind 本身可能就是 "static class"，再加前缀会变成 "static static class"
        TypeKind.Text = type.IsStatic && !type.Kind.StartsWith("static", StringComparison.Ordinal)
            ? "static " + type.Kind
            : type.Kind;
        TypeName.Text = type.Name;
        TypeFullName.Text = type.FullName;

        TypeSummary.Text = type.Summary;
        TypeSummary.IsVisible = type.HasSummary;

        TruncatedNote.IsVisible = type.IsTruncated;
        TruncatedText.Text = type.IsTruncated
            ? $"这是生成代码，共 {type.MemberCount} 个成员，这里只显示类型级摘要（逐条列出会淹没页面）。"
            : "";

        MembersHeader.Text = type.IsTruncated
            ? "成员（已折叠）"
            : $"成员（{type.MemberCount}）";

        MemberList.ItemsSource = type.Members;
    }
}
