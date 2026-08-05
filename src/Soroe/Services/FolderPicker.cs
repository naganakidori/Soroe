using Microsoft.Win32;

namespace Soroe.Services;

/// <summary>
/// WPF 標準のフォルダ選択ダイアログによる <see cref="IFolderPicker" /> の実装。
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
    /// <inheritdoc />
    public string? Pick()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "画像が入っているフォルダを選んでください",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
