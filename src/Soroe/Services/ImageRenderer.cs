using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Soroe.Models;

namespace Soroe.Services;

/// <summary>
/// OpenCvSharp による <see cref="IImageRenderer" /> の実装。
/// </summary>
public sealed class ImageRenderer : IImageRenderer
{
    /// <inheritdoc />
    public RenderSource? Load(string path, int maxEdge)
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

        var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (decoded.Empty())
        {
            // 対応していない形式、または壊れたファイル
            decoded.Dispose();
            return null;
        }

        var longest = Math.Max(decoded.Width, decoded.Height);
        if (maxEdge <= 0 || longest <= maxEdge)
        {
            // 縮小不要。読み込んだものをそのまま渡す（破棄の責任も RenderSource に移る）
            return new RenderSource(decoded, decoded.Width, decoded.Height, 1.0);
        }

        // 縮小後だけを保持し、原寸のほうは破棄する
        using (decoded)
        {
            var scale = (double)maxEdge / longest;
            var resized = new Mat();

            // 縮小は Area が最もモアレが出にくい
            Cv2.Resize(decoded, resized, new Size(), scale, scale, InterpolationFlags.Area);
            return new RenderSource(resized, decoded.Width, decoded.Height, scale);
        }
    }

    /// <inheritdoc />
    public BitmapSource Render(RenderSource source, ProcessingSettings settings, double previewScale)
    {
        // 適用処理は Encode と共有する。ここで独自に加工を足さないこと
        using var result = Apply(
            source.Image,
            settings,
            new RenderContext(source.OriginalWidth, source.OriginalHeight, previewScale));

        var bitmap = result.ToWriteableBitmap();

        // Freeze すると以降変更不可になる代わりに、スレッドをまたいで安全に渡せる。
        // レンダリングはバックグラウンドで行うため、ここで凍結してから UI に渡す。
        bitmap.Freeze();
        return bitmap;
    }

    /// <inheritdoc />
    public byte[] Encode(RenderSource source, ProcessingSettings settings, double previewScale, EncodeSettings encode)
    {
        // 適用処理は Render と共有する。ここで独自に加工を足さないこと。
        // 分けた瞬間に「プレビューと出力が違う」という最悪の不具合が生まれる
        using var result = Apply(
            source.Image,
            settings,
            new RenderContext(source.OriginalWidth, source.OriginalHeight, previewScale));

        // Cv2.ImWrite は使わない。ImRead と同じくファイル名を ANSI でネイティブへ渡すため、
        // CP932 外の文字を含む出力パスで例外になり、一括処理が中断する。
        // エンコードだけ行い、ファイルへの書き込みは .NET 側で行う
        Cv2.ImEncode(encode.Extension, result, out var bytes);
        return bytes;
    }

    /// <summary>
    /// 設定を固定順で適用した結果を新しい <see cref="Mat" /> として返す。
    /// </summary>
    /// <remarks>
    /// <b>プレビュー（<see cref="Render" />）と書き出し（<see cref="Encode" />）は、
    /// どちらも必ずこのメソッドを通す。</b>「見たとおりに出る」ことがこのアプリの
    /// 存在意義なので、経路ごとに加工を書いてはいけない。
    /// <para>
    /// 適用順序は次で固定する（CLAUDE.md「適用順序は固定」）。
    /// 1. 回転 / 2. リサイズ / 3. 明るさ・コントラスト / 4. 彩度 / 5. グレースケール /
    /// 6. 二値化 / 7. シャープ / 8. 枠線。
    /// </para>
    /// <para>
    /// 現時点で実装しているのは 3. の明るさのみ。残りはこのメソッドに順番どおり挿入していく。
    /// </para>
    /// <para>
    /// 寸法に関わる調整（2. リサイズ、8. 枠線の太さ）は、まず元画像に対する出力寸法を
    /// 決めてから <see cref="RenderContext.PreviewScale" /> を掛ける。画素値だけを変える
    /// 調整（明るさなど）は倍率の影響を受けない。
    /// </para>
    /// </remarks>
    private static Mat Apply(Mat original, ProcessingSettings settings, RenderContext context)
    {
        // 元画像には書き込まない。毎回コピーから作り直すので画質劣化が蓄積しない
        var result = original.Clone();

        // 2. リサイズ
        if (settings.Resize.Enabled)
        {
            // 出力寸法は元画像の寸法から決める。倍率からの逆算はしない
            var (targetWidth, targetHeight) =
                settings.Resize.ResolveSize(context.OriginalWidth, context.OriginalHeight);

            // プレビューは既に縮小されているので、出力寸法に現在の倍率を掛けたところまで縮める。
            // 書き出し時は倍率が 1.0 なので、出力寸法そのものになる
            var width = Math.Max(1, (int)Math.Round(targetWidth * context.PreviewScale));
            var height = Math.Max(1, (int)Math.Round(targetHeight * context.PreviewScale));

            if (width != result.Width || height != result.Height)
            {
                var resized = new Mat();

                // 拡大はしないと決めているため、縮小に最も適した Area で固定する
                Cv2.Resize(result, resized, new Size(width, height), interpolation: InterpolationFlags.Area);
                result.Dispose();
                result = resized;
            }
        }

        // 3. 明るさ
        if (settings.Brightness.Enabled && settings.Brightness.Value != 0)
        {
            using var lut = BuildBrightnessLut(settings.Brightness.Value);
            var adjusted = new Mat();
            Cv2.LUT(result, lut, adjusted);
            result.Dispose();
            result = adjusted;
        }

        return result;
    }

    /// <summary>
    /// 明るさ変換の対応表を作る。
    /// </summary>
    /// <remarks>
    /// 画素ごとに加算すると 1 画素あたり何度も計算が走るため、0〜255 の変換表を
    /// 1 度だけ作って <c>Cv2.LUT</c> で一括変換する。後でコントラストを足すときも、
    /// 同じ表に畳み込めば走査は 1 回で済む。
    /// </remarks>
    private static Mat BuildBrightnessLut(int value)
    {
        var lut = new Mat(1, 256, MatType.CV_8UC1);
        for (var i = 0; i < 256; i++)
        {
            lut.Set(0, i, (byte)Math.Clamp(i + value, 0, 255));
        }

        return lut;
    }
}
