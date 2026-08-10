using System.Diagnostics;
using System.IO;
using OpenCvSharp;
using Soroe.Models;
using Soroe.Services;

/// <summary>
/// 性能の測定。<c>--bench</c> を付けたときだけ走る。
/// </summary>
/// <remarks>
/// 手で測っては文書に書き写す、という手順を繰り返した結果、数字が古いまま残り、
/// しかもそれが判断の根拠として誤りになることが実際に起きた。1 コマンドで測り直せる
/// ようにして、性能に関わる変更をしたら必ず走らせる。
/// <para>
/// 通常の検証には含めない。全部で 1 分近くかかるためで、変更のたびに待たされると
/// 検証そのものを回さなくなる。
/// </para>
/// <para>
/// 標本は既定では合成画像を作る。リポジトリの外のファイルに依存させないため。
/// 実写で測りたいときは画像のパスを渡す（<c>--bench 画像.jpg</c>）。
/// <b>絶対値は標本と機械に依存する。</b>比較して意味があるのは同じ条件で測った値どうしだけ。
/// </para>
/// </remarks>
internal static class Bench
{
    /// 計測の繰り返し回数。最初の <see cref="Warmup" /> 回は捨てる
    private const int Repeat = 7;

    /// JIT とファイルキャッシュの影響を外すために捨てる回数
    private const int Warmup = 2;

    public static void Run(string? samplePath)
    {
        var work = Path.Combine(Path.GetTempPath(), "soroe_bench");
        if (Directory.Exists(work))
        {
            Directory.Delete(work, true);
        }

        Directory.CreateDirectory(work);

        Console.WriteLine($"Soroe 性能測定  {DateTime.Now:yyyy-MM-dd HH:mm}");
        Console.WriteLine($"構成: {Configuration}  繰り返し {Repeat} 回の中央値（最初の {Warmup} 回は捨てる）");
        Console.WriteLine();

        var photo = Path.Combine(work, "photo.png");
        if (samplePath is not null && File.Exists(samplePath))
        {
            photo = Path.Combine(work, "photo" + Path.GetExtension(samplePath));
            File.Copy(samplePath, photo);
            Console.WriteLine($"写真の標本: {samplePath}（指定）");
        }
        else
        {
            WritePhotoLike(photo, 3000, 4000);
            Console.WriteLine("写真の標本: 合成（3000x4000）");
            Console.WriteLine(
                "  注意: 合成の写真は PNG の圧縮率の差を小さく見せる（実写では 1 と 6 で 4 倍以上の");
            Console.WriteLine(
                "  時間差が出るが、合成では 1.2 倍程度）。圧縮率の判断をやり直すときは実写を渡すこと。");
        }

        var flat = Path.Combine(work, "flat.png");
        WriteFlat(flat, 3000, 4000);
        var shot = Path.Combine(work, "shot.png");
        WriteFlat(shot, 1500, 1080);
        Console.WriteLine("平坦の標本: 合成（3000x4000 と 1500x1080、単色面 + 細線）");

        var renderer = new ImageRenderer();

        BenchFormats(renderer, photo, work);
        BenchPngCompression(photo, flat, shot);
        BenchPreview(renderer, photo);
        BenchBinarize(renderer, photo, flat, work);

        Directory.Delete(work, true);

        Console.WriteLine();
        Console.WriteLine("これらの数字を文書に書き写すときは、測定日とこのコマンドを添えること。");
    }

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>出力形式ごとの、書き出し 1 枚あたりの時間。</summary>
    private static void BenchFormats(ImageRenderer renderer, string photo, string work)
    {
        Console.WriteLine();
        Console.WriteLine("■ 形式ごとの書き出し 1 枚（読み込み + チェーン + エンコード + 書き込み）");

        var settings = new ProcessingSettings();
        foreach (var extension in new[] { ".jpg", ".png", ".webp", ".bmp" })
        {
            var elapsed = Median(() => ExportOnce(renderer, photo, work, settings, extension));
            Console.WriteLine($"  {extension,-6} {elapsed,8:F0}ms");
        }
    }

    /// <summary>PNG の圧縮率ごとの時間とサイズ。</summary>
    /// <remarks>
    /// 現在の値は <c>ImageRenderer</c> の中で固定している。ここを変えるときは
    /// この表を取り直すこと。
    /// </remarks>
    private static void BenchPngCompression(string photo, string flat, string shot)
    {
        Console.WriteLine();
        Console.WriteLine("■ PNG の圧縮率（エンコードのみ）");

        foreach (var (label, path) in new[]
                 {
                     ("写真", photo), ("平坦 3000x4000", flat), ("平坦 1500x1080", shot),
                 })
        {
            using var image = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
            Console.WriteLine($"  {label}");

            foreach (var level in new[] { 1, 3, 4, 6, 9 })
            {
                var size = 0;
                var elapsed = Median(() =>
                {
                    Cv2.ImEncode(".png", image, out var bytes, [(int)ImwriteFlags.PngCompression, level]);
                    size = bytes.Length;
                });

                Console.WriteLine($"    圧縮率 {level}  {elapsed,7:F0}ms  {size / 1024.0 / 1024.0,7:F2}MB");
            }
        }
    }

    /// <summary>プレビュー 1 枚のレンダリング。</summary>
    private static void BenchPreview(ImageRenderer renderer, string photo)
    {
        Console.WriteLine();
        Console.WriteLine($"■ プレビュー 1 枚のレンダリング（長辺 {ImageRenderer.CanonicalEdge} に縮小済みの画像に対して）");

        using var source = renderer.Load(photo, ImageRenderer.CanonicalEdge);

        foreach (var (label, configure) in PreviewCases())
        {
            var settings = new ProcessingSettings();
            configure(settings);
            var elapsed = Median(() => renderer.Render(source!, settings, source!.Scale));
            Console.WriteLine($"  {label,-30} {elapsed,7:F1}ms");
        }
    }

