using System.Windows;

namespace Soroe.Views;

/// <summary>
/// 書き出しの設定と確認を行うダイアログ。
/// </summary>
public partial class ExportWindow : Window
{
    /// <summary>
    /// <see cref="ExportWindow" /> の新しいインスタンスを生成する。
    /// </summary>
    public ExportWindow()
    {
        InitializeComponent();
    }

    /// <remarks>
    /// ウィンドウを閉じるのは View の仕事なので、コマンドではなくここで受ける。
    /// 「閉じる」側は <c>IsCancel</c> が同じことを自動で行う。
    /// </remarks>
    private void OnRun(object sender, RoutedEventArgs e) => DialogResult = true;
}
