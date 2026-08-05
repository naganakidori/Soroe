using System.Windows.Media.Imaging;

namespace Soroe.Services;

/// <summary>
/// 画像ファイルを読み込み、WPF で表示可能なビットマップに変換する。
/// </summary>
/// <remarks>
/// OpenCvSharp への依存はこのインターフェイスの実装側だけに閉じ込める。
/// ViewModel から <c>Mat</c> が見えないようにすることで、解放漏れを起こしうる箇所を Services 層に限定する。
/// </remarks>
public interface IImageLoader
{
    /// <summary>
    /// 指定したパスの画像を読み込む。
    /// </summary>
    /// <param name="path">画像ファイルの絶対パス。</param>
    /// <returns>
    /// 読み込んだ画像。凍結済みなのでそのまま UI にバインドできる。
    /// 読み込めない形式や破損ファイルの場合は <see langword="null" />。
    /// </returns>
    BitmapSource? Load(string path);
}
