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
        FitToWorkArea();
    }

    /// <summary>
    /// 既定サイズが画面の作業領域に収まらない場合、収まるところまで縮める。
    /// </summary>
    /// <remarks>
    /// 既定サイズ（XAML の 1100x990）は「調整項目が全部見える高さ」から決めている。
    /// 実測（DPI 150%）では、調整パネルの中身は 9 項目で 550.4 論理px あり、
    /// 見えている高さは <c>割合 x (窓の高さ - 201.3)</c> になる。割合 0.70 で
    /// 全項目が見える最小の窓高は 990 だった。
    /// <para>
    /// <b>その高さが入らない画面がある。</b>1080p を 150% で使うと論理 1280x720 で、
    /// 作業領域は 672 程度しかない。ここへ 990 をそのまま渡すと、下端
    /// （書き出しボタンのある行）が画面外へ出る。固定値のままにはできないので、
    /// 作業領域で頭打ちにする。縮める方向にしか働かないため、広い画面では
    /// 既定サイズがそのまま使われる。
    /// </para>
    /// <para>
    /// なお <see cref="WindowStartupLocation" /> を CenterScreen にしてあるのが対になっている。
    /// 既定の配置は左上からずらして開くため、作業領域いっぱいの高さにすると
    /// ずらしたぶんだけ下端がはみ出す。中央寄せなら、収まる大きさは必ず収まる。
    /// </para>
    /// <para>
    /// <see cref="SystemParameters.WorkArea" /> はプライマリ画面・システム DPI 基準なので、
    /// 別の倍率のサブ画面へ開いた場合は多少ずれる。既定値を縮める用途にしか使わないため、
    /// ずれても「少し小さい / 大きい」だけで済む。
    /// </para>
    /// </remarks>
    private void FitToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        (Width, Height) = FitToWorkArea(Width, Height, MinWidth, MinHeight, work.Width, work.Height);
    }

    /// <summary>
    /// 既定サイズを作業領域に収まる大きさへ丸める。
    /// </summary>
    /// <remarks>
    /// 判定だけを切り出してある。開発機の作業領域が広いと縮める側の経路を一度も
    /// 通らないため、実際の画面に頼ると検証できない。
    /// <para>
    /// 最小サイズは作業領域より優先する。作業領域のほうが狭い場合に最小サイズを
    /// 下回る値を返しても、WPF 側で結局 MinWidth / MinHeight に丸め直されるので、
    /// ここで返す値と実際の窓の大きさが食い違うだけになる。
    /// </para>
    /// </remarks>
    internal static (double Width, double Height) FitToWorkArea(
        double width, double height, double minWidth, double minHeight, double workWidth, double workHeight) =>
        (Math.Max(minWidth, Math.Min(width, workWidth)),
         Math.Max(minHeight, Math.Min(height, workHeight)));

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
