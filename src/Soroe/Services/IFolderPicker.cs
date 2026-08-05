namespace Soroe.Services;

/// <summary>
/// フォルダ選択ダイアログ。
/// </summary>
/// <remarks>
/// ダイアログ表示は UI の都合なので、ViewModel から直接呼ばずにこの層を挟む。
/// </remarks>
public interface IFolderPicker
{
    /// <summary>
    /// フォルダを 1 つ選ばせる。
    /// </summary>
    /// <returns>選択されたフォルダの絶対パス。キャンセルされた場合は <see langword="null" />。</returns>
    string? Pick();
}
