using System.ComponentModel;
using System.Windows;
using Soroe.Common;
using Soroe.ViewModels;

namespace Soroe.Views;

/// <summary>
/// 書き出しの設定・実行・結果表示を行うダイアログ。
/// </summary>
public partial class ExportWindow : Window
{
    /// <summary>閉じる操作を受けて、書き出しの終了を待っている最中かどうか。</summary>
    private bool _waitingForExportToStop;

    /// <summary>
    /// <see cref="ExportWindow" /> の新しいインスタンスを生成する。
    /// </summary>
    public ExportWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 書き出しの最中に閉じられた場合、中止してから閉じる。
    /// </summary>
    /// <remarks>
    /// そのまま閉じると、書き込み中の一時ファイルを片付ける処理が走らないまま残る。
    /// 中止を要求して、処理中の 1 枚が終わるのを待ってから閉じる。
    /// 待ち時間は長くても 1 枚分である。
    /// </remarks>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (DataContext is not ExportDialogViewModel viewModel || !viewModel.IsExporting)
        {
            return;
        }

        e.Cancel = true;

        if (_waitingForExportToStop)
        {
            return;
        }

        _waitingForExportToStop = true;
        viewModel.CancelCommand.Execute(null);
        viewModel.PropertyChanged += OnViewModelPropertyChangedWhileClosing;
    }

    private void OnViewModelPropertyChangedWhileClosing(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportDialogViewModel.IsExporting)
            || sender is not ExportDialogViewModel { IsExporting: false } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChangedWhileClosing;
        Close();
    }

    /// <summary>
    /// 失敗の一覧を、例外の原文込みでクリップボードへ写す。
    /// </summary>
    /// <remarks>
    /// 画面には日本語の要約しか出していないため、不具合の報告に使える形をここで用意する。
    /// クリップボードは View の機能なので、文面の組み立てだけ ViewModel に任せる。
    /// </remarks>
    private void OnCopyFailures(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportDialogViewModel viewModel)
        {
            return;
        }

        try
        {
            Clipboard.SetText(viewModel.BuildFailureReport());
            viewModel.ResultMessage += "（詳細をクリップボードにコピーしました）";
        }
        catch (Exception ex)
        {
            // クリップボードは他のプロセスに掴まれていると失敗することがある
            FileErrorMessage.Log("クリップボードへのコピー", ex);
            viewModel.ResultMessage += "（クリップボードにコピーできませんでした）";
        }
    }
}
