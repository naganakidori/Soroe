using System.Windows.Media.Imaging;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// 画像の読み込みと、設定を適用した結果の生成を担う。
/// </summary>
/// <remarks>
/// OpenCvSharp への依存はこのインターフェイスの実装側だけに閉じ込める。
/// ViewModel から <c>Mat</c> が見えないようにすることで、解放漏れを起こしうる箇所を
/// Services 層に限定する。
/// </remarks>
public interface IImageRenderer
{
    /// <summary>
    /// 画像を読み込み、レンダリングの元として保持する。
    /// </summary>
    /// <param name="path">画像ファイルの絶対パス。</param>
    /// <param name="maxEdge">
    /// 長辺の上限（ピクセル）。これを超える画像は縮小して保持する。
    /// 0 以下を渡すと縮小しない（書き出し時はこちら）。
    /// </param>
    /// <returns>読み込めなかった場合は <see langword="null" />。</returns>
    RenderSource? Load(string path, int maxEdge);

    /// <summary>
    /// 元画像に設定を適用した結果を生成する。
    /// </summary>
    /// <param name="source">元になる画像。この中身は変更しない。</param>
    /// <param name="settings">適用する設定。</param>
    /// <param name="previewScale">
    /// <paramref name="source" /> が元画像に対して何倍かを表す値。通常は
    /// <see cref="RenderSource.Scale" /> をそのまま渡す。
    /// 寸法に関わる調整（リサイズ、枠線の太さ）は、プレビューが既に縮小されている分を
    /// この倍率で打ち消してから適用する。
    /// </param>
    /// <returns>凍結済みのビットマップ。そのまま UI にバインドできる。</returns>
    BitmapSource Render(RenderSource source, ProcessingSettings settings, double previewScale);
}
