using OpenCvSharp;

namespace Soroe.Services;

/// <summary>
/// レンダリングの元になる 1 枚。調整は一切かかっていない。
/// </summary>
/// <remarks>
/// プレビュー用に読み込んだ場合は縮小されていることがあり、そのときの倍率が
/// <see cref="Scale" /> になる。非破壊で作り直すために、この画像は読むだけで書き換えない。
/// <para>
/// 使い終わったら必ず <see cref="Dispose" /> すること。中身はネイティブメモリである。
/// </para>
/// </remarks>
public sealed class RenderSource : IDisposable
{
    internal RenderSource(Mat image, int originalWidth, int originalHeight, double scale)
    {
        Image = image;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        Scale = scale;
    }

    /// <summary>元画像の幅（縮小前）。</summary>
    public int OriginalWidth { get; }

    /// <summary>元画像の高さ（縮小前）。</summary>
    public int OriginalHeight { get; }

    /// <summary>
    /// 元画像に対する倍率。1.0 なら原寸、プレビューでは 1.0 未満になることがある。
    /// </summary>
    /// <remarks>
    /// 寸法に関わる調整（リサイズ、枠線の太さ）は、この倍率を掛けてから適用しないと
    /// プレビューと書き出し結果がずれる。
    /// </remarks>
    public double Scale { get; }

    /// <summary>
    /// 調整前の画素。Mat を外に出さないため internal にしている。
    /// </summary>
    internal Mat Image { get; }

    /// <inheritdoc />
    public void Dispose() => Image.Dispose();
}
