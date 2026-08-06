using CommunityToolkit.Mvvm.ComponentModel;

namespace Soroe.Models;

/// <summary>
/// 書き出しの設定。
/// </summary>
/// <remarks>
/// 第 1 段では出力先のみ。形式の選択、ファイル名のオプション（プレフィックス /
/// サフィックス / 連番）、同名ファイルがあったときの扱いは第 2 段でここに足す。
/// </remarks>
public sealed partial class ExportSettings : ObservableObject
{
    /// <summary>出力先フォルダの絶対パス。未選択なら <see langword="null" />。</summary>
    [ObservableProperty]
    public partial string? Folder { get; set; }
}
