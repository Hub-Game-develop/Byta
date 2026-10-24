// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Byta.Docs.Views;

namespace Byta.Docs;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        switch (ApplicationLifetime)
        {
            // 桌面：普通窗口
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new DocsWindow();
                break;

            // 浏览器（Avalonia.Browser）：单视图挂到 #out 上
            case ISingleViewApplicationLifetime single:
                single.MainView = new DocsView();
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
