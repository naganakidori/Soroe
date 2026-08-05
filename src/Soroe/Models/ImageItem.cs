using System.IO;

namespace Soroe.Models;

/// <summary>
/// 処理対象の画像 1 件。
/// </summary>
/// <remarks>
/// 元画像は非破壊で扱うため、この型はパスだけを持ち、画素データは保持しない。
/// </remarks>
public sealed class ImageItem
{
    /// <summary>
    /// <see cref="ImageItem" /> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="fullPath">画像ファイルの絶対パス。</param>
    public ImageItem(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
    }

    /// <summary>画像ファイルの絶対パス。重複判定の基準でもある。</summary>
    public string FullPath { get; }

    /// <summary>リストに表示するファイル名。</summary>
    public string FileName { get; }
}
