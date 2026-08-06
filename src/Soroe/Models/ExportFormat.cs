namespace Soroe.Models;

/// <summary>
/// 書き出す画像の形式。
/// </summary>
public enum ExportFormat
{
    /// <summary>元のファイルと同じ形式で書き出す。</summary>
    KeepOriginal = 0,

    /// <summary>JPEG (.jpg)。</summary>
    Jpeg,

    /// <summary>PNG (.png)。</summary>
    Png,

    /// <summary>BMP (.bmp)。</summary>
    Bmp,

    /// <summary>WebP (.webp)。</summary>
    WebP,
}
