using System.Windows;
using Soroe.Services;
using Soroe.ViewModels;

namespace Soroe.Views;

/// <summary>
/// <see cref="IExportDialog" /> の実装。
/// </summary>
/// <remarks>
/// インターフェイスは Services にあるが、実装は View を組み立てるためここに置く。
/// Services から View の型を参照させないための配置。
/// </remarks>
public sealed class ExportDialogService : IExportDialog
{
    /// <inheritdoc />
    public bool Confirm(ExportDialogViewModel viewModel)
    {
        var window = new ExportWindow
        {
            DataContext = viewModel,
            Owner = Application.Current?.MainWindow,
        };

        return window.ShowDialog() == true;
    }
}
