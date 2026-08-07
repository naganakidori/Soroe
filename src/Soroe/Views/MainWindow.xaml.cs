using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <remarks>
    /// 書き出し中の閉じる操作はダイアログ側が受け持つ。書き出しはモーダルの中で
    /// 完結するため、この画面が書き出し中に閉じられることはない。
    /// </remarks>
    public MainWindow()
    {
        InitializeComponent();
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
