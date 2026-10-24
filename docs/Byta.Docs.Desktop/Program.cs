// SPDX-License-Identifier: MIT
using Avalonia;

namespace Byta.Docs.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Byta.Docs.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