    /// <summary>二値化を有効にしたときの、書き出しの増分。</summary>
    private static void BenchBinarize(ImageRenderer renderer, string photo, string flat, string work)
    {
        Console.WriteLine();
        Console.WriteLine("■ 二値化の増分（書き出し 1 枚、JPEG 出力）");

        foreach (var (label, path) in new[] { ("写真 3000x4000", photo), ("平坦 3000x4000", flat) })
        {
            var off = new ProcessingSettings();
            var on = new ProcessingSettings();
            on.Binarize.Enabled = true;

            var withoutBinarize = Median(() => ExportOnce(renderer, path, work, off, ".jpg"));
            var withBinarize = Median(() => ExportOnce(renderer, path, work, on, ".jpg"));

            Console.WriteLine(
                $"  {label,-16} OFF {withoutBinarize,7:F0}ms  ON {withBinarize,7:F0}ms  "
                + $"増分 {withBinarize - withoutBinarize,6:F0}ms（{(withBinarize - withoutBinarize) / withoutBinarize * 100,5:F1}%）");
        }
    }

    private static IEnumerable<(string Label, Action<ProcessingSettings> Configure)> PreviewCases()
    {
        yield return ("調整なし", _ => { });
        yield return ("明るさのみ", s =>
        {
            s.Brightness.Enabled = true;
            s.Brightness.Value = 30;
        });
        yield return ("明るさ + コントラスト + 彩度", s =>
        {
            s.Brightness.Enabled = true;
            s.Brightness.Value = 30;
            s.Contrast.Enabled = true;
            s.Contrast.Value = -20;
            s.Saturation.Enabled = true;
            s.Saturation.Value = 40;
        });
        yield return ("グレースケール + 二値化", s =>
        {
            s.Grayscale.Enabled = true;
            s.Binarize.Enabled = true;
        });
        yield return ("実装済み 6 項目すべて", s =>
        {
            s.Resize.Enabled = true;
            s.Resize.LongestEdge = 1280;
            s.Brightness.Enabled = true;
            s.Brightness.Value = 30;
            s.Contrast.Enabled = true;
            s.Contrast.Value = -20;
            s.Saturation.Enabled = true;
            s.Saturation.Value = 40;
            s.Grayscale.Enabled = true;
            s.Binarize.Enabled = true;
        });
    }

    private static void ExportOnce(
        ImageRenderer renderer, string path, string work, ProcessingSettings settings, string extension)
    {
        using var source = renderer.Load(path, 0);
        var bytes = renderer.Encode(
            source!, settings, source!.Scale, new EncodeSettings { Extension = extension, JpegQuality = 95 });
        File.WriteAllBytes(Path.Combine(work, "out" + extension), bytes);
    }

    private static double Median(Action action)
    {
        var times = new List<double>();
        for (var i = 0; i < Repeat; i++)
        {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            if (i >= Warmup)
            {
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
        }

        times.Sort();
        return times[times.Count / 2];
    }

    /// <summary>
    /// 写真に近い性質の画像を作る。
    /// </summary>
    /// <remarks>
    /// <b>画素ごとの乱数にしないこと。</b>写真は隣り合う画素に相関があり、そこが
    /// 圧縮の効き方を決める。画素ごとの乱数だと PNG が全く縮まず（実測で 27MB）、
    /// 圧縮率を変えても差が出ないため、標本として役に立たない。
    /// <para>
    /// 低い解像度で作った乱数を滑らかに引き伸ばして相関を作り、そこへ細かい濃淡を
    /// 少しだけ足す。
    /// </para>
    /// </remarks>
    private static void WritePhotoLike(string path, int width, int height)
    {
        using var seed = new Mat(height / 10, width / 10, MatType.CV_8UC3);
        Cv2.Randu(seed, Scalar.All(0), Scalar.All(255));

        using var mat = new Mat();
        Cv2.Resize(seed, mat, new Size(width, height), 0, 0, InterpolationFlags.Cubic);

        // 引き伸ばしただけでは滑らかすぎるので、粒状感を少し足す
        using var grain = new Mat(height, width, MatType.CV_8UC3);
        Cv2.Randu(grain, Scalar.All(0), Scalar.All(16));
        Cv2.Add(mat, grain, mat);

        Cv2.ImEncode(".png", mat, out var png, [(int)ImwriteFlags.PngCompression, 1]);
        File.WriteAllBytes(path, png);
    }

    /// スクリーンショットや図に近い性質（大きな単色面 + 細線）
    private static void WriteFlat(string path, int width, int height)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(245, 245, 245));
        int rows = mat.Rows, cols = mat.Cols;

        for (var y = 0; y < rows; y++)
        {
            if (y / 200 % 2 == 0)
            {
                continue;
            }

            for (var x = 0; x < cols; x++)
            {
                mat.Set(y, x, new Vec3b(230, 225, 220));
            }
        }

        for (var y = 30; y < rows - 30; y += 24)
        {
            for (var x = 30; x < cols - 30; x += 4)
            {
                if ((x / 4) % 9 == 0)
                {
                    continue;
                }

                mat.Set(y, x, new Vec3b(40, 40, 40));
                mat.Set(y + 1, x, new Vec3b(40, 40, 40));
            }
        }

        Cv2.ImEncode(".png", mat, out var png, [(int)ImwriteFlags.PngCompression, 1]);
        File.WriteAllBytes(path, png);
    }
}
