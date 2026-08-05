using System.Windows;
using Soroe.Services;
using Soroe.ViewModels;
using Soroe.Views;

namespace Soroe;

/// <summary>
/// アプリケーションのエントリポイント。依存関係の組み立て（合成ルート）もここで行う。
/// </summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // StartupUri ではなくここで組み立てるのは、ViewModel に依存（IImageLoader）を注入するため。
        // DI コンテナを導入するほどの規模ではないので、実装の差し替え口だけを用意しておく。
        var window = new MainWindow
        {
            DataContext = new MainViewModel(new ImageRenderer(), new FolderPicker()),
        };
        window.Show();
    }
}
