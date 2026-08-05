using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace Soroe.Services;

/// <summary>
/// OpenCvSharp による <see cref="IImageLoader" /> の実装。
/// </summary>
public sealed class ImageLoader : IImageLoader
{
    /// <inheritdoc />
    public BitmapSource? Load(string path)
    {
        // Cv2.ImRead はファイル名を ANSI（システムのコードページ）でネイティブへ渡すため、
        // コードページに無い文字を含むパスで ArgumentException を投げる。
        // 日本語環境（CP932）でも、韓国語や絵文字を含むファイル名で実際に落ちることを確認済み。
        // ファイル自体は .NET 側で読み、バイト列を ImDecode に渡すことでこれを回避する。
        // ImDecode も ImRead と同じく Exif の Orientation を適用して展開する。
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // Mat は GC の管理外のネイティブメモリなので、必ず using で解放する
        using var mat = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (mat.Empty())
        {
            // 対応していない形式、または壊れたファイル
            return null;
        }

        var bitmap = mat.ToWriteableBitmap();

        // Freeze すると以降変更不可になる代わりに、スレッドをまたいで安全に渡せる。
        // 将来プレビュー生成をバックグラウンドで行うため、この時点で凍結しておく。
        bitmap.Freeze();
        return bitmap;
    }
}
