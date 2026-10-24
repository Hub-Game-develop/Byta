// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Browser;

namespace Byta.Docs.Browser;

internal static class Program
{
    private static Task Main(string[] args) =>
        BuildAvaloniaApp().WithInterFont().StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Byta.Docs.App>();
}
