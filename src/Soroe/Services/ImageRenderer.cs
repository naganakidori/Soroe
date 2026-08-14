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
    /// プレビューに使う長辺の上限。
    /// </summary>
    /// <remarks>
    /// <b>これは性能の調整値ではなく、出力を決める定数である。</b>
    /// 二値化のしきい値は大津の方法で画像から決まるが、その計算はこの寸法に縮小した
    /// 画像に対して行う（プレビューと書き出しで同じ値を得るため）。したがって
    /// <b>この値を変えると、同じ画像・同じ設定でも二値化の結果が変わる。</b>
    /// 保存済みのプリセットを読み込んでも以前と違う絵が出る。
    /// <para>
    /// 窓の大きさや DPI に依存させてはいけない。依存させると、窓を広げただけで
    /// 二値化の結果が変わる。
    /// </para>
    /// </remarks>
    public const int CanonicalEdge = 1600;

    /// <summary>
    /// シャープ（アンシャープマスク）のぼかし半径。出力画素を基準にした値。
    /// </summary>
    /// <remarks>
    /// <b><see cref="CanonicalEdge" /> と同じく、これは調整値ではなく出力を決める定数である。</b>
    /// 変えると、同じ画像・同じ設定でもシャープの結果が変わり、保存済みのプリセットを
    /// 読み込んでも以前と違う絵が出る。しかも<b>この値は UI に出していないので、
    /// 外から見て変わったことが分からない</b>。触るときは出力が変わることを承知のうえで行う。
    /// <para>
    /// プレビューでは倍率を掛けて縮める（<c>σ × previewScale</c>）。掛けないと、
    /// 縮小画像に原寸と同じ半径を掛けることになり、画像に対する相対的な効きが変わる。
    /// 実測では倍率 0.40 で平均差 3.96、倍率 0.20 で 5.71 になり、
    /// プレビューと書き出しの一致（閾値 2.0）を大きく超えた。
    /// </para>
    /// <para>
    /// この 2.0 という値は、倍率を掛けた後も効きが残るように選んである。実測では
    /// σ が 0.3 を下回ると効果が消える。σ 2.0 なら倍率 0.15 まで持つので、
    /// 長辺 10000px 程度までは効果がプレビューに出る。
    /// </para>
    /// </remarks>
    public const double SharpenSigma = 2.0;

    /// <summary>
    /// PNG の圧縮率。
    /// </summary>
    /// <remarks>
    /// <b>PNG は可逆なので、この値を変えても画質は一切変わらない。</b>
    /// 変わるのはファイルサイズと圧縮にかかる時間だけであり、
    /// 「画質」として UI に出すと誤解を招くため出さない。
    /// <para>
    /// 実測 2026-08-10（Release、実写 3000×4000、5 回の中央値、最初の 2 回は捨てる、
    /// エンコードのみ）。再現は
    /// <c>dotnet run --project tests/Soroe.Verify -c Release -- --bench 写真.jpg</c>。
    /// <b>合成の標本では圧縮率の差が小さく出る</b>ので、判断をやり直すときは実写を渡すこと。
    /// </para>
    /// <code>
    ///                         実写 3000×4000        スクリーンショット 1500×1080
    ///   圧縮率 1（既定）        507ms / 16.30MB        18ms / 0.090MB
    ///   圧縮率 3               829ms / 15.67MB        18ms / 0.088MB
    ///   圧縮率 4               753ms / 16.07MB        26ms / 0.075MB
    ///   圧縮率 6              2235ms / 15.25MB        28ms / 0.074MB
    /// </code>
    /// <para>
    /// <b>4 を選ぶ理由。</b>費用と便益が別の画像に乗っている。実写は PNG では
    /// ほとんど縮まないのに時間だけ延び、平坦な画像は逆にほとんど時間をかけずに
    /// 大きく縮む。6 にすると実写 1 枚あたり 4 に対して +1.5 秒（1 に対しては +1.7 秒）
    /// かかるが、実際のスクリーンショットで 4 との差は 83.3% と 82.5% しかない。
    /// 一括処理では 1 枚の差が枚数倍になるため、得るものの小さい 4 → 6 は割に合わない。
    /// </para>
    /// <para>
    /// <b>3 は選ばないこと。</b>折衷に見えるが、zlib は 4 以上で探索方式を切り替えるため、
    /// 3 では平坦画像の利得がほぼ出ない（99.8%）。実写では遅くなるだけである。
    /// なお実写では 3 が 4 より遅く測れており、この帯では単調でない。
    /// </para>
    /// </remarks>
    private const int PngCompression = 4;

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

        var resized = Downscale(decoded, maxEdge);
        if (resized is null)
        {
            // 縮小不要。読み込んだものをそのまま渡す（破棄の責任も RenderSource に移る）
            return new RenderSource(decoded, decoded.Width, decoded.Height, 1.0);
        }

        // 縮小後だけを保持し、原寸のほうは破棄する
        using (decoded)
        {
            return new RenderSource(
                resized, decoded.Width, decoded.Height, ScaleFor(decoded.Width, decoded.Height, maxEdge));
        }
    }

    /// <summary>
    /// 長辺を <paramref name="maxEdge" /> 以内に縮める。縮小が要らなければ <see langword="null" />。
    /// </summary>
    /// <remarks>
    /// <b>同じ画像を作るつもりの縮小は、必ずこの 1 つの関数を通すこと。</b>
    /// <c>Cv2.Resize</c> は倍率で指定するか寸法で指定するかで内部の係数の求め方が変わり、
    /// 割り切れない比では別の画素になる。実測では、2551×1699 を長辺 1600 へ縮めたとき、
    /// 同じ寸法でありながら全体の 54% の画素が違い、最大差は 30 階調だった。
    /// <para>
    /// 二値化のしきい値はこの縮小結果から決まるため、呼び出し箇所を分けた時点で
    /// 「プレビューと書き出しで同じ値になる」という保証が失われる。
    /// </para>
    /// </remarks>
    private static Mat? Downscale(Mat source, int maxEdge)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (maxEdge <= 0 || longest <= maxEdge)
        {
            return null;
        }

        var scale = ScaleFor(source.Width, source.Height, maxEdge);
        var resized = new Mat();

        // 縮小は Area が最もモアレが出にくい
        Cv2.Resize(source, resized, new Size(), scale, scale, InterpolationFlags.Area);
        return resized;
    }

    /// <summary>長辺を <paramref name="maxEdge" /> 以内に収めるための倍率。</summary>
    private static double ScaleFor(int width, int height, int maxEdge)
    {
        var longest = Math.Max(width, height);
        return maxEdge <= 0 || longest <= maxEdge ? 1.0 : (double)maxEdge / longest;
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
    /// 適用順序は次で固定する（docs/design.md「適用順序は固定」）。
    /// 1. 回転 / 2. リサイズ / 3. 明るさ・コントラスト / 4. 彩度 / 5. グレースケール /
    /// 6. 二値化 / 7. シャープ / 8. 枠線。
    /// </para>
    /// <para>
    /// 寸法に関わる調整（2. リサイズ、8. 枠線の太さ）は、まず元画像に対する出力寸法を
    /// 決めてから <see cref="RenderContext.PreviewScale" /> を掛ける。画素値だけを変える
    /// 調整（明るさなど）は倍率の影響を受けない。
    /// </para>
    /// </remarks>
    /// <param name="original">元画像。書き込まない。</param>
    /// <param name="settings">適用する設定。</param>
    /// <param name="context">元画像の寸法と、プレビューの倍率。</param>
    /// <param name="stopBeforeBinarize">
    /// 6. 二値化の直前で打ち切る。<see cref="ResolveThreshold" /> が、大津に渡す画素を
    /// 作るために使う。
    /// <para>
    /// <b>「二値化の手前までのチェーン」の定義をこの 1 箇所に閉じるための引数である。</b>
    /// 以前は設定の写しから二値化以降を落として <see cref="Apply" /> を流し直していたが、
    /// それだと同じ「前半のチェーン」が 2 箇所に書かれることになり、順序が二値化より
    /// 後ろの項目を足したときに落とし忘れると静かに食い違った（実際に起きた）。
    /// 打ち切りにすれば、<b>後ろに足した工程はこの return より下に書かれるので、
    /// 物理的にしきい値の計算へ混ざりようがない。</b>
    /// </para>
    /// </param>
    private static Mat Apply(
        Mat original, ProcessingSettings settings, RenderContext context, bool stopBeforeBinarize = false)
    {
        // 元画像には書き込まない。毎回コピーから作り直すので画質劣化が蓄積しない
        var result = original.Clone();

        // 1. 回転
        if (settings.Rotation.Enabled)
        {
            var rotated = new Mat();
            Cv2.Rotate(result, rotated, settings.Rotation.Angle switch
            {
                RotationAngle.Half => RotateFlags.Rotate180,
                RotationAngle.CounterClockwise90 => RotateFlags.Rotate90Counterclockwise,
                _ => RotateFlags.Rotate90Clockwise,
            });
            result.Dispose();
            result = rotated;
        }

        // 出力寸法。回転で縦横が入れ替わるので、RenderContext が持つ回転前の寸法を
        // そのまま使ってはいけない。求め方は ProcessingSettings.ResolveOutputSize に
        // 閉じてあり、寸法表示や枠線の頭打ち判定も同じものを通る。
        // 枠線の太さの頭打ちにも使うので、リサイズが無効でも求めておく
        var (targetWidth, targetHeight) =
            settings.ResolveOutputSize(context.OriginalWidth, context.OriginalHeight);

        // 2. リサイズ
        if (settings.Resize.Enabled)
        {
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

        // 3. 明るさ / コントラスト（1 本の LUT にまとめて 1 回で通す）
        var brightness = settings.Brightness.Enabled ? settings.Brightness.Value : 0;
        var contrast = settings.Contrast.Enabled ? settings.Contrast.Value : 0;
        if (brightness != 0 || contrast != 0)
        {
            using var lut = BuildToneLut(brightness, contrast);
            var adjusted = new Mat();
            Cv2.LUT(result, lut, adjusted);
            result.Dispose();
            result = adjusted;
        }

        // 4. 彩度
        if (settings.Saturation.Enabled && settings.Saturation.Value != 0)
        {
            var saturated = ApplySaturation(result, settings.Saturation.Value);
            result.Dispose();
            result = saturated;
        }

        // 5. グレースケール
        if (settings.Grayscale.Enabled)
        {
            var gray = ToGray3(result);
            result.Dispose();
            result = gray;
        }

        // ここから下が「二値化以降」。しきい値を求めるための呼び出しはここで戻る。
        // 新しい工程をこの行より下に足せば、しきい値の計算には自動的に混ざらない
        if (stopBeforeBinarize)
        {
            return result;
        }

        // 6. 二値化（大津）
        if (settings.Binarize.Enabled)
        {
            var threshold = ResolveThreshold(original, settings, context, result);
            var binarized = ApplyBinarize(result, threshold);
            result.Dispose();
            result = binarized;
        }

        // 7. シャープ
        if (settings.Sharpen.Enabled && settings.Sharpen.Value != 0)
        {
            var sharpened = ApplySharpen(result, settings.Sharpen.Value, context.PreviewScale);
            result.Dispose();
            result = sharpened;
        }

        // 8. 枠線
        if (settings.Frame.Enabled)
        {
            // 太さは出力画素で決めてから倍率を掛ける。プレビューの寸法から
            // 頭打ちを求めると、プレビューと書き出しで太さが変わる
            var thickness = settings.Frame.ResolveThickness(targetWidth, targetHeight);
            DrawFrame(result, thickness, context.PreviewScale, settings.Frame.Color);
        }

        return result;
    }

    /// <summary>
    /// 画像の内側に枠線を描く。
    /// </summary>
    /// <remarks>
    /// <b>内側に描くので出力寸法は変わらない。</b>外側に足すとリサイズの指定
    /// （長辺 N）が破れる（詳細は <see cref="FrameOption" />）。
    /// <para>
    /// <c>Cv2.Rectangle</c> の線幅は境界をまたいで描かれるため、塗り潰した長方形を
    /// 4 本置く。こうすれば「内側ちょうど」であることが式から読める。
    /// </para>
    /// </remarks>
    private static void DrawFrame(Mat target, int thickness, double previewScale, FrameColor color)
    {
        // 倍率を掛けた結果が 0 になっても、1px は描く。プレビューだけ枠線が
        // 消えると「有効にしたのに何も起きない」という嘘になる
        var scaled = Math.Max(1, (int)Math.Round(thickness * previewScale));

        // 縮小の丸めで、倍率を掛けた太さが画像を覆いきる場合がありうる。
        // 覆うと絵が残らないので、実際の Mat の短辺の半分で止める
        var width = target.Width;
        var height = target.Height;
        scaled = Math.Max(1, Math.Min(scaled, Math.Min(width, height) / 2));

        var scalar = color switch
        {
            FrameColor.Black => new Scalar(0, 0, 0),
            FrameColor.Gray => new Scalar(128, 128, 128),
            _ => new Scalar(255, 255, 255),
        };

        var middle = Math.Max(0, height - (scaled * 2));

        Cv2.Rectangle(target, new Rect(0, 0, width, scaled), scalar, -1);
        Cv2.Rectangle(target, new Rect(0, height - scaled, width, scaled), scalar, -1);
        Cv2.Rectangle(target, new Rect(0, scaled, scaled, middle), scalar, -1);
        Cv2.Rectangle(target, new Rect(width - scaled, scaled, scaled, middle), scalar, -1);
    }

    /// <summary>
    /// アンシャープマスクでシャープを掛ける。
    /// </summary>
    /// <remarks>
    /// <c>元 + 量 ×（元 − ぼかし）</c>。<b>ぼかしの半径には
    /// <paramref name="previewScale" /> を掛けること。</b>
    /// これがチェーンで初めての空間フィルタで、1 画素だけを見る調整（明るさなど）と違い、
    /// 画像の大きさに対する相対的な効きが結果を決める。倍率を掛けないと、縮小された
    /// プレビューに原寸と同じ半径を掛けることになり、書き出しと違う絵になる。
    /// <para>
    /// 二値化の後に来るため、二値化が有効なときは何も起きない（飽和で元の値に戻る）。
    /// これは仕様であり、UI で文字で断っている。
    /// </para>
    /// </remarks>
    private static Mat ApplySharpen(Mat source, int strength, double previewScale)
    {
        var amount = strength / 50.0;
        var sigma = SharpenSigma * previewScale;

        using var blurred = new Mat();
        Cv2.GaussianBlur(source, blurred, new Size(0, 0), sigma);

        var result = new Mat();
        Cv2.AddWeighted(source, 1.0 + amount, blurred, -amount, 0.0, result);
        return result;
    }

    /// <summary>
    /// 3ch のまま輝度へ落とす。
    /// </summary>
    /// <remarks>
    /// チェーンは常に 8bit 3ch で流すため、1ch にはしない。
    /// 結果は「彩度 -100」と全画素一致する。
    /// </remarks>
    private static Mat ToGray3(Mat source)
    {
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

        var result = new Mat();
        Cv2.CvtColor(gray, result, ColorConversionCodes.GRAY2BGR);
        return result;
    }

    /// <summary>
    /// 与えられたしきい値で白黒に分ける。
    /// </summary>
    private static Mat ApplyBinarize(Mat source, double threshold)
    {
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

        using var binary = new Mat();
        Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);

        var result = new Mat();
        Cv2.CvtColor(binary, result, ColorConversionCodes.GRAY2BGR);
        return result;
    }

    /// <summary>
    /// 二値化のしきい値を決める。
    /// </summary>
    /// <remarks>
    /// <b>必ず <see cref="CanonicalEdge" /> に縮小した画像から求める。</b>
    /// プレビューは縮小画像、書き出しは原寸を処理するので、それぞれの画素から
    /// 大津を求めると値が食い違う。二値化はしきい値 1 階調のずれで広い面積が反転しうる
    /// （実測で、谷の狭いヒストグラムでは 1 階調の差で 11.6% の画素が反転した）。
    /// <para>
    /// プレビューの入力は既に canonical そのものなので、そのときは計算済みの
    /// <paramref name="current" /> をそのまま使う。書き出しのときだけ、元画像を
    /// canonical へ縮めてチェーンを流し直す。
    /// </para>
    /// <para>
    /// <b>2 経路が同じ画素を見ることは、二重定義を無くすことで保証している。</b>
    /// 遅い経路は「設定から二値化以降を落とした写し」ではなく、<see cref="Apply" /> に
    /// <c>stopBeforeBinarize</c> を渡して同じチェーンを途中で打ち切る。
    /// 「二値化の手前までのチェーン」の定義が 1 箇所しか無くなるので、順序が二値化より
    /// 後ろの項目を足しても、それは打ち切りの return より下に書かれ、しきい値の計算へは
    /// 混ざりようがない。
    /// </para>
    /// <para>
    /// 写しを作る作りだったときは、後段を落とし忘れると静かに食い違った。実際、束 3 で
    /// シャープ（順序 7）を足したときに落とし忘れ、束 4 まで気づかなかった
    /// （実測で 161 と 150）。<b>「同じ入力に同じ処理を掛けるから一致する」という説明は、
    /// 前半のチェーンの実装が 1 本しかないときにだけ成り立つ。</b>
    /// </para>
    /// <para>
    /// 代償として、しきい値は原寸ではなく縮小画像から決まる。原寸の大津のほうが
    /// 良い値を出す場面はありうるが、ユーザーが見て納得したのは縮小画像から
    /// 決まった値のほうである。「見たとおりに出る」を優先する。
    /// </para>
    /// </remarks>
    private static double ResolveThreshold(
        Mat original, ProcessingSettings settings, RenderContext context, Mat current)
    {
        var canonicalScale = ScaleFor(context.OriginalWidth, context.OriginalHeight, CanonicalEdge);

        // プレビュー経路。入力が既に canonical なので、いま作った結果がそのまま使える
        if (context.PreviewScale == canonicalScale)
        {
            return Otsu(current);
        }

        // 書き出し経路。元画像を canonical へ縮めて、同じチェーンを流し直す
        using var canonical = Downscale(original, CanonicalEdge);
        if (canonical is null)
        {
            // 元画像が canonical 以下。縮小が要らないので現在の結果がそのまま canonical
            return Otsu(current);
        }

        // 書き出し経路。元画像を canonical へ縮めて、同じチェーンを二値化の手前まで流す。
        // 速い経路が渡してくる current と同じ工程をたどるが、その「同じ」は
        // 引数で打ち切っているという意味であって、書き写しではない
        using var canonicalResult = Apply(
            canonical,
            settings,
            new RenderContext(context.OriginalWidth, context.OriginalHeight, canonicalScale),
            stopBeforeBinarize: true);

        return Otsu(canonicalResult);
    }

    /// <summary>大津の方法でしきい値を求める。</summary>
    private static double Otsu(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        using var binary = new Mat();
        return Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
    }

    /// <summary>
    /// しきい値の計算に使う canonical 画像を、可逆形式（PNG）で返す。
    /// </summary>
    /// <returns>PNG のバイト列。</returns>
    /// <remarks>
    /// 検証用。プレビュー経路と書き出し経路で canonical 画像が同じ画素になることを、
    /// <c>Mat</c> を外へ出さずに確かめられるようにするために持つ。
    /// <b>2 つの経路で作った結果がバイト一致しなければ、しきい値の一致は保証されない。</b>
    /// </remarks>
    public byte[] EncodeCanonical(RenderSource source)
    {
        var canonical = Downscale(source.Image, CanonicalEdge);
        try
        {
            Cv2.ImEncode(".png", canonical ?? source.Image, out var bytes, [(int)ImwriteFlags.PngCompression, 1]);
            return bytes;
        }
        finally
        {
            canonical?.Dispose();
        }
    }

    /// <summary>
    /// 明るさとコントラストをまとめた対応表を作る。
    /// </summary>
    /// <remarks>
    /// 画素ごとに計算すると 1 画素あたり何度も走るため、0〜255 の変換表を 1 度だけ作って
    /// <c>Cv2.LUT</c> で一括変換する。<b>2 つを別々の LUT で通さないこと。</b>
    /// 中間で 2 度丸めが入り、階調がわずかに崩れる。
    /// <para>
    /// <b>コントラストを先に、明るさを後に</b>掛ける。逆にすると明るさの効きが
    /// コントラストの倍率だけ増減し、「コントラストを上げたら明るさスライダーの
    /// 効き方まで変わった」という挙動になる。この順なら明るさは常にちょうど
    /// <paramref name="brightness" /> だけ動く。
    /// </para>
    /// <para>
    /// 中心は 128 ではなく 127.5。0 と 255 が対称に動くようにするため。
    /// </para>
    /// <para>
    /// <b>倍率は <c>2^(contrast/100)</c>。</b>-100 で 0.5 倍、0 で 1 倍、+100 で 2 倍と、
    /// 倍率が対称になる。<c>1 + contrast/100</c> だと -100 で 0 倍になり、
    /// 一面が中間の明るさに潰れて画像の情報が完全に消える。-50 の時点で 0.5 倍と
    /// かなり平坦で、スライダーの下半分の大部分が実用にならない領域になっていた。
    /// </para>
    /// </remarks>
    /// <param name="brightness">加算する量（-100〜100）。0 で変化なし。</param>
    /// <param name="contrast">コントラストの強さ（-100〜100）。0 で変化なし。</param>
    private static Mat BuildToneLut(int brightness, int contrast)
    {
        const double center = 127.5;

        // contrast が 0 なら 2^0 = 1 になり、明るさ単独の結果は変わらない
        var scale = Math.Pow(2.0, contrast / 100.0);

        var lut = new Mat(1, 256, MatType.CV_8UC1);
        for (var i = 0; i < 256; i++)
        {
            var value = ((i - center) * scale) + center + brightness;
            lut.Set(0, i, (byte)Math.Clamp(Math.Round(value), 0, 255));
        }

        return lut;
    }

    /// <summary>
    /// 彩度を適用した結果を新しい <see cref="Mat" /> として返す。
    /// </summary>
    /// <remarks>
    /// HSV や Lab へは変換しない。8bit で色空間を往復すると、量子化のせいで
    /// それだけで画素が変わってしまう。ここでは<b>輝度との線形補間</b>で済ませる。
    /// <para>
    /// 倍率 0（値 -100）の結果は <c>BGR2GRAY</c> と完全に一致する。後から入る
    /// グレースケールと地続きになり、「彩度 -100」と「グレースケール ON」で
    /// 違う絵が出るという食い違いが起きない。
    /// </para>
    /// </remarks>
    private static Mat ApplySaturation(Mat source, int value)
    {
        var scale = 1.0 + (value / 100.0);

        // プレビューは操作のたびに走る。ここで破棄を漏らすとネイティブメモリが増え続ける
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);

        using var gray3 = new Mat();
        Cv2.CvtColor(gray, gray3, ColorConversionCodes.GRAY2BGR);

        // 出力 = 元の色 × scale + グレー × (1 - scale)。飽和は OpenCV 側が行う
        var result = new Mat();
        Cv2.AddWeighted(source, scale, gray3, 1.0 - scale, 0.0, result);
        return result;
    }
}
