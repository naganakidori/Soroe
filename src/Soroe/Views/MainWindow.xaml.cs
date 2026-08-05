using System.Windows;
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
    public MainWindow()
    {
        InitializeComponent();
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

        if (DataContext is MainViewModel viewModel && viewModel.AddFilesCommand.CanExecute(paths))
        {
            viewModel.AddFilesCommand.Execute(paths);
        }

        e.Handled = true;
    }
}
