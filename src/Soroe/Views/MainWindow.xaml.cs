using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Soroe.Common;
using Soroe.ViewModels;

namespace Soroe.Views;

/// <summary>
/// メインウィンドウ。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// <see cref="MainWindow" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <summary>閉じる操作を受けて、書き出しの終了を待っている最中かどうか。</summary>
    private bool _waitingForExportToStop;

    /// <summary>
    /// <see cref="MainWindow" /> の新しいインスタンスを生成する。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 書き出しの最中に閉じられた場合、中止してから閉じる。
    /// </summary>
    /// <remarks>
    /// そのまま閉じるとプロセスが終了し、書き込み中の一時ファイルを片付ける処理が
    /// 走らないまま残ってしまう。中止を要求して、処理中の 1 枚が終わるのを待ってから閉じる。
    /// 待ち時間は長くても 1 枚分である。
    /// </remarks>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.IsExporting)
        {
            return;
        }

        e.Cancel = true;

        if (_waitingForExportToStop)
        {
            return;
        }

        _waitingForExportToStop = true;
        viewModel.CancelExportCommand.Execute(null);
        viewModel.PropertyChanged += OnViewModelPropertyChangedWhileClosing;
    }

    private void OnViewModelPropertyChangedWhileClosing(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsExporting)
            || sender is not MainViewModel { IsExporting: false } viewModel)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChangedWhileClosing;
        Close();
    }

    /// <summary>
    /// 調整項目のスライダー上でホイールを回したときに 1 目盛り動かす。
    /// </summary>
    /// <remarks>
    /// つまみを狙わずに値を詰めるための補助。既定では Slider はホイールに反応せず、
    /// 親のスクロールに流れてしまうため、ここで受け取って処理する。
    /// <para>
    /// <b>フォーカスがある場合だけ値を変える。</b>調整パネルはスクロールするため、
    /// 通り過ぎただけのホイールで値が変わると、スクロールしたつもりで設定が
    /// 書き換わったことに気づけない。一括処理の設定が黙って変わるのは実害がある。
    /// フォーカスが無いときは何もせず、そのままスクロールに流す。
    /// </para>
    /// </remarks>
    private void OnSliderMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsEnabled || !slider.IsKeyboardFocusWithin)
        {
            return;
        }

        slider.Value += Math.Sign(e.Delta) * (slider.SmallChange == 0 ? 1 : slider.SmallChange);
        e.Handled = true;
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
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            Clipboard.SetText(viewModel.BuildFailureReport());
            viewModel.StatusMessage = "失敗の詳細をクリップボードにコピーしました";
        }
        catch (Exception ex)
        {
            // クリップボードは他のプロセスに掴まれていると失敗することがある
            FileErrorMessage.Log("クリップボードへのコピー", ex);
            viewModel.StatusMessage = "クリップボードにコピーできませんでした";
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        // DataObject は WPF 固有の型なので、ここで string[] に変換してから ViewModel に渡す。
        // こうしておけば ViewModel 側は WPF のドラッグ＆ドロップを知らずに済む。
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.AddPathsCommand.CanExecute(paths))
        {
            viewModel.AddPathsCommand.Execute(paths);
        }

        e.Handled = true;
    }
}
