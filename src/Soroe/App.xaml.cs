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
    private SettingsAutoSaver? _autoSaver;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 前回の設定を復元する。読めなければ既定値が返るので、ここでの失敗はありえない
        var store = new SettingsStore();
        var stored = store.Load();

        // StartupUri ではなくここで組み立てるのは、ViewModel に依存（IImageRenderer など）を注入するため。
        // DI コンテナを導入するほどの規模ではないので、実装の差し替え口だけを用意しておく。
        var renderer = new ImageRenderer();
        var window = new MainWindow
        {
            DataContext = new MainViewModel(
                stored.Processing,
                stored.Export,
                renderer,
                new ImageExporter(renderer),
                new FolderPicker(),
                new ExportDialogService()),
        };

        // 復元した設定の実体をそのまま監視する。ViewModel はこれを差し替えない
        _autoSaver = new SettingsAutoSaver(store, stored.Processing, stored.Export);

        window.Show();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 待機中の変更を取りこぼさないための締め。<b>ここだけに頼ってはいけない</b> —
    /// クラッシュや強制終了では呼ばれないため、普段の保存は
    /// <see cref="SettingsAutoSaver" /> が受け持つ。
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        // 保存に失敗しても例外は出ない（記録だけされる）ので、終了を妨げない
        _autoSaver?.SaveNow();
        _autoSaver?.Dispose();

        base.OnExit(e);
    }
}
