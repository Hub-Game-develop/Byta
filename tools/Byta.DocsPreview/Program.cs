// SPDX-License-Identifier: MIT
// 用 Avalonia 的 headless 平台 + Skia 把文档站渲染成 PNG，便于在没有 GUI 的环境里检查版式。
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Byta.Docs.Views;

string output = args.Length > 0 ? args[0] : "/tmp/docs-preview.png";
string? selectType = args.Length > 1 && args[1] != "-" ? args[1] : null;
string? query = args.Length > 2 ? args[2] : null;
int width = 1280, height = 860;

AppBuilder.Configure<Byta.Docs.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var window = new DocsWindow
{
    Width = width,
    Height = height,
};
window.Show();
Dispatcher.UIThread.RunJobs();

// 选中一个类型，让详情面板有内容
var view = window.Content as DocsView;
if (view is not null && query is not null)
    view.Search(query);
if (view is not null && selectType is not null)
    view.SelectTypeByName(selectType);

Dispatcher.UIThread.RunJobs();

var frame = window.CaptureRenderedFrame();
if (frame is null)
{
    Console.Error.WriteLine("渲染失败：CaptureRenderedFrame 返回 null");
    return 1;
}

frame.Save(output);
Console.WriteLine($"[preview] {output}");
return 0;
