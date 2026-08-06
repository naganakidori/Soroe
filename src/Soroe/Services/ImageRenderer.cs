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
    /// <summary>
    /// PNG の圧縮率。
    /// </summary>
    /// <remarks>
    /// OpenCV の既定は 1（速度優先）で、一般的なツールの既定である 6 に比べて
    /// ファイルがかなり大きくなる。「一括で小さくして送る」ためのソフトで出力が
    /// 無駄に大きいのは目的と食い違うため、明示的に 6 を指定する。
    /// <b>PNG は可逆なので、この値を変えても画質は一切変わらない。</b>
    /// 変わるのはファイルサイズと圧縮にかかる時間だけであり、
    /// 「画質」として UI に出すと誤解を招くため出さない。
    /// </remarks>
    private const int PngCompression = 6;

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

        var decoded = Decode(bytes);
        if (decoded is null)
        {
            // 対応していない形式、または壊れたファイル
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

    /// <summary>
    /// バイト列を 8bit 3ch の <see cref="Mat" /> として読み込む。
    /// </summary>
    /// <remarks>
    /// アルファを持ちうる形式だけ <c>Unchanged</c> で読む。
    /// <b><c>Unchanged</c> は Exif の Orientation を適用しない</b>（実測で確認済み。
    /// <c>IMREAD_UNCHANGED</c> は -1 で、<c>IMREAD_IGNORE_ORIENTATION</c> のビットを含むため）。
    /// すべてを <c>Unchanged</c> にすると、スマホで撮った縦写真が横倒しで出力される。
    /// <para>
    /// 既知の制限として、Exif の Orientation を持つ WebP は回転が適用されない。
    /// WebP は仕様上 Exif を持てるが、回転情報つきの WebP は実際にはほぼ存在しない。
    /// </para>
    /// </remarks>
    private static Mat? Decode(byte[] bytes)
    {
        if (!MayHaveAlpha(bytes))
        {
            var color = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (!color.Empty())
            {
                return color;
            }

            color.Dispose();
            return null;
        }

        var image = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
        if (image.Empty())
        {
            image.Dispose();
            return null;
        }

        // 深度を先に落とす。16bit のアルファは 0〜65535 なので、合成より前に揃える必要がある
        image = NormalizeDepth(image);
        return NormalizeChannels(image);
    }

    /// <summary>
    /// アルファを持ちうる形式かどうかを、ファイル先頭の署名で判定する。
    /// </summary>
    /// <remarks>拡張子は詐称されうるため中身で見る。</remarks>
    private static bool MayHaveAlpha(byte[] bytes)
    {
        // PNG
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
        {
            return true;
        }

        // WebP は RIFF コンテナ。先頭 "RIFF"、8 バイト目から "WEBP"
        if (bytes.Length >= 12
            && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
        {
            return true;
        }

        // BMP（32bit BMP はアルファを持ちうる）
        return bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M';
    }

    /// <summary>
    /// 8bit 以外で読み込まれた画像を 8bit に落とす。
    /// </summary>
    /// <remarks>
    /// <c>Unchanged</c> はチャンネル数だけでなくビット深度も保持するため、16bit PNG は
    /// CV_16U のまま返ってくる。明るさで使う <c>Cv2.LUT</c> は 8bit 入力を前提としており、
    /// そのまま流すと失敗する。
    /// </remarks>
    private static Mat NormalizeDepth(Mat image)
    {
        var depth = image.Depth();
        if (depth == (int)MatType.CV_8U)
        {
            return image;
        }

        // 16bit は 0〜65535、浮動小数は 0〜1 を想定して 0〜255 に写す
        var scale = depth == (int)MatType.CV_16U ? 255.0 / 65535.0
            : depth == (int)MatType.CV_32F || depth == (int)MatType.CV_64F ? 255.0
            : 1.0;

        using (image)
        {
            var converted = new Mat();
            image.ConvertTo(converted, MatType.CV_8U, scale);
            return converted;
        }
    }

    /// <summary>
    /// チャンネル数を 3ch (BGR) に揃える。透過は白で合成する。
    /// </summary>
    private static Mat NormalizeChannels(Mat image)
    {
        switch (image.Channels())
        {
            case 3:
                return image;

            case 4:
                using (image)
                {
                    return CompositeOverWhite(image);
                }

            case 1:
                using (image)
                {
                    var bgr = new Mat();
                    Cv2.CvtColor(image, bgr, ColorConversionCodes.GRAY2BGR);
                    return bgr;
                }

            case 2:
                // グレースケール + アルファ。OpenCV では 2ch の PNG を書き出せないため
                // 検証用ファイルを作れず、この経路は実地確認ができていない
                using (image)
                {
                    var planes = Cv2.Split(image);
                    try
                    {
                        using var bgra = new Mat();
                        Cv2.Merge([planes[0], planes[0], planes[0], planes[1]], bgra);
                        return CompositeOverWhite(bgra);
                    }
                    finally
                    {
                        foreach (var plane in planes)
                        {
                            plane.Dispose();
                        }
                    }
                }

            default:
                using (image)
                {
                    var fallback = new Mat();
                    Cv2.CvtColor(image, fallback, ColorConversionCodes.GRAY2BGR);
                    return fallback;
                }
        }
    }

    /// <summary>
    /// BGRA を白の背景に合成して BGR にする。
    /// </summary>
    /// <remarks>
    /// 透過は保持せず、出力形式によらず白で塗り潰す。形式ごとに分岐させると、
    /// アルファを保持する経路が生まれて全調整項目にチャンネル方針が必要になるため。
    /// <para>
    /// 読み込み直後に行うので、以降のチェーンは 3ch だけを扱えばよい。
    /// 調整は合成後の画素にかかるため、暗くすれば元・透明部分も一緒に暗くなる。
    /// </para>
    /// </remarks>
    private static Mat CompositeOverWhite(Mat bgra)
    {
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

        using var alpha = new Mat();
        Cv2.ExtractChannel(bgra, alpha, 3);

        // 0〜1 に正規化して 3 チャンネルに広げる
        using var alphaNormalized = new Mat();
        alpha.ConvertTo(alphaNormalized, MatType.CV_32FC1, 1.0 / 255.0);
        using var alpha3 = new Mat();
        Cv2.Merge([alphaNormalized, alphaNormalized, alphaNormalized], alpha3);

        using var source = new Mat();
        bgr.ConvertTo(source, MatType.CV_32FC3);
        using var white = new Mat(bgr.Size(), MatType.CV_32FC3, Scalar.All(255));

        // 出力 = 255 + (元の色 - 255) × α
        using var difference = new Mat();
        Cv2.Subtract(source, white, difference);
        using var scaled = new Mat();
        Cv2.Multiply(difference, alpha3, scaled);
        using var composited = new Mat();
        Cv2.Add(white, scaled, composited);

        var result = new Mat();
        composited.ConvertTo(result, MatType.CV_8UC3);
        return result;
    }

    /// <summary>
    /// 形式ごとのエンコード指定を組み立てる。
    /// </summary>
    private static int[] BuildEncodeParameters(string extension, EncodeSettings encode) => extension switch
    {
        ".jpg" or ".jpeg" => [(int)ImwriteFlags.JpegQuality, encode.JpegQuality],
        ".webp" => [(int)ImwriteFlags.WebPQuality, encode.WebPQuality],
        ".png" => [(int)ImwriteFlags.PngCompression, PngCompression],
        _ => [],
    };

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
        // エンコードだけ行い、ファイルへの書き込みは .NET 側で行う。
        // 「元の形式を維持」では .JPG のように大文字のこともあるので小文字に揃える
        var extension = encode.Extension.ToLowerInvariant();
        Cv2.ImEncode(extension, result, out var bytes, BuildEncodeParameters(extension, encode));
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
